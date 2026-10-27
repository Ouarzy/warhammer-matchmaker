using System.Net;
using System.Text.RegularExpressions;

namespace Matchmaker.Web.Tests.Infrastructure;

/// <summary>Soumet un formulaire Blazor (rendu serveur) comme le ferait un navigateur.</summary>
internal static partial class HtmlForms
{
    public static async Task<HttpResponseMessage> SubmitAsync(
        this HttpClient client, string pageUrl, string formName, IDictionary<string, string> fields)
    {
        var page = await client.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();

        var form = new Dictionary<string, string>(fields)
        {
            ["_handler"] = formName,
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(html).Groups[1].Value,
        };
        return await client.PostAsync(pageUrl, new FormUrlEncodedContent(form));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
