using System.Net;
using System.Text.RegularExpressions;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class PlayersDirectoryTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task SignIn(string name)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PlayerAccounts>().SignInAsync(MatchmakerWebFactory.NewPlayerId(), name, null);
    }

    private async Task<string> Html(string url) =>
        WebUtility.HtmlDecode(await _factory.CreateAnonymousClient().GetStringAsync(url));

    [Fact]
    public async Task All_players_are_listed_in_alphabetical_order_whatever_the_case()
    {
        // Les noms commençant par « 000 » passent avant tous les autres joueurs de la base partagée.
        await SignIn("000 Gamma");
        await SignIn("000 alpha");
        await SignIn("000 Beta");

        var html = await Html("/players");

        var (alpha, beta, gamma) = (html.IndexOf("000 alpha"), html.IndexOf("000 Beta"), html.IndexOf("000 Gamma"));
        Assert.True(alpha >= 0 && alpha < beta && beta < gamma, "ordre alphabétique attendu : alpha, Beta, Gamma");
    }

    [Fact]
    public async Task Players_are_shown_50_per_page()
    {
        for (var i = 0; i < 51; i++)
            await SignIn($"Annuaire {i:00}");

        var firstPage = await Html("/players");
        var secondPage = await Html("/players?page=2");

        Assert.Equal(PlayerAccounts.DirectoryPageSize, Regex.Matches(firstPage, "<tr><td>").Count);
        Assert.Contains("href=\"/players?page=2\"", firstPage);
        Assert.Contains("Page 2 /", secondPage);
    }

    [Fact]
    public async Task Searching_still_shows_only_the_matching_players()
    {
        var name = $"Introuvable{Random.Shared.Next(100000)}";
        await SignIn(name);

        var html = await Html($"/players?q={name}");

        Assert.Contains(name, html);
        Assert.Single(Regex.Matches(html, "<tr><td>"));
    }

    public void Dispose() => _factory.Dispose();
}
