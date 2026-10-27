namespace Matchmaker.Domain.Matchmaking;

public static class GameFormats
{
    public static readonly GameFormat Incursion = new(GameSystem.Warhammer40k, "Incursion");
    public static readonly GameFormat StrikeForce = new(GameSystem.Warhammer40k, "Strike Force");
    public static readonly GameFormat Onslaught = new(GameSystem.Warhammer40k, "Onslaught");

    private static readonly GameFormat[] All = [Incursion, StrikeForce, Onslaught];

    public static IReadOnlyList<GameFormat> Of(GameSystem game) => [.. All.Where(f => f.Game == game)];

    public static GameFormat? Find(GameSystem game, string name) =>
        All.FirstOrDefault(f => f.Game == game && f.Name == name);
}
