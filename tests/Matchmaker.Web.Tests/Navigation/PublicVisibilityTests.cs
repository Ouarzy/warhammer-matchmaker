using System.Net;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Navigation;

[Collection(PostgresCollection.Name)]
public sealed class PublicVisibilityTests(PostgresFixture postgres) : IDisposable
{
    private const string Site = "https://warhammer-matchmaker.com";

    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    // Base partagée entre les tests : chacun planifie à une date à lui.
    private readonly DateTimeOffset _evening = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
        .AddDays(Random.Shared.Next(30, 20_000)).AddHours(20);

    private async Task<string> Anonymous(string url) =>
        WebUtility.HtmlDecode(await _factory.CreateAnonymousClient().GetStringAsync(url));

    private async Task<(PlayerId Player, HttpClient Client)> NewPlayer(string name)
    {
        var player = MatchmakerWebFactory.NewPlayerId();
        return (player, await _factory.CreateClientAsAsync(player, $"{name}{_suffix}"));
    }

    private async Task<ScheduledSearch> Plan(PlayerId player, DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        var list = await scope.ServiceProvider.GetRequiredService<ArmyListService>()
            .ImportAsync(player, Samples.CustodesCode, "Liste du soir", GameFormats.StrikeForce);
        return await scope.ServiceProvider.GetRequiredService<ScheduledGames>().PlanAsync(player, list.Id, ranked: true, at);
    }

    [Theory]
    [InlineData("/", "")]
    [InlineData("/ranking", "/ranking")]
    [InlineData("/live", "/live")]
    [InlineData("/searches", "/searches")]
    [InlineData("/players", "/players")]
    [InlineData("/tts", "/tts")]
    [InlineData("/a-propos", "/a-propos")]
    public async Task Public_pages_describe_themselves_to_search_engines_and_link_previews(string url, string canonical)
    {
        var html = await Anonymous(url);

        Assert.Contains("<meta name=\"description\" content=\"", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"{Site}{canonical}\"", html);
        Assert.Contains($"<meta property=\"og:url\" content=\"{Site}{canonical}\"", html);
        Assert.Contains("<meta property=\"og:title\" content=\"", html);
        Assert.Contains("<meta property=\"og:description\" content=\"", html);
        Assert.Contains($"<meta property=\"og:image\" content=\"{Site}/og-image.png\"", html);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", html);
        Assert.DoesNotContain("noindex", html);
    }

    [Theory]
    [InlineData("/robots.txt", "Sitemap: https://warhammer-matchmaker.com/sitemap.xml")]
    [InlineData("/robots.txt", "Disallow: /play")]
    [InlineData("/sitemap.xml", "<loc>https://warhammer-matchmaker.com/ranking</loc>")]
    public async Task Search_engines_find_their_instructions(string url, string expected)
    {
        Assert.Contains(expected, await Anonymous(url));
    }

    [Theory]
    [InlineData("/og-image.png", "image/png")]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/icon-192.png", "image/png")]
    [InlineData("/icon-512.png", "image/png")]
    public async Task The_images_of_the_site_are_served(string url, string contentType)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_home_page_explains_how_it_works_and_shows_the_activity()
    {
        var (alice, _) = await NewPlayer("Alice");
        await Plan(alice, _evening);

        var html = await Anonymous("/");

        Assert.Contains("Comment ça marche", html);
        Assert.Contains("Gratuit", html);
        Assert.Contains("LCT", html);
        Assert.Contains("À une date précise", html);
        Assert.Contains("href=\"/searches\"", html);
        Assert.Matches(@"class=""home-stat""[^>]*><strong>\d+</strong> joueurs?", html);
        Assert.Matches(@"class=""home-stat""[^>]*><strong>\d+</strong> recherches? en cours", html);
    }

    [Fact]
    public async Task The_home_page_shows_the_top_of_the_ranking()
    {
        var (a, b) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        await _factory.CreateClientAsAsync(a, $"Alice{_suffix}");
        await _factory.CreateClientAsAsync(b, $"Bob{_suffix}");
        var matchId = await _factory.StartMatchAsync(a, b);
        await _factory.WithMatchesAsync(async matches =>
        {
            await matches.DeclareResultAsync(a, matchId, 80, 40);
            await matches.ConfirmResultAsync(b, matchId);
        });

        var html = await Anonymous("/");

        Assert.Contains("Meilleurs joueurs", html);
        Assert.Contains("class=\"ranking\"", html);
        Assert.Contains("Voir le classement complet", html);
    }

    [Fact]
    public async Task A_shared_profile_has_its_own_preview_but_stays_out_of_search_engines()
    {
        var (alice, _) = await NewPlayer("Alice");

        var html = await Anonymous($"/players/{alice.SteamId}");

        Assert.Contains($"<meta property=\"og:title\" content=\"Alice{_suffix}", html);
        Assert.Contains("Elo 1500", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"{Site}/players/{alice.SteamId}\"", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html);
        Assert.Contains($"data-share-url=\"{Site}/players/{alice.SteamId}\"", html);
    }

    [Fact]
    public async Task A_planned_search_has_a_link_to_share_with_the_time_in_its_preview()
    {
        var (alice, _) = await NewPlayer("Alice");
        var search = await Plan(alice, _evening);
        var paris = TimeZoneInfo.ConvertTime(_evening, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));

        var html = await Anonymous($"/searches/{search.Id}");

        Assert.Contains($"<meta property=\"og:title\" content=\"Alice{_suffix} cherche un adversaire", html);
        Assert.Contains($"à {paris:HH}h00", html);
        Assert.Contains("Adeptus Custodes · Strike Force (2000 pts)", html);
        Assert.Contains("heure de Paris", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html);
        Assert.Contains($"href=\"/login?returnUrl=/searches/{search.Id}\"", html);
        Assert.Contains("Voir toutes les recherches", html);
    }

    [Fact]
    public async Task The_list_and_my_planned_searches_offer_to_share_them()
    {
        var (alice, aliceClient) = await NewPlayer("Alice");
        var search = await Plan(alice, _evening);
        var shareLink = $"data-share-url=\"{Site}/searches/{search.Id}\"";

        Assert.Contains(shareLink, await Anonymous("/searches"));
        Assert.Contains(shareLink, WebUtility.HtmlDecode(await aliceClient.GetStringAsync("/play")));
    }

    [Fact]
    public async Task A_search_that_is_no_longer_open_says_so_and_leads_to_the_others()
    {
        var html = await Anonymous($"/searches/{Guid.NewGuid()}");

        Assert.Contains("Cette recherche n'est plus ouverte", html);
        Assert.Contains("Voir toutes les recherches", html);
        Assert.Contains("<meta name=\"robots\" content=\"noindex\"", html);
    }

    public void Dispose() => _factory.Dispose();
}
