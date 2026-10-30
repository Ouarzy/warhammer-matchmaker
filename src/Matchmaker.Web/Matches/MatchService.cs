using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Data;
using Matchmaker.Web.Tts;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Matches;

/// <summary>
/// Matchs acceptés, enregistrés en base : déclaration et confirmation du résultat, abandon, Elo,
/// historique, classement. Un joueur peut avoir plusieurs matchs en cours, sans limite de durée.
/// </summary>
public sealed class MatchService(MatchmakerDbContext db, TimeProvider time)
{
    public const int MaxActiveMatches = 5;

    /// <summary>Message affiché quand l'adversaire, ou la confirmation automatique, a modifié la partie au même moment.</summary>
    public const string ConcurrentChangeMessage = "La partie vient de changer (votre adversaire a agi au même moment) : vérifiez son état, puis recommencez si besoin.";

    private static readonly MatchStatus[] InProgress = [MatchStatus.Accepted, MatchStatus.ResultDeclared];
    private static readonly MatchStatus[] Finished = [MatchStatus.Completed, MatchStatus.Abandoned];

    public async Task StartAsync(Match match, CancellationToken cancellationToken = default)
    {
        await PrepareStartAsync(match, cancellationToken);
        db.Matches.Add(match);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fige les listes jouées et crée le code TTS du match qui commence, sans enregistrer :
    /// l'appelant enregistre le tout dans la même transaction.
    /// </summary>
    public async Task PrepareStartAsync(Match match, CancellationToken cancellationToken = default)
    {
        if (match.Status != MatchStatus.Accepted)
            throw new InvalidOperationException("Seul un match accepté par les deux joueurs peut commencer.");

        var listIds = new[] { match.First.ArmyListId, match.Second.ArmyListId };
        var lists = await db.ArmyLists.Where(l => listIds.Contains(l.Id)).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        db.ArchivedArmyLists.AddRange(lists.Select(list => list.ArchiveFor(match.Id, now)));
        // Code secret pour envoyer le score depuis l'objet TTS.
        db.ReportCodes.Add(new ReportCode(match.Id, ReportCode.Generate()));
    }

    public async Task<ArchivedLists> ArchivedListsOfAsync(IEnumerable<Guid> matchIds, CancellationToken cancellationToken = default)
    {
        var ids = matchIds.Distinct().ToList();
        return new ArchivedLists(await db.ArchivedArmyLists.Where(a => ids.Contains(a.MatchId)).ToListAsync(cancellationToken));
    }

    public async Task<ArchivedArmyList?> FindArchivedListAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.ArchivedArmyLists.FindAsync([id], cancellationToken);

    public async Task<Match?> FindAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.Matches.FindAsync([matchId], cancellationToken);

    /// <summary>Le match, s'il existe et si ce joueur y participe.</summary>
    public async Task<Match?> FindForAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        return match is not null && (match.First.Player == player || match.Second.Player == player) ? match : null;
    }

    public async Task<IReadOnlyList<Match>> ActiveMatchesOfAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        await db.Matches
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .Where(m => InProgress.Contains(m.Status))
            .OrderByDescending(m => m.ProposedAt)
            .ToListAsync(cancellationToken);

    public async Task<bool> CanLookForAnotherOpponentAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        await CommitmentsOfAsync(player, cancellationToken) < MaxActiveMatches;

    /// <summary>
    /// Ce qui compte dans la limite de <see cref="MaxActiveMatches"/>, pour une recherche immédiate comme planifiée :
    /// parties en cours, parties planifiées proposées et recherches planifiées ouvertes.
    /// </summary>
    public async Task<int> CommitmentsOfAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        await db.Matches
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .CountAsync(m => InProgress.Contains(m.Status) || (m.Status == MatchStatus.Proposed && m.ScheduledFor != null), cancellationToken)
        + await db.ScheduledSearches.CountAsync(s => s.Ticket.Player == player && s.Status == ScheduledSearchStatus.Open, cancellationToken);

    public Task<Match> DeclareResultAsync(PlayerId player, Guid matchId, int myVictoryPoints, int opponentVictoryPoints,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.DeclareResult(player, myVictoryPoints, opponentVictoryPoints, time.GetUtcNow()), cancellationToken);

