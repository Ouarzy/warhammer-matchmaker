namespace Matchmaker.Domain.Results;

/// <summary>
/// Score d'un joueur sur 20 selon le barème WTC : 10-10 jusqu'à 5 VP d'écart,
/// puis un point de plus au gagnant par tranche de 5 VP, 20-0 à partir de 51 VP.
/// </summary>
public readonly record struct WtcScore
{
    public const int Max = 20;
    private const int DrawMargin = 5;
    private const int VictoryPointsPerStep = 5;

    public int Points { get; }

    public WtcScore(int points)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(points);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(points, Max);
        Points = points;
    }

    public static WtcScore FromVictoryPoints(int playerVp, int opponentVp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(playerVp);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentVp);

        var margin = Math.Abs(playerVp - opponentVp);
        var bonus = margin <= DrawMargin
            ? 0
            : Math.Min(Max / 2, (int)Math.Ceiling((margin - DrawMargin) / (double)VictoryPointsPerStep));

        return new WtcScore(Max / 2 + (playerVp >= opponentVp ? bonus : -bonus));
    }

    public WtcScore Opponent => new(Max - Points);

    public double AsEloResult => (double)Points / Max;

    public ResultCategory Category => Points switch
    {
        >= 16 => ResultCategory.CrushingVictory,
        >= 13 => ResultCategory.Victory,
        >= 11 => ResultCategory.MinorVictory,
        10 => ResultCategory.Draw,
        >= 8 => ResultCategory.MinorDefeat,
        >= 5 => ResultCategory.Defeat,
        _ => ResultCategory.HeavyDefeat,
    };
}
