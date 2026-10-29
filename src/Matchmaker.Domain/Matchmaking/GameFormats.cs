namespace Matchmaker.Domain.Matchmaking;

public static class GameFormats
{
    public static readonly GameFormat Incursion = new(GameSystem.Warhammer40k, "Incursion");
    public static readonly GameFormat StrikeForce = new(GameSystem.Warhammer40k, "Strike Force");
    public static readonly GameFormat Onslaught = new(GameSystem.Warhammer40k, "Onslaught");

    private static readonly GameFormat[] All = [Incursion, StrikeForce, Onslaught];

    // Hors de GameFormat, enregistré tel quel en base : pas de migration pour une donnée qui ne dépend que du nom.
    private static readonly Dictionary<GameFormat, int> Points = new()
    {
        [Incursion] = 1000,
        [StrikeForce] = 2000,
        [Onslaught] = 3000,
    };

    public static IReadOnlyList<GameFormat> Of(GameSystem game) => [.. All.Where(f => f.Game == game)];

    public static GameFormat? Find(GameSystem game, string name) =>
        All.FirstOrDefault(f => f.Game == game && f.Name == name);

    public static int PointsOf(GameFormat format) => Points[format];
}
