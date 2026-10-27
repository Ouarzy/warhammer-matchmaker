using Matchmaker.Domain.Matchmaking;

namespace Matchmaker.Domain.Matches;

public sealed class Match
{
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

    public static Match Propose(QueueTicket first, QueueTicket second, DateTimeOffset now) => new(first, second, now);
}
