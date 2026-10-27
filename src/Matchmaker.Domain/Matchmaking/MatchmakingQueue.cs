using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.Matchmaking;

/// <summary>
/// File d'attente pour une recherche immédiate. Non thread-safe : l'appelant sérialise les accès.
/// </summary>
public sealed class MatchmakingQueue(int maxRatingGap = int.MaxValue)
{
    private readonly List<QueueTicket> _waiting = [];

    public bool Contains(PlayerId player) => _waiting.Exists(t => t.Player == player);

    public Match? Enqueue(QueueTicket ticket, DateTimeOffset now)
    {
        if (Contains(ticket.Player))
            throw new InvalidOperationException($"Le joueur {ticket.Player.SteamId} est déjà dans la file.");

        var opponent = _waiting
            .Where(waiting => waiting.CanFace(ticket, maxRatingGap))
            .OrderBy(waiting => waiting.RatingGapWith(ticket))
            .ThenBy(waiting => waiting.EnqueuedAt)
            .FirstOrDefault();

        if (opponent is null)
        {
            _waiting.Add(ticket);
            return null;
        }

        _waiting.Remove(opponent);
        return Match.Propose(opponent, ticket, now);
    }

    public bool Leave(PlayerId player) => _waiting.RemoveAll(t => t.Player == player) > 0;
}
