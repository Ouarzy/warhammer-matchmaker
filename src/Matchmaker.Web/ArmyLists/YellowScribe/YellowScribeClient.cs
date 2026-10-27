using System.Net;
using System.Text.RegularExpressions;
using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Web.ArmyLists.YellowScribe;

/// <summary>
/// Récupère une liste à partir d'un code YellowScribe. Le code expire 10 minutes après sa création :
/// la liste doit être récupérée et stockée au moment de l'import.
/// </summary>
public sealed partial class YellowScribeClient(HttpClient http) : IRosterProvider
{
    public static readonly Uri BaseAddress = new("https://yellowscribe.link/");

    public async Task<Roster> FetchAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(normalized))
            throw new RosterImportException(RosterImportError.InvalidCode, "Un code YellowScribe contient 8 caractères (chiffres et lettres de a à f).");

        string json;
        try
        {
            using var response = await http.GetAsync($"get_army_by_id?id={normalized}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new RosterImportException(RosterImportError.NotFound, "Code YellowScribe inconnu ou expiré (un code n'est valable que 10 minutes).");
            if (!response.IsSuccessStatusCode)
                throw new RosterImportException(RosterImportError.Unavailable, $"YellowScribe a répondu {(int)response.StatusCode}.");

            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new RosterImportException(RosterImportError.Unavailable, "YellowScribe est injoignable pour le moment.", e);
        }

        return YellowScribeRosterParser.Parse(json);
    }

    [GeneratedRegex("^[0-9a-f]{8}$")]
    private static partial Regex CodePattern();
}
