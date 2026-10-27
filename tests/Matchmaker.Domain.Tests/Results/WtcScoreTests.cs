using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Tests.Results;

public class WtcScoreTests
{
    [Theory]
    [InlineData(70, 70, 10)]
    [InlineData(75, 70, 10)]
    [InlineData(76, 70, 11)]
    [InlineData(80, 70, 11)]
    [InlineData(81, 70, 12)]
    [InlineData(85, 70, 12)]
    [InlineData(90, 70, 13)]
    [InlineData(95, 70, 14)]
    [InlineData(100, 70, 15)]
    [InlineData(100, 65, 16)]
    [InlineData(100, 64, 17)]
    [InlineData(100, 60, 17)]
    [InlineData(100, 55, 18)]
    [InlineData(100, 50, 19)]
    [InlineData(100, 49, 20)]
    [InlineData(100, 0, 20)]
    public void Converts_victory_point_margin_to_wtc_score_for_the_winner(int winnerVp, int loserVp, int expected)
    {
        Assert.Equal(expected, WtcScore.FromVictoryPoints(winnerVp, loserVp).Points);
    }

    [Theory]
    [InlineData(70, 76, 9)]
    [InlineData(70, 85, 8)]
    [InlineData(0, 100, 0)]
    public void Loser_gets_the_complement_to_20(int loserVp, int winnerVp, int expected)
    {
        Assert.Equal(expected, WtcScore.FromVictoryPoints(loserVp, winnerVp).Points);
    }

    [Fact]
    public void Opponent_score_is_the_complement_to_20()
    {
        var score = WtcScore.FromVictoryPoints(90, 70);

        Assert.Equal(7, score.Opponent.Points);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, -1)]
    public void Rejects_negative_victory_points(int playerVp, int opponentVp)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WtcScore.FromVictoryPoints(playerVp, opponentVp));
    }

    [Theory]
    [InlineData(20, ResultCategory.CrushingVictory)]
    [InlineData(16, ResultCategory.CrushingVictory)]
    [InlineData(15, ResultCategory.Victory)]
    [InlineData(13, ResultCategory.Victory)]
    [InlineData(12, ResultCategory.MinorVictory)]
    [InlineData(11, ResultCategory.MinorVictory)]
    [InlineData(10, ResultCategory.Draw)]
    [InlineData(9, ResultCategory.MinorDefeat)]
    [InlineData(8, ResultCategory.MinorDefeat)]
    [InlineData(7, ResultCategory.Defeat)]
    [InlineData(5, ResultCategory.Defeat)]
    [InlineData(4, ResultCategory.HeavyDefeat)]
    [InlineData(0, ResultCategory.HeavyDefeat)]
    public void Categorises_the_result_from_the_wtc_score(int points, ResultCategory expected)
    {
        Assert.Equal(expected, new WtcScore(points).Category);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Rejects_a_score_outside_0_to_20(int points)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WtcScore(points));
    }

    [Theory]
    [InlineData(10, 0.5)]
    [InlineData(13, 0.65)]
    [InlineData(20, 1.0)]
    [InlineData(0, 0.0)]
    public void Elo_result_is_the_score_divided_by_20(int points, double expected)
    {
        Assert.Equal(expected, new WtcScore(points).AsEloResult, precision: 10);
    }
}
