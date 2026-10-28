using Matchmaker.Web.Tts;

namespace Matchmaker.Web.Tests.Tts;

public class LiveScoresTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_last_score_received_is_kept()
    {
        var scores = new LiveScores();
        var match = Guid.NewGuid();

        scores.Update(match, new LiveScore(10, 5, 1, Noon));
        scores.Update(match, new LiveScore(20, 15, 2, Noon.AddMinutes(30)));

        Assert.Equal(20, scores.Of(match)!.FirstVictoryPoints);
    }

    [Fact]
    public void Scores_left_without_news_for_a_day_are_forgotten()
    {
        var scores = new LiveScores();
        var (abandoned, ongoing) = (Guid.NewGuid(), Guid.NewGuid());
        scores.Update(abandoned, new LiveScore(10, 5, 1, Noon));

        scores.Update(ongoing, new LiveScore(10, 5, 1, Noon + LiveScores.KeptFor + TimeSpan.FromSeconds(1)));

        Assert.Null(scores.Of(abandoned));
        Assert.NotNull(scores.Of(ongoing));
    }
}
