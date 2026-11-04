using System.Net;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Matchmaking;

/// <summary>Partie proposée à un joueur en particulier, immédiate ou planifiée.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ProposedGamesTests(PostgresFixture postgres) : IDisposable
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

    private Task Searches(Func<GameSearches, Task> work) => Searches(async (searches, _) => { await work(searches); return 0; });

    private Task<Guid> ImportList(PlayerId player, GameFormat? format = null, string name = "Liste du soir") => Searches(async (_, services) =>
        (await services.GetRequiredService<ArmyListService>().ImportAsync(player, Samples.CustodesCode, name, format ?? GameFormats.StrikeForce)).Id);

    private async Task ProposeNow(PlayerId player, PlayerId opponent, bool ranked = true)
    {
        var list = await ImportList(player);
        await Searches(searches => searches.SearchNowAsync(player, list, ranked, opponent));
    }

    private async Task ProposePlanned(PlayerId player, PlayerId opponent)
    {
        var list = await ImportList(player);
        await Searches(async (_, services) => await services.GetRequiredService<ScheduledGames>().PlanAsync(player, list, true, _evening, opponent));
    }

    private Task<IReadOnlyList<OpenSearch>> Open(PlayerId? viewer) => Searches((searches, _) => searches.OpenAsync(SearchKind.All, viewer));

    private Task<IReadOnlyList<OpenSearch>> ProposedTo(PlayerId player) => Searches((searches, _) => searches.ProposedToAsync(player));

    private MatchmakingCoordinator Coordinator => _factory.Services.GetRequiredService<MatchmakingCoordinator>();

    private static async Task<string> Html(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));

    [Fact]
    public async Task A_game_proposed_to_a_player_is_listed_only_for_them_and_its_author()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await ProposeNow(alice, bob);
        await ProposePlanned(alice, bob);

        Assert.Equal(2, (await Open(alice)).Count(s => s.Ticket.Player == alice));
        Assert.Equal(2, (await Open(bob)).Count(s => s.Ticket.Player == alice));
        Assert.DoesNotContain(await Open(carol), s => s.Ticket.Player == alice);
        Assert.DoesNotContain(await Open(null), s => s.Ticket.Player == alice);
    }

    [Fact]
    public async Task The_player_sees_the_games_proposed_to_them()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await ProposeNow(alice, bob);
        await ProposePlanned(carol, bob);

        var proposed = await ProposedTo(bob);

        Assert.Equal([alice, carol], proposed.Select(s => s.Ticket.Player));
        Assert.Empty(await ProposedTo(alice));
    }

    [Fact]
    public async Task A_game_proposed_now_is_not_matched_with_another_player()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await ProposeNow(alice, bob);

        var list = await ImportList(carol);
        await Searches(searches => searches.SearchNowAsync(carol, list, ranked: true));

        Assert.Equal(PlayerActivity.Searching, Coordinator.StatusOf(alice).Activity);
        Assert.Equal(PlayerActivity.Searching, Coordinator.StatusOf(carol).Activity);
    }

    [Fact]
    public async Task Joining_a_game_proposed_now_proposes_the_match_with_its_author()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        var carolList = await ImportList(carol);
        await ProposeNow(alice, bob, ranked: false);
        await Searches(searches => searches.SearchNowAsync(carol, carolList, ranked: false));
        var proposed = Assert.Single(await ProposedTo(bob));
        var list = await ImportList(bob);

        await Searches(searches => searches.JoinAsync(bob, list, proposed));

        var match = Coordinator.StatusOf(bob).Match;
        Assert.NotNull(match);
        Assert.Equal(alice, match.Opponent.Player);
        Assert.Equal(PlayerActivity.Searching, Coordinator.StatusOf(carol).Activity);
    }

    [Fact]
    public async Task Joining_a_planned_game_proposed_to_the_player_proposes_it_at_the_same_time()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await ProposePlanned(alice, bob);
        var proposed = Assert.Single(await ProposedTo(bob));
        var list = await ImportList(bob);

        await Searches(searches => searches.JoinAsync(bob, list, proposed));

        var match = Assert.Single((await Searches(async (_, services) =>
            await services.GetRequiredService<ScheduledGames>().OverviewAsync(bob))).Proposals);
        Assert.Equal(alice, match.OpponentOf(bob).Player);
        Assert.Equal(_evening, match.ScheduledFor);
    }

    [Fact]
    public async Task Another_player_cannot_join_a_game_proposed_to_someone_else()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await ProposePlanned(alice, bob);
        var proposed = Assert.Single(await ProposedTo(bob));
        var list = await ImportList(carol);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Searches(searches => searches.JoinAsync(carol, list, proposed)));
    }

    [Fact]
    public async Task The_player_can_decline_the_games_proposed_to_them()
    {
        var ((alice, _), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await ProposeNow(alice, bob);
        await ProposePlanned(alice, bob);

        foreach (var proposed in await ProposedTo(bob))
            await Searches(searches => searches.DeclineAsync(bob, proposed));

        Assert.Empty(await ProposedTo(bob));
        Assert.Equal(PlayerActivity.Idle, Coordinator.StatusOf(alice).Activity);
        Assert.Empty((await Searches(async (_, services) =>
            await services.GetRequiredService<ScheduledGames>().OverviewAsync(alice))).OpenSearches);
    }

    [Fact]
    public async Task Only_the_player_a_game_is_proposed_to_can_decline_it()
    {
        var ((alice, _), (bob, _), (carol, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await ProposePlanned(alice, bob);
        var proposed = Assert.Single(await ProposedTo(bob));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Searches(searches => searches.DeclineAsync(carol, proposed)));
    }

    [Fact]
    public async Task A_game_cannot_be_proposed_to_oneself_or_to_an_unknown_player()
    {
        var (alice, _) = await NewPlayer("Alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProposeNow(alice, alice));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProposePlanned(alice, MatchmakerWebFactory.NewPlayerId()));
    }

    [Fact]
    public async Task The_new_game_form_offers_every_other_player_as_opponent_anyone_by_default()
    {
        var ((alice, aliceClient), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await ImportList(alice);

        var html = await Html(aliceClient, "/play");

        Assert.Contains("Adversaire souhaité", html);
        Assert.Matches("Adversaire souhaité\\s*<select[^>]*>\\s*<option value(=\"\")?>N'importe qui</option>", html);
        Assert.Contains($"<option value=\"{bob.SteamId}\">Bob{_suffix}</option>", html);
        Assert.DoesNotContain($"<option value=\"{alice.SteamId}\"", html);
    }

    [Fact]
    public async Task The_opponent_given_in_the_address_is_selected()
    {
        var ((alice, aliceClient), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await ImportList(alice);

        var html = await Html(aliceClient, $"/play?opponent={bob.SteamId}");

        Assert.Contains($"<option value=\"{bob.SteamId}\" selected>Bob{_suffix}</option>", html);
    }

    [Fact]
    public async Task The_play_page_shows_the_games_proposed_to_the_player()
    {
        var ((alice, _), (bob, bobClient)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await ProposePlanned(alice, bob);
        await ImportList(bob);

        var html = await Html(bobClient, "/play");

        Assert.Contains("Parties qu'on vous propose", html);
        Assert.Contains($"Alice{_suffix}", html);
        Assert.Contains("Jouer cette partie", html);
        Assert.Contains("Refuser", html);
    }

    [Fact]
    public async Task The_profile_of_another_player_offers_to_propose_them_a_game()
    {
        var ((_, aliceClient), (bob, bobClient)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));

        Assert.Contains($"href=\"/play?opponent={bob.SteamId}\"", await Html(aliceClient, $"/players/{bob.SteamId}"));
        Assert.Contains($"href=\"/play?opponent={bob.SteamId}\"", await Html(_factory.CreateAnonymousClient(), $"/players/{bob.SteamId}"));
        Assert.DoesNotContain("Proposer une partie", await Html(bobClient, $"/players/{bob.SteamId}"));
        Assert.DoesNotContain("Proposer une partie", await Html(bobClient, "/profile"));
    }

    public void Dispose() => _factory.Dispose();
}
