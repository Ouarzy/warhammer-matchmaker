using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;

namespace Matchmaker.Domain.Tests;

internal static class Tickets
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    public static readonly GameFormat StrikeForce = GameFormats.StrikeForce;
    public static readonly GameFormat Incursion = GameFormats.Incursion;

    public static QueueTicket For(
        string steamId,
        GameFormat? format = null,
        bool ranked = true,
        int rating = 1500,
        DateTimeOffset? enqueuedAt = null) =>
        new(new PlayerId(steamId), Guid.NewGuid(), format ?? StrikeForce, ranked, new EloRating(rating), enqueuedAt ?? Noon);
}
