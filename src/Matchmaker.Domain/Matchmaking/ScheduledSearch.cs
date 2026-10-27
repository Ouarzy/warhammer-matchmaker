using Matchmaker.Domain.Matches;

namespace Matchmaker.Domain.Matchmaking;

public enum ScheduledSearchStatus
{
    Open,
    Matched,
    Cancelled,
    Expired,
}

/// <summary>
/// Recherche d'un adversaire pour jouer à une date et une heure précises. Elle reste ouverte jusqu'à
/// ce qu'un joueur planifie une partie compatible à une heure d'écart au plus, et au plus tard une
/// heure avant la partie (le temps d'accepter).
/// </summary>
public sealed class ScheduledSearch
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromHours(1);

    private ScheduledSearch(QueueTicket ticket, DateTimeOffset plannedFor)
    {
        Ticket = ticket;
        PlannedFor = plannedFor;
    }

    // Pour EF Core, qui renseigne les propriétés lui-même.
    private ScheduledSearch()
    {
        Ticket = null!;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public QueueTicket Ticket { get; }
    public DateTimeOffset PlannedFor { get; }
    public ScheduledSearchStatus Status { get; private set; } = ScheduledSearchStatus.Open;
    /// <summary>Le match proposé à partir de cette recherche, tant qu'elle n'est pas rouverte.</summary>
    public Guid? MatchId { get; private set; }

    public static ScheduledSearch Open(QueueTicket ticket, DateTimeOffset plannedFor, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(plannedFor, LatestStartForMatching(now));
        return new(ticket, plannedFor);
    }

    /// <summary>La recherche ouverte la plus proche en heure, puis en Elo, puis la plus ancienne.</summary>
    public ScheduledSearch? BestOpponentAmong(IEnumerable<ScheduledSearch> candidates, DateTimeOffset now) =>
        candidates
            .Where(other => other.Status == ScheduledSearchStatus.Open && other.PlannedFor >= LatestStartForMatching(now))
            .Where(other => other.Ticket.CanFace(Ticket, int.MaxValue) && Gap(other) <= Tolerance)
            .OrderBy(Gap)
            .ThenBy(other => other.Ticket.RatingGapWith(Ticket))
            .ThenBy(other => other.Ticket.EnqueuedAt)
            .FirstOrDefault();

    /// <summary>Propose une partie à l'heure choisie par le joueur qui attendait.</summary>
    public Match MatchWith(ScheduledSearch waiting, DateTimeOffset now)
    {
        var match = Match.ProposeScheduled(waiting.Ticket, Ticket, waiting.PlannedFor, now);
        waiting.Matched(match);
        Matched(match);
        return match;
    }

    /// <summary>La partie proposée n'a pas eu lieu (refusée ou pas acceptée à temps par l'adversaire).</summary>
    public void Reopen(DateTimeOffset now)
    {
        EnsureStatus(ScheduledSearchStatus.Matched);
        MatchId = null;
        Status = PlannedFor >= LatestStartForMatching(now) ? ScheduledSearchStatus.Open : ScheduledSearchStatus.Expired;
    }

    public void Cancel()
    {
        EnsureStatus(ScheduledSearchStatus.Open);
        Status = ScheduledSearchStatus.Cancelled;
    }

    /// <summary>Le joueur a laissé passer ou refusé la partie proposée : sa recherche s'arrête là.</summary>
    public void Close()
    {
        EnsureStatus(ScheduledSearchStatus.Matched);
        MatchId = null;
        Status = ScheduledSearchStatus.Cancelled;
    }

    /// <returns>Vrai si la recherche vient d'expirer : trop tard pour encore trouver un adversaire.</returns>
    public bool ExpireIfTooLate(DateTimeOffset now)
    {
        if (Status != ScheduledSearchStatus.Open || PlannedFor >= LatestStartForMatching(now)) return false;
        Status = ScheduledSearchStatus.Expired;
        return true;
    }

    private static DateTimeOffset LatestStartForMatching(DateTimeOffset now) => now + Match.ScheduledAcceptanceNotice;

    private TimeSpan Gap(ScheduledSearch other) => (other.PlannedFor - PlannedFor).Duration();

    private void Matched(Match match)
    {
        EnsureStatus(ScheduledSearchStatus.Open);
        Status = ScheduledSearchStatus.Matched;
        MatchId = match.Id;
    }

    private void EnsureStatus(ScheduledSearchStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Action impossible : la recherche est au statut {Status}.");
    }
}
