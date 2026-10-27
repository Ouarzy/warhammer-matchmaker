using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Matches;

/// <summary>
/// Matchs acceptés, enregistrés en base : déclaration et confirmation du résultat, Elo, historique, classement.
/// </summary>
public sealed class MatchService(MatchmakerDbContext db, TimeProvider time)
{
    private static readonly MatchStatus[] InProgress = [MatchStatus.Accepted, MatchStatus.ResultDeclared];
    private static readonly MatchStatus[] Finished = [MatchStatus.Completed, MatchStatus.Disputed, MatchStatus.Abandoned];

    public async Task StartAsync(Match match, CancellationToken cancellationToken = default)
    {
        if (match.Status != MatchStatus.Accepted)
            throw new InvalidOperationException("Seul un match accepté par les deux joueurs peut commencer.");

        db.Matches.Add(match);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Match?> ActiveMatchOfAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        await db.Matches
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .Where(m => InProgress.Contains(m.Status))
            .OrderByDescending(m => m.ProposedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Match> DeclareResultAsync(PlayerId player, Guid matchId, int myVictoryPoints, int opponentVictoryPoints,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.DeclareResult(player, myVictoryPoints, opponentVictoryPoints, time.GetUtcNow()), cancellationToken);

    /// <summary>Confirme le résultat et, pour un match classé, met à jour l'Elo des deux joueurs dans la même transaction.</summary>
    public async Task<Match> ConfirmResultAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default)
    {
        var match = await ParticipatingMatchAsync(player, matchId, cancellationToken);
        match.ConfirmResult(player);

        if (match.AffectsRating)
        {
            var first = await PlayerAsync(match.First.Player, cancellationToken);
            var second = await PlayerAsync(match.Second.Player, cancellationToken);
            MatchRating.Apply(match, first, second);
        }

        await db.SaveChangesAsync(cancellationToken);
        return match;
    }

    public Task<Match> DisputeResultAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.DisputeResult(player), cancellationToken);

    public Task<Match> AbandonAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken = default) =>
        UpdateAsync(player, matchId, match => match.Abandon(player), cancellationToken);

    public async Task<IReadOnlyList<Match>> HistoryOfAsync(PlayerId player, int limit = 20, CancellationToken cancellationToken = default) =>
        await db.Matches
            .Where(m => m.First.Player == player || m.Second.Player == player)
            .Where(m => Finished.Contains(m.Status))
            .OrderByDescending(m => m.ProposedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Player>> RankingAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        await db.Players
            .Where(p => p.RankedGamesPlayed > 0)
            .OrderByDescending(p => p.Rating)
            .ThenByDescending(p => p.RankedGamesPlayed)
            .Take(limit)
            .ToListAsync(cancellationToken);

    private async Task<Match> UpdateAsync(PlayerId player, Guid matchId, Action<Match> change, CancellationToken cancellationToken)
    {
        var match = await ParticipatingMatchAsync(player, matchId, cancellationToken);
        change(match);
        await db.SaveChangesAsync(cancellationToken);
        return match;
    }

    private async Task<Match> ParticipatingMatchAsync(PlayerId player, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await db.Matches.FindAsync([matchId], cancellationToken);
        if (match is null || (match.First.Player != player && match.Second.Player != player))
            throw new InvalidOperationException("Ce match n'existe pas ou ne vous concerne pas.");
        return match;
    }

    private async Task<Player> PlayerAsync(PlayerId id, CancellationToken cancellationToken) =>
        await db.Players.FindAsync([id], cancellationToken)
        ?? throw new InvalidOperationException($"Joueur {id.SteamId} introuvable.");
}
