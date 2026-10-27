using Matchmaker.Domain.Ratings;
using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Players;

public sealed class Player(PlayerId id)
{
    public PlayerId Id { get; } = id;
    public EloRating Rating { get; private set; } = EloRating.Initial;
    public int RankedGamesPlayed { get; private set; }

    public void RecordRankedGame(EloRating opponentRatingBefore, WtcScore score)
    {
        Rating = Rating.AfterGame(opponentRatingBefore, score, RankedGamesPlayed);
        RankedGamesPlayed++;
    }
}
