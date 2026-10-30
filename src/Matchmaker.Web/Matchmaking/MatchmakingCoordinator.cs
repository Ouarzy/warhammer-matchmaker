using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;

namespace Matchmaker.Web.Matchmaking;

/// <summary>
/// Recherche et propositions de match, en mémoire (une seule instance du site).
/// Un match accepté par les deux joueurs est rendu à l'appelant pour être enregistré en base,
/// puis libéré avec <see cref="Release"/>. Sérialise les accès au domaine et prévient chaque joueur
/// dont la situation change.
/// </summary>
public sealed class MatchmakingCoordinator(TimeProvider time, PlayerNotifier notifier)
{
    private readonly Lock _gate = new();
    private readonly MatchmakingQueue _queue = new();
    private readonly Dictionary<PlayerId, QueueTicket> _searches = [];
    private readonly Dictionary<PlayerId, Match> _matches = [];

    public PlayerStatus StatusOf(PlayerId player)
    {
        lock (_gate)
        {
            if (_matches.TryGetValue(player, out var match))
                return new PlayerStatus(
                    match.Status == MatchStatus.Proposed ? PlayerActivity.MatchProposed : PlayerActivity.InMatch,
                    Match: ViewOf(match, player));

            return _searches.TryGetValue(player, out var search)
                ? new PlayerStatus(PlayerActivity.Searching, Search: search)
                : PlayerStatus.Idle;
        }
    }

    public IReadOnlyList<QueueTicket> OpenSearches()
    {
        lock (_gate)
            return [.. _searches.Values];
    }

    public void Search(Player player, ArmyList list, bool ranked)
    {
        if (!list.IsOwnedBy(player.Id))
            throw new InvalidOperationException("Cette liste ne vous appartient pas.");

        List<PlayerId> changed;
        lock (_gate)
        {
            if (_searches.ContainsKey(player.Id) || _matches.ContainsKey(player.Id))
                throw new InvalidOperationException("Vous cherchez déjà un adversaire ou avez un match en cours.");

            var now = time.GetUtcNow();
            changed = Enqueue(new QueueTicket(player.Id, list.Id, list.Format, ranked, player.Rating, now), now);
        }
        Notify(changed);
    }

    public void CancelSearch(PlayerId player)
    {
        lock (_gate)
        {
            if (!_queue.Leave(player)) return;
            _searches.Remove(player);
        }
        Notify([player]);
    }

    /// <returns>Le match, une fois accepté par les deux joueurs : à enregistrer puis à libérer.</returns>
    public Match? Accept(PlayerId player, Guid matchId)
    {
        Match match;
        lock (_gate)
        {
            match = MatchOf(player, matchId);
            match.Accept(player, time.GetUtcNow());
        }
        Notify([match.First.Player, match.Second.Player]);
        return match.Status == MatchStatus.Accepted ? match : null;
    }

    public void Decline(PlayerId player, Guid matchId)
    {
        var changed = new List<PlayerId>();
        lock (_gate)
        {
            var match = MatchOf(player, matchId);
            var requeued = match.Decline(player);
            changed.AddRange(Release(match));
            foreach (var ticket in requeued)
                changed.AddRange(Enqueue(ticket, time.GetUtcNow()));
        }
        Notify(changed);
    }

    /// <summary>Libère un match accepté, une fois enregistré en base.</summary>
    public void Release(Guid matchId)
    {
        List<PlayerId> changed;
        lock (_gate)
        {
            var match = _matches.Values.FirstOrDefault(m => m.Id == matchId);
            if (match?.Status != MatchStatus.Accepted)
                throw new InvalidOperationException("Seul un match accepté par les deux joueurs peut être libéré.");
            changed = Release(match);
        }
        Notify(changed);
    }

    /// <summary>À appeler régulièrement : fait expirer les propositions non acceptées à temps.</summary>
    public void ExpireOverdueMatches()
    {
        var changed = new List<PlayerId>();
        lock (_gate)
        {
            var now = time.GetUtcNow();
            var overdue = _matches.Values
                .Distinct()
                .Where(m => m.Status == MatchStatus.Proposed && now > m.AcceptanceDeadline)
                .ToList();

            foreach (var match in overdue)
            {
                var requeued = match.Expire(now);
                changed.AddRange(Release(match));
                foreach (var ticket in requeued)
                    changed.AddRange(Enqueue(ticket, now));
            }
        }
        Notify(changed);
    }

    private List<PlayerId> Enqueue(QueueTicket ticket, DateTimeOffset now)
    {
        var match = _queue.Enqueue(ticket, now);
        if (match is null)
        {
            _searches[ticket.Player] = ticket;
            return [ticket.Player];
        }

        foreach (var player in new[] { match.First.Player, match.Second.Player })
        {
            _searches.Remove(player);
            _matches[player] = match;
        }
        return [match.First.Player, match.Second.Player];
    }

    private List<PlayerId> Release(Match match)
    {
        _matches.Remove(match.First.Player);
        _matches.Remove(match.Second.Player);
        return [match.First.Player, match.Second.Player];
    }

    private Match MatchOf(PlayerId player, Guid matchId) =>
        _matches.TryGetValue(player, out var match) && match.Id == matchId
            ? match
            : throw new InvalidOperationException("Ce match n'existe plus ou ne vous concerne pas.");

    private static MatchView ViewOf(Match match, PlayerId player)
    {
        var opponent = match.OpponentOf(player);
        return new MatchView(
            match.Id,
            match.Status,
            match.AcceptanceDeadline,
            Me: match.First.Player == player ? match.First : match.Second,
            Opponent: opponent,
            IAccepted: match.HasAccepted(player),
            OpponentAccepted: match.HasAccepted(opponent.Player));
    }

    private void Notify(IEnumerable<PlayerId> players) => notifier.Notify(players);
}
