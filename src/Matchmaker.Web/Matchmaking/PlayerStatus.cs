using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;

namespace Matchmaker.Web.Matchmaking;

public enum PlayerActivity
{
    Idle,
    Searching,
    MatchProposed,
    InMatch,
}

/// <summary>Photo de la situation d'un joueur, à afficher sans verrou.</summary>
public sealed record PlayerStatus(PlayerActivity Activity, QueueTicket? Search = null, MatchView? Match = null)
{
    public static readonly PlayerStatus Idle = new(PlayerActivity.Idle);
}

/// <summary>Un match vu par l'un des deux joueurs.</summary>
public sealed record MatchView(
    Guid Id,
    MatchStatus Status,
    DateTimeOffset AcceptanceDeadline,
    QueueTicket Me,
    QueueTicket Opponent,
    bool IAccepted,
    bool OpponentAccepted);