    /// <summary>Confirme le résultat et, pour un match classé, met à jour l'Elo des deux joueurs dans la même transaction.</summary>
    public Task<Match> ConfirmResultAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.ConfirmResult(player), cancellationToken);

    /// <summary>Conteste le score proposé en en proposant un autre, que l'adversaire doit à son tour confirmer ou contester.</summary>
    public Task<Match> ContestResultAsync(PlayerId player, Guid matchId, int myVictoryPoints, int opponentVictoryPoints,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.ContestResult(player, myVictoryPoints, opponentVictoryPoints, time.GetUtcNow()), cancellationToken);

    /// <summary>Abandon : victoire 20-0 pour l'adversaire, Elo mis à jour pour un match classé.</summary>
    public Task<Match> AbandonAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.Abandon(player), cancellationToken);

    /// <summary>Confirme les résultats restés sans réponse plus de 24 h.</summary>
    /// <returns>Les matchs confirmés automatiquement.</returns>
    public async Task<IReadOnlyList<Match>> ConfirmOverdueResultsAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var declaredBefore = now - Match.ResultConfirmationDelay;
        var overdue = await db.Matches
            .Where(m => m.Status == MatchStatus.ResultDeclared && m.DeclaredAt < declaredBefore)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        // Chaque match est relu et enregistré seul : un conflit sur l'un ne laisse rien en suspens pour les suivants
        // (sinon l'Elo d'un match pouvait être enregistré sans son statut, puis appliqué une seconde fois).
        var confirmed = new List<Match>();
        foreach (var matchId in overdue)
        {
            db.ChangeTracker.Clear();
            var match = await db.Matches.FindAsync([matchId], cancellationToken);
            if (match is not { Status: MatchStatus.ResultDeclared } || now <= match.ResultConfirmationDeadline)
                continue;
            try
            {
                match.ConfirmAutomatically(now);
                await ApplyRatingAsync(match, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                confirmed.Add(match);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Un joueur a agi entre-temps : son action l'emporte, le match sera revu au prochain passage.
            }
        }
        db.ChangeTracker.Clear();
        return confirmed;
    }

    public async Task<IReadOnlyList<Match>> HistoryOfAsync(PlayerId player, int limit = 20, CancellationToken cancellationToken = default) =>
        await FinishedMatchesOf(player).Take(limit).ToListAsync(cancellationToken);

    /// <summary>Toutes les parties terminées du joueur, de la plus récente à la plus ancienne, page par page.</summary>
    public async Task<Page<Match>> HistoryPageAsync(PlayerId player, int page, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var total = await FinishedMatchesOf(player).CountAsync(cancellationToken);
        var items = await FinishedMatchesOf(player).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new Page<Match>(items, page, pageSize, total);
    }

    private IQueryable<Match> FinishedMatchesOf(PlayerId player) =>
        db.Matches
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .Where(m => Finished.Contains(m.Status))
            .OrderByDescending(m => m.ProposedAt)
            .ThenBy(m => m.Id);

    public const int InProgressPageSize = 50;

    /// <summary>Toutes les parties en cours sur le site, les plus récentes d'abord.</summary>
    public async Task<Page<Match>> InProgressPageAsync(int page, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var inProgress = db.Matches.Where(m => InProgress.Contains(m.Status));
        var total = await inProgress.CountAsync(cancellationToken);
        var items = await inProgress
            .OrderByDescending(m => m.ProposedAt)
            .ThenBy(m => m.Id)
            .Skip((page - 1) * InProgressPageSize)
            .Take(InProgressPageSize)
            .ToListAsync(cancellationToken);
        return new Page<Match>(items, page, InProgressPageSize, total);
    }

    public async Task<IReadOnlyList<Player>> RankingAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        await db.Players
            .Where(p => p.RankedGamesPlayed > 0)
            .OrderByDescending(p => p.Rating)
            .ThenByDescending(p => p.RankedGamesPlayed)
            .Take(limit)
            .ToListAsync(cancellationToken);

    private async Task<Match> UpdateAsync(PlayerId player, Guid matchId, Action<Match> change, CancellationToken cancellationToken)
    {
        var match = await FindForAsync(player, matchId, cancellationToken)
            ?? throw new InvalidOperationException("Ce match n'existe pas ou ne vous concerne pas.");

        change(match);
        await ApplyRatingAsync(match, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return match;
    }

    private async Task ApplyRatingAsync(Match match, CancellationToken cancellationToken)
    {
        if (!match.AffectsRating || match.RatingApplied) return;

        var first = await PlayerAsync(match.First.Player, cancellationToken);
        var second = await PlayerAsync(match.Second.Player, cancellationToken);
        MatchRating.Apply(match, first, second);
    }

    private async Task<Player> PlayerAsync(PlayerId id, CancellationToken cancellationToken) =>
        await db.Players.FindAsync([id], cancellationToken)
        ?? throw new InvalidOperationException($"Joueur {id.SteamId} introuvable.");
}
