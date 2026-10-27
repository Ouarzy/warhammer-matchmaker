using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Matches;

/// <summary>
/// Cycle de vie d'un match : proposé → accepté par les deux joueurs → résultat déclaré → confirmé.
/// Un match refusé, expiré, abandonné ou contesté n'impacte pas l'Elo.
/// </summary>
public sealed class Match
{
    public static readonly TimeSpan AcceptanceDelay = TimeSpan.FromMinutes(5);

    private readonly HashSet<PlayerId> _accepted = [];
    private readonly Dictionary<PlayerId, int> _victoryPoints = [];
    private PlayerId? _declarer;

    private Match(QueueTicket first, QueueTicket second, DateTimeOffset proposedAt)
    {
        First = first;
        Second = second;
        ProposedAt = proposedAt;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public QueueTicket First { get; }
    public QueueTicket Second { get; }
    public DateTimeOffset ProposedAt { get; }
    public MatchStatus Status { get; private set; } = MatchStatus.Proposed;

    public DateTimeOffset AcceptanceDeadline => ProposedAt + AcceptanceDelay;
    public bool AffectsRating => First.Ranked && Status == MatchStatus.Completed;

    public static Match Propose(QueueTicket first, QueueTicket second, DateTimeOffset now) => new(first, second, now);

    public QueueTicket OpponentOf(PlayerId player) => TicketOf(player) == First ? Second : First;

    public bool HasAccepted(PlayerId player) => _accepted.Contains(player);

    public void Accept(PlayerId player, DateTimeOffset now)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Proposed);
        if (now > AcceptanceDeadline)
            throw new InvalidOperationException("Le délai d'acceptation est dépassé.");

        _accepted.Add(player);
        if (_accepted.Count == 2)
            Status = MatchStatus.Accepted;
    }

    /// <returns>Les tickets à remettre dans la file.</returns>
    public IReadOnlyList<QueueTicket> Decline(PlayerId player)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Proposed);

        Status = MatchStatus.Declined;
        return [OpponentOf(player)];
    }

    /// <returns>Les tickets à remettre dans la file : ceux des joueurs qui avaient accepté.</returns>
    public IReadOnlyList<QueueTicket> Expire(DateTimeOffset now)
    {
        EnsureStatus(MatchStatus.Proposed);
        if (now <= AcceptanceDeadline)
            throw new InvalidOperationException("Le délai d'acceptation n'est pas encore dépassé.");

        Status = MatchStatus.Expired;
        return [.. new[] { First, Second }.Where(t => HasAccepted(t.Player))];
    }

    public void DeclareResult(PlayerId declarer, int declarerVp, int opponentVp)
    {
        EnsureParticipant(declarer);
        EnsureStatus(MatchStatus.Accepted);
        ArgumentOutOfRangeException.ThrowIfNegative(declarerVp);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentVp);

        _declarer = declarer;
        _victoryPoints[declarer] = declarerVp;
        _victoryPoints[OpponentOf(declarer).Player] = opponentVp;
        Status = MatchStatus.ResultDeclared;
    }

    public void ConfirmResult(PlayerId player) => RespondToResult(player, MatchStatus.Completed);

    public void DisputeResult(PlayerId player) => RespondToResult(player, MatchStatus.Disputed);

    public void Abandon(PlayerId player)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Accepted, MatchStatus.ResultDeclared);

        Status = MatchStatus.Abandoned;
    }

    public int VictoryPointsOf(PlayerId player)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Completed);
        return _victoryPoints[player];
    }

    public WtcScore ScoreOf(PlayerId player) =>
        WtcScore.FromVictoryPoints(VictoryPointsOf(player), VictoryPointsOf(OpponentOf(player).Player));

    private void RespondToResult(PlayerId player, MatchStatus outcome)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.ResultDeclared);
        if (player == _declarer)
            throw new InvalidOperationException("Seul l'adversaire du déclarant peut répondre au résultat.");

        Status = outcome;
    }

    private QueueTicket TicketOf(PlayerId player) =>
        player == First.Player ? First
        : player == Second.Player ? Second
        : throw new InvalidOperationException($"Le joueur {player.SteamId} ne participe pas à ce match.");

    private void EnsureParticipant(PlayerId player) => TicketOf(player);

    private void EnsureStatus(params MatchStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidOperationException($"Action impossible : le match est au statut {Status}.");
    }
}
