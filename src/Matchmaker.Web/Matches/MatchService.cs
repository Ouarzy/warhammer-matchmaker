using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Matches;

/// <summary>
/// Matchs acceptés, enregistrés en base : déclaration et confirmation du résultat, abandon, Elo,
/// historique, classement. Un joueur peut avoir plusieurs matchs en cours, sans limite de durée.
/// </summary>
public sealed class MatchService(MatchmakerDbContext db, TimeProvider time)
{
    public const int MaxActiveMatches = 5;

    private static readonly MatchStatus[] InProgress = [MatchStatus.Accepted, MatchStatus.ResultDeclared];
    private static readonly MatchStatus[] Finished = [MatchStatus.Completed, MatchStatus.Disputed, MatchStatus.Abandoned];

    public async Task StartAsync(Match match, CancellationToken cancellationToken = default)
    {
        if (match.Status != MatchStatus.Accepted)
            throw new InvalidOperationException("Seul un match accepté par les deux joueurs peut commencer.");

        db.Matches.Add(match);
        await db.SaveChangesAsync(cancellationToken);
    }

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
        await db.Matches
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .CountAsync(m => InProgress.Contains(m.Status), cancellationToken) < MaxActiveMatches;

    public Task<Match> DeclareResultAsync(PlayerId player, Guid matchId, int myVictoryPoints, int opponentVictoryPoints,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.DeclareResult(player, myVictoryPoints, opponentVictoryPoints, time.GetUtcNow()), cancellationToken);

    /// <summary>Confirme le résultat et, pour un match classé, met à jour l'Elo des deux joueurs dans la même transaction.</summary>
    public Task<Match> ConfirmResultAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.ConfirmResult(player), cancellationToken);

    public Task<Match> DisputeResultAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.DisputeResult(player), cancellationToken);

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
            .ToListAsync(cancellationToken);

        var confirmed = new List<Match>();
        foreach (var match in overdue)
        {
            try
            {
                match.ConfirmAutomatically(now);
                await ApplyRatingAsync(match, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                confirmed.Add(match);
            }
            catch (DbUpdateConcurrencyException)
            {
                // L'adversaire a répondu entre-temps : sa réponse l'emporte.
                db.ChangeTracker.Clear();
            }
        }
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
