using System.Net;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Navigation;

[Collection(PostgresCollection.Name)]
public sealed class NavigationTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    [Fact]
    public async Task Once_signed_in_the_home_page_leads_to_my_account()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/profile", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task My_account_offers_a_direct_button_to_look_for_a_game()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync("/profile");

        Assert.Contains("href=\"/play\"", html);
        Assert.Contains("Rechercher une partie", html);
    }

    [Fact]
    public async Task Another_player_profile_has_no_search_button()
    {
        var other = MatchmakerWebFactory.NewPlayerId();
        await _factory.CreateClientAsAsync(other);
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync($"/players/{other.SteamId}");

        Assert.DoesNotContain("class=\"button call-to-action\"", html);
    }

    [Fact]
    public async Task Signed_in_players_get_a_side_menu()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync("/ranking");

        Assert.Contains("class=\"sidebar\"", html);
        foreach (var (url, label) in new[]
                 {
                     ("/profile", "Mon compte"), ("/play", "Rechercher une partie"), ("/matches", "Mes parties"),
                     ("/lists", "Mes listes"), ("/ranking", "Classement"), ("/players", "Joueurs"),
                 })
        {
            Assert.Contains($"href=\"{url}\"", html);
            Assert.Contains(label, html);
        }
    }

    [Fact]
    public async Task On_small_screens_the_side_menu_opens_from_a_burger_button()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync("/ranking");

        Assert.Contains("id=\"menu-toggle\"", html);
        Assert.Contains("class=\"burger\" for=\"menu-toggle\"", html);
        Assert.Contains("aria-label=\"Menu\"", html);
    }

    [Fact]
    public async Task Visitors_are_invited_to_sign_in_without_side_menu()
    {
        var html = await _factory.CreateAnonymousClient().GetStringAsync("/ranking");

        Assert.Contains("Se connecter avec Steam", html);
        Assert.DoesNotContain("Mes parties", html);
    }

    public void Dispose() => _factory.Dispose();
}
