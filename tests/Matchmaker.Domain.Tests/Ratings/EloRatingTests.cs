using Matchmaker.Domain.Ratings;
using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Tests.Ratings;

public class EloRatingTests
{
    private const int NewPlayer = 0;
    private const int Veteran = 30;

    [Fact]
    public void New_players_start_at_1500()
    {
        Assert.Equal(1500, EloRating.Initial.Value);
    }

    [Theory]
    [InlineData(1500, 1500, 0.5)]
    [InlineData(1500, 1900, 0.0909090909)]
    [InlineData(1900, 1500, 0.9090909091)]
    public void Expected_result_follows_the_chess_formula(int player, int opponent, double expected)
    {
        Assert.Equal(expected, new EloRating(player).ExpectedResultAgainst(new EloRating(opponent)), precision: 9);
    }

    [Theory]
    [InlineData(1500, NewPlayer, 40)]
    [InlineData(1500, 29, 40)]
    [InlineData(1500, Veteran, 20)]
    [InlineData(2399, Veteran, 20)]
    [InlineData(2400, Veteran, 10)]
    public void K_factor_depends_on_experience_and_level(int rating, int rankedGamesPlayed, int expected)
    {
        Assert.Equal(expected, new EloRating(rating).KFactor(rankedGamesPlayed));
    }

    [Theory]
    [InlineData(1500, 1500, 20, NewPlayer, 1520)]
    [InlineData(1500, 1500, 0, NewPlayer, 1480)]
    [InlineData(1500, 1500, 10, Veteran, 1500)]
    [InlineData(1500, 1500, 13, Veteran, 1503)]
    [InlineData(1500, 1900, 20, Veteran, 1518)]
    [InlineData(2400, 2400, 20, Veteran, 2405)]
    public void Rating_moves_by_k_times_the_gap_between_result_and_expectation(
        int player, int opponent, int wtcPoints, int rankedGamesPlayed, int expected)
    {
        var after = new EloRating(player).AfterGame(new EloRating(opponent), new WtcScore(wtcPoints), rankedGamesPlayed);

        Assert.Equal(expected, after.Value);
    }

    [Fact]
    public void A_narrow_win_against_a_much_weaker_opponent_costs_rating()
    {
        var after = new EloRating(1900).AfterGame(new EloRating(1500), new WtcScore(11), Veteran);

        Assert.Equal(1893, after.Value);
    }

    [Fact]
    public void Players_with_the_same_k_factor_exchange_the_same_amount_of_rating()
    {
        var winner = new EloRating(1620);
        var loser = new EloRating(1480);
        var score = WtcScore.FromVictoryPoints(88, 61);

        var winnerGain = winner.AfterGame(loser, score, Veteran).Value - winner.Value;
        var loserGain = loser.AfterGame(winner, score.Opponent, Veteran).Value - loser.Value;

        Assert.Equal(0, winnerGain + loserGain);
    }

    [Fact]
    public void Rejects_a_negative_number_of_ranked_games()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EloRating.Initial.KFactor(-1));
    }
}
