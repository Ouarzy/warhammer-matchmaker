using Matchmaker.Domain.Matchmaking;

namespace Matchmaker.Domain.Tests.Matchmaking;

public class GameFormatsTests
{
    [Fact]
    public void Warhammer40k_offers_the_standard_formats()
    {
        Assert.Equal(
            ["Incursion", "Strike Force", "Onslaught"],
            GameFormats.Of(GameSystem.Warhammer40k).Select(f => f.Name));
    }

    [Fact]
    public void A_format_is_found_by_its_name()
    {
        Assert.Equal(GameFormats.StrikeForce, GameFormats.Find(GameSystem.Warhammer40k, "Strike Force"));
        Assert.Null(GameFormats.Find(GameSystem.Warhammer40k, "Apocalypse"));
    }

    [Fact]
    public void Each_format_has_its_points_limit()
    {
        Assert.Equal(1000, GameFormats.PointsOf(GameFormats.Incursion));
        Assert.Equal(2000, GameFormats.PointsOf(GameFormats.StrikeForce));
        Assert.Equal(3000, GameFormats.PointsOf(GameFormats.Onslaught));
    }
}
