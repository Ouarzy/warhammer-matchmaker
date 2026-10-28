using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Matchmaker.Web.Matches;
using Matchmaker.Web.Push;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Matchmaking;

/// <summary>Recherches ouvertes d'un joueur et parties planifiées qu'on lui propose.</summary>
public sealed record ScheduledOverview(IReadOnlyList<ScheduledSearch> OpenSearches, IReadOnlyList<Match> Proposals);

/// <summary>
/// Parties planifiées à une date et une heure précises, enregistrées en base : les joueurs ne sont
/// pas en ligne en même temps. Une proposition reste 24 h à accepter (au plus tard 1 h avant la partie) ;
/// une fois acceptée, la partie se déroule comme une partie immédiate.
/// </summary>
public sealed class ScheduledGames(MatchmakerDbContext db, MatchService matches, PlayerNotifier notifier, PushOutbox push, TimeProvider time)
{
    // Une seule instance du site : sérialiser les appariements évite de proposer deux fois la même recherche.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly ScheduledSearchStatus Open = ScheduledSearchStatus.Open;

    /// <summary>Ce qu'une opération change : joueurs à prévenir et notifications push, envoyées une fois les changements enregistrés.</summary>
    private sealed class Changes
    {
        public List<PlayerId> Players { get; } = [];
        public List<(PlayerId, PushMessage)> Pushes { get; } = [];
    }

    public async Task<ScheduledSearch> PlanAsync(PlayerId player, Guid armyListId, bool ranked, DateTimeOffset plannedFor,
        CancellationToken cancellationToken = default)
    {
        var account = await db.Players.FindAsync([player], cancellationToken)
            ?? throw new InvalidOperationException("Joueur introuvable.");
        var list = await db.ArmyLists.FindAsync([armyListId], cancellationToken);
        if (list is null || !list.IsOwnedBy(player))
            throw new InvalidOperationException("Cette liste ne vous appartient pas.");

        return await SerializedAsync(async changed =>
        {
            if (await matches.CommitmentsOfAsync(player, cancellationToken) >= MatchService.MaxActiveMatches)
                throw new InvalidOperationException(
                    $"Vous avez déjà {MatchService.MaxActiveMatches} parties en cours, planifiées ou recherchées.");

            var now = time.GetUtcNow();
            // PostgreSQL n'enregistre que des dates UTC ; l'heure saisie arrive dans le fuseau du joueur.
            var search = ScheduledSearch.Open(new QueueTicket(player, list.Id, list.Format, ranked, account.Rating, now), plannedFor.ToUniversalTime(), now);
            db.ScheduledSearches.Add(search);
            changed.Players.Add(player);
            await TryMatchAsync(search, now, changed, cancellationToken);
            return search;
        }, cancellationToken);
    }

    public Task CancelAsync(PlayerId player, Guid searchId, CancellationToken cancellationToken = default) =>
        SerializedAsync(async changed =>
        {
            var search = await db.ScheduledSearches.FindAsync([searchId], cancellationToken);
            if (search is null || search.Ticket.Player != player)
                throw new InvalidOperationException("Cette recherche n'existe pas ou ne vous concerne pas.");
            search.Cancel();
            changed.Players.Add(player);
            return 0;
        }, cancellationToken);

    /// <returns>Le match, au statut Accepted une fois accepté par les deux joueurs.</returns>
    public Task<Match> AcceptAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        SerializedAsync(async changed =>
        {
            var match = await ProposalAsync(player, matchId, cancellationToken);
            match.Accept(player, time.GetUtcNow());
            if (match.Status == MatchStatus.Accepted)
            {
                await matches.PrepareStartAsync(match, cancellationToken);
                changed.Pushes.AddRange([(match.First.Player, PushMessages.ScheduledConfirmed(match)), (match.Second.Player, PushMessages.ScheduledConfirmed(match))]);
            }
            changed.Players.AddRange([match.First.Player, match.Second.Player]);
            return match;
        }, cancellationToken);

    public Task DeclineAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        SerializedAsync(async changed =>
        {
            var match = await ProposalAsync(player, matchId, cancellationToken);
            var stillSearching = match.Decline(player).Select(ticket => ticket.Player).ToList();
            await SettleSearchesAsync(match, stillSearching, changed, cancellationToken);
            return 0;
        }, cancellationToken);

