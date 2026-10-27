using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;

namespace Matchmaker.Domain.Matchmaking;

/// <summary>
/// Recherche d'adversaire d'un joueur. EnqueuedAt fixe son ancienneté dans la file,
/// conservée s'il y est remis après un match refusé ou expiré.
/// </summary>
public sealed record QueueTicket(
    PlayerId Player,
    Guid ArmyListId,
    GameFormat Format,
    bool Ranked,
    EloRating Rating,
    DateTimeOffset EnqueuedAt)
{
    public bool CanFace(QueueTicket other, int maxRatingGap) =>
        other.Player != Player
        && other.Format == Format
        && other.Ranked == Ranked
        && RatingGapWith(other) <= maxRatingGap;

    public int RatingGapWith(QueueTicket other) => Math.Abs(other.Rating.Value - Rating.Value);
}
