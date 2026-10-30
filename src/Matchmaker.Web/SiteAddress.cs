namespace Matchmaker.Web;

/// <summary>Adresse publique du site, pour les liens lus hors du site : derrière Caddy, la requête ne la donne pas.</summary>
public static class SiteAddress
{
    public const string Name = "Warhammer Matchmaker";

    private const string Domain = "https://warhammer-matchmaker.com";

    public static string Of(string path) => Domain + path;
}
