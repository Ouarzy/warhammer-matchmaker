using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Ratings;

/// <summary>
/// Classement Elo inspiré des échecs (FIDE) : le résultat d'une partie
/// est le score WTC du joueur ramené entre 0 et 1.
/// </summary>
public readonly record struct EloRating(int Value)
{
    public static readonly EloRating Initial = new(1500);

    private const int ProvisionalGames = 30;
    private const int EliteThreshold = 2400;

    public double ExpectedResultAgainst(EloRating opponent) =>
        1 / (1 + Math.Pow(10, (opponent.Value - Value) / 400.0));

    public int KFactor(int rankedGamesPlayed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rankedGamesPlayed);

        if (rankedGamesPlayed < ProvisionalGames) return 40;
        if (Value >= EliteThreshold) return 10;
        return 20;
    }

    public EloRating AfterGame(EloRating opponent, WtcScore score, int rankedGamesPlayed)
    {
        var change = KFactor(rankedGamesPlayed) * (score.AsEloResult - ExpectedResultAgainst(opponent));

        return new EloRating(Value + (int)Math.Round(change, MidpointRounding.AwayFromZero));
    }
}
