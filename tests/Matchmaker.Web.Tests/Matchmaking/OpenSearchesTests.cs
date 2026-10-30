using System.Net;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Matchmaking;

[Collection(PostgresCollection.Name)]
public sealed class OpenSearchesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    // Base partagée entre les tests : chacun planifie à une date à lui.
    private readonly DateTimeOffset _evening = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
        .AddDays(Random.Shared.Next(30, 20_000)).AddHours(20);

    private async Task<(PlayerId Player, HttpClient Client)> NewPlayer(string name)
    {
        var player = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(player, $"{name}{_suffix}");
        return (player, client);
    }

    private async Task<T> Searches<T>(Func<GameSearches, IServiceProvider, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<GameSearches>(), scope.ServiceProvider);
    }

    private Task<Guid> ImportList(PlayerId player, GameFormat format, string name = "Liste du soir") => Searches(async (_, services) =>
        (await services.GetRequiredService<ArmyListService>().ImportAsync(player, Samples.CustodesCode, name, format)).Id);

    private async Task SearchNow(PlayerId player, GameFormat format, bool ranked = true)
    {
        var list = await ImportList(player, format);
        await Searches(async (searches, _) => { await searches.SearchNowAsync(player, list, ranked); return 0; });
    }

    private async Task Plan(PlayerId player, DateTimeOffset at, GameFormat format, bool ranked = true)
    {
        var list = await ImportList(player, format);
        await Searches(async (_, services) => await services.GetRequiredService<ScheduledGames>().PlanAsync(player, list, ranked, at));
    }

    private Task<IReadOnlyList<OpenSearch>> Open(SearchKind kind = SearchKind.All) =>
        Searches((searches, _) => searches.OpenAsync(kind));

    private static async Task<string> Html(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));

    [Fact]
    public async Task Live_and_planned_searches_are_listed_from_the_oldest_to_the_newest()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await Plan(alice, _evening, GameFormats.StrikeForce);
        await SearchNow(bob, GameFormats.Incursion);
        await Plan(carol, _evening.AddDays(5), GameFormats.StrikeForce);

        var mine = (await Open()).Where(s => new[] { alice, bob, carol }.Contains(s.Ticket.Player)).ToList();

        Assert.Equal([alice, bob, carol], mine.Select(s => s.Ticket.Player));
    }

    [Fact]
    public async Task The_searches_can_be_filtered_by_kind()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening, GameFormats.StrikeForce);
        await SearchNow(bob, GameFormats.StrikeForce);

        var live = await Open(SearchKind.Live);
        var planned = await Open(SearchKind.Planned);

        Assert.Contains(live, s => s.Ticket.Player == bob && s.PlannedFor is null);
        Assert.DoesNotContain(live, s => s.Ticket.Player == alice);
        Assert.Contains(planned, s => s.Ticket.Player == alice && s.PlannedFor == _evening);
        Assert.DoesNotContain(planned, s => s.Ticket.Player == bob);
    }

    [Fact]
    public async Task A_search_answered_by_an_opponent_is_no_longer_listed()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await SearchNow(alice, GameFormats.StrikeForce);
        await SearchNow(bob, GameFormats.StrikeForce);

        Assert.DoesNotContain(await Open(), s => s.Ticket.Player == alice || s.Ticket.Player == bob);
    }

    [Fact]
    public async Task The_same_search_as_a_live_one_proposes_the_match()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await SearchNow(alice, GameFormats.Incursion, ranked: false);
        var search = Assert.Single(await Open(SearchKind.Live), s => s.Ticket.Player == alice);
        var list = await ImportList(bob, GameFormats.Incursion);

        await Searches(async (searches, _) => { await searches.JoinAsync(bob, list, search); return 0; });

        var coordinator = _factory.Services.GetRequiredService<MatchmakingCoordinator>();
        var proposal = coordinator.StatusOf(bob).Match;
        Assert.NotNull(proposal);
        Assert.Equal(alice, proposal.Opponent.Player);
        Assert.False(proposal.Me.Ranked);
    }

    [Fact]
    public async Task The_same_search_as_a_planned_one_proposes_the_match_at_the_same_time()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening, GameFormats.StrikeForce);
        var search = Assert.Single(await Open(SearchKind.Planned), s => s.Ticket.Player == alice);
        var list = await ImportList(bob, GameFormats.StrikeForce);

        await Searches(async (searches, _) => { await searches.JoinAsync(bob, list, search); return 0; });

        var proposal = Assert.Single((await Searches(async (_, services) =>
            await services.GetRequiredService<ScheduledGames>().OverviewAsync(bob))).Proposals);
        Assert.Equal(alice, proposal.OpponentOf(bob).Player);
        Assert.Equal(_evening, proposal.ScheduledFor);
    }

    [Fact]
    public async Task A_list_of_another_format_cannot_join_a_search()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await SearchNow(alice, GameFormats.StrikeForce);
        var search = Assert.Single(await Open(SearchKind.Live), s => s.Ticket.Player == alice);
        var list = await ImportList(bob, GameFormats.Incursion);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Searches(async (searches, _) => { await searches.JoinAsync(bob, list, search); return 0; }));
    }

    [Fact]
    public async Task Anyone_sees_the_searches_and_visitors_are_invited_to_log_in()
    {
        var (alice, _) = await NewPlayer("Alice");
        await Plan(alice, _evening, GameFormats.StrikeForce);

        var response = await _factory.CreateAnonymousClient().GetAsync("/searches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"Alice{_suffix}", html);
        Assert.Contains("Strike Force", html);
        Assert.Contains($"datetime=\"{_evening.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\"", html);
        Assert.Contains("Se connecter pour jouer", html);
        Assert.DoesNotContain("Faire la même recherche", html);
    }

    [Fact]
    public async Task A_search_shows_the_faction_and_the_points()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening, GameFormats.StrikeForce);
        await SearchNow(bob, GameFormats.Incursion);

        var html = await Html(_factory.CreateAnonymousClient(), "/searches");

        Assert.Contains("Adeptus Custodes · Strike Force (2000 pts)", html);
        Assert.Contains("Adeptus Custodes · Incursion (1000 pts)", html);
    }

    [Fact]
    public async Task The_page_offers_the_kinds_of_game_to_choose_from()
    {
        var html = await Html(_factory.CreateAnonymousClient(), "/searches");

        Assert.Contains("Partie live", html);
        Assert.Contains("Partie programmée", html);
        Assert.Contains("Les 2", html);
    }

    [Fact]
    public async Task A_player_with_a_list_of_the_format_gets_the_button_with_their_lists_when_they_have_several()
    {
        var ((alice, _), (bob, bobClient)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening, GameFormats.StrikeForce);
        await ImportList(bob, GameFormats.StrikeForce, "Première liste");
        await ImportList(bob, GameFormats.StrikeForce, "Seconde liste");
        await ImportList(bob, GameFormats.Incursion, "Liste Incursion");

        var html = await Html(bobClient, "/searches");

        Assert.Contains("Faire la même recherche", html);
        Assert.Contains("Première liste", html);
        Assert.Contains("Seconde liste", html);
        Assert.DoesNotContain("Liste Incursion", html);
    }

    [Fact]
    public async Task A_player_without_a_list_of_the_format_is_invited_to_import_one()
    {
        var ((alice, _), (bob, bobClient)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening, GameFormats.StrikeForce);
        await ImportList(bob, GameFormats.Incursion);

        var html = await Html(bobClient, "/searches");

        Assert.Contains("Importer une liste Strike Force", html);
        Assert.DoesNotContain("Faire la même recherche", html);
    }

    [Fact]
    public async Task A_player_sees_their_own_search_without_the_button()
    {
        var (alice, aliceClient) = await NewPlayer("Alice");
        await Plan(alice, _evening, GameFormats.StrikeForce);

        var html = await Html(aliceClient, "/searches");

        Assert.Contains("Votre recherche", html);
    }

    [Fact]
    public async Task The_menu_links_to_the_searches_just_below_the_games_in_progress()
    {
        var (_, client) = await NewPlayer("Alice");

        foreach (var html in new[] { await client.GetStringAsync("/profile"), await _factory.CreateAnonymousClient().GetStringAsync("/") })
        {
            var live = html.IndexOf("href=\"/live\"", StringComparison.Ordinal);
            var searches = html.IndexOf("href=\"/searches\"", StringComparison.Ordinal);
            var ranking = html.IndexOf("href=\"/ranking\"", StringComparison.Ordinal);
            Assert.InRange(searches, live + 1, ranking - 1);
        }
    }

    public void Dispose() => _factory.Dispose();
}
