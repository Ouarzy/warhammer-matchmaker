using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;

namespace Matchmaker.Domain.Matchmaking;

/// <summary>
/// Recherche d'adversaire d'un joueur. EnqueuedAt fixe son ancienneté dans la file,
/// conservée s'il y est remis après un match refusé ou expiré. Une recherche réservée à un joueur
/// (<paramref name="ReservedFor"/>) n'est proposée qu'à lui, quel que soit l'écart d'Elo.
/// </summary>
public sealed record QueueTicket(
    PlayerId Player,
    Guid ArmyListId,
    GameFormat Format,
    bool Ranked,
    EloRating Rating,
    DateTimeOffset EnqueuedAt,
    PlayerId? ReservedFor = null)
{
    // Pour EF Core, qui renseigne les propriétés lui-même.
    private QueueTicket() : this(default, Guid.Empty, null!, false, default, default)
    {
    }

    public bool CanFace(QueueTicket other, int maxRatingGap) =>
        other.Player != Player
        && Accepts(other) && other.Accepts(this)
        && other.Format == Format
        && other.Ranked == Ranked
        && (IsReservedBetween(other) || RatingGapWith(other) <= maxRatingGap);

    /// <summary>Vrai si ce joueur cherche à jouer contre <paramref name="player"/> en particulier.</summary>
    public bool IsReservedFor(PlayerId player) => ReservedFor == player;

    private bool Accepts(QueueTicket other) => ReservedFor is null || IsReservedFor(other.Player);

    private bool IsReservedBetween(QueueTicket other) => IsReservedFor(other.Player) || other.IsReservedFor(Player);

    public int RatingGapWith(QueueTicket other) => Math.Abs(other.Rating.Value - Rating.Value);
}
