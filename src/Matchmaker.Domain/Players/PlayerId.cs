namespace Matchmaker.Domain.Players;

public readonly record struct PlayerId(string SteamId)
{
    private const string SteamOpenIdPrefix = "https://steamcommunity.com/openid/id/";

    /// <summary>Lit le Steam ID dans l'identifiant renvoyé par la connexion Steam (OpenID).</summary>
    public static PlayerId FromSteamOpenId(string identifier)
    {
        var steamId = identifier.StartsWith(SteamOpenIdPrefix, StringComparison.Ordinal)
            ? identifier[SteamOpenIdPrefix.Length..]
            : "";

        if (steamId.Length == 0 || !steamId.All(char.IsAsciiDigit))
            throw new ArgumentException($"Identifiant Steam OpenID invalide : '{identifier}'.", nameof(identifier));

        return new PlayerId(steamId);
    }
}