    /// <summary>À appeler régulièrement : propositions non acceptées à temps, recherches devenues trop tardives.</summary>
    public Task ExpireOverdueAsync(CancellationToken cancellationToken = default) =>
        SerializedAsync(async changed =>
        {
            var now = time.GetUtcNow();
            var proposals = await db.Matches
                .Where(m => m.Status == MatchStatus.Proposed && m.ScheduledFor != null)
                .ToListAsync(cancellationToken);
            foreach (var match in proposals.Where(m => now > m.AcceptanceDeadline))
            {
                var stillSearching = match.Expire(now).Select(ticket => ticket.Player).ToList();
                await SettleSearchesAsync(match, stillSearching, changed, cancellationToken);
            }

            var latest = now + Match.ScheduledAcceptanceNotice;
            var tooLate = await db.ScheduledSearches
                .Where(s => s.Status == Open && s.PlannedFor < latest)
                .ToListAsync(cancellationToken);
            foreach (var search in tooLate.Where(s => s.ExpireIfTooLate(now)))
                changed.Players.Add(search.Ticket.Player);
            return 0;
        }, cancellationToken);

    public async Task<ScheduledOverview> OverviewAsync(PlayerId player, CancellationToken cancellationToken = default)
    {
        var searches = await db.ScheduledSearches
            .Where(s => s.Ticket.Player == player && s.Status == Open)
            .OrderBy(s => s.PlannedFor)
            .ToListAsync(cancellationToken);
        return new ScheduledOverview(searches, await ProposalsOfAsync(player, cancellationToken));
    }

    /// <summary>Propositions que le joueur n'a pas encore acceptées : pastille du menu.</summary>
    public async Task<int> ProposalsToAnswerAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        (await ProposalsOfAsync(player, cancellationToken)).Count(m => !m.HasAccepted(player));

    private async Task<List<Match>> ProposalsOfAsync(PlayerId player, CancellationToken cancellationToken) =>
        await db.Matches
            .Where(m => m.Status == MatchStatus.Proposed && m.ScheduledFor != null)
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .OrderBy(m => m.ScheduledFor)
            .ToListAsync(cancellationToken);

    private async Task<Match> ProposalAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await matches.FindForAsync(player, matchId, cancellationToken);
        return match is { Status: MatchStatus.Proposed, ScheduledFor: not null }
            ? match
            : throw new InvalidOperationException("Cette proposition n'existe plus ou ne vous concerne pas.");
    }

    private async Task TryMatchAsync(ScheduledSearch search, DateTimeOffset now, Changes changed, CancellationToken cancellationToken)
    {
        var (from, to) = (search.PlannedFor - ScheduledSearch.Tolerance, search.PlannedFor + ScheduledSearch.Tolerance);
        var candidates = await db.ScheduledSearches
            .Where(s => s.Status == Open && s.Id != search.Id && s.Ticket.Player != search.Ticket.Player)
            .Where(s => s.PlannedFor >= from && s.PlannedFor <= to)
            .Where(s => s.Ticket.Format.Name == search.Ticket.Format.Name && s.Ticket.Ranked == search.Ticket.Ranked)
            .ToListAsync(cancellationToken);
        if (search.BestOpponentAmong(candidates, now) is not { } opponent) return;

        var match = search.MatchWith(opponent, now);
        db.Matches.Add(match);
        changed.Players.AddRange([match.First.Player, match.Second.Player]);
        changed.Pushes.AddRange([(match.First.Player, PushMessages.ScheduledProposal(match)), (match.Second.Player, PushMessages.ScheduledProposal(match))]);
    }

    // Après un refus ou une expiration : la recherche des joueurs qui voulaient jouer repart, les autres s'arrêtent.
    private async Task SettleSearchesAsync(Match match, IReadOnlyList<PlayerId> stillSearching, Changes changed,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var searches = await db.ScheduledSearches.Where(s => s.MatchId == match.Id).ToListAsync(cancellationToken);
        foreach (var search in searches)
        {
            changed.Players.Add(search.Ticket.Player);
            if (!stillSearching.Contains(search.Ticket.Player))
            {
                search.Close();
                continue;
            }
            search.Reopen(now);
            if (search.Status != Open)
            {
                changed.Pushes.Add((search.Ticket.Player, PushMessages.SearchStopped(search.PlannedFor)));
                continue;
            }
            await TryMatchAsync(search, now, changed, cancellationToken);
            if (search.Status == Open)
                changed.Pushes.Add((search.Ticket.Player, PushMessages.SearchGoesOn(search.PlannedFor)));
        }
    }

    private async Task<T> SerializedAsync<T>(Func<Changes, Task<T>> work, CancellationToken cancellationToken)
    {
        var changed = new Changes();
        T result;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            result = await work(changed);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
        notifier.Notify(changed.Players);
        push.Send(changed.Pushes);
        return result;
    }
}
