namespace Matchmaker.Domain.Matchmaking;

public enum GameSystem
{
    Warhammer40k,
}

public sealed record GameFormat(GameSystem Game, string Name);
