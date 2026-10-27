using Matchmaker.Domain.Ratings;
using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Players;

public sealed class Player(PlayerId id)
{
    public PlayerId Id { get; } = id;
    public string DisplayName { get; private set; } = id.SteamId;
    public string? AvatarUrl { get; private set; }
    public EloRating Rating { get; private set; } = EloRating.Initial;
    public int RankedGamesPlayed { get; private set; }

    public void UpdateProfile(string displayName, string? avatarUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
        AvatarUrl = avatarUrl;
    }

    public void RecordRankedGame(EloRating opponentRatingBefore, WtcScore score)
    {
        Rating = Rating.AfterGame(opponentRatingBefore, score, RankedGamesPlayed);
        RankedGamesPlayed++;
    }
}
