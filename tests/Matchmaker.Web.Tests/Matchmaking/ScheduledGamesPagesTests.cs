using System.Net;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Matchmaking;

[Collection(PostgresCollection.Name)]
public sealed class ScheduledGamesPagesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    // Base partagée entre les tests : chacun planifie à une date à lui.
    private readonly DateTimeOffset _evening = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
        .AddDays(Random.Shared.Next(30, 20_000)).AddHours(20);

    private async Task<(PlayerId Player, HttpClient Client)> NewPlayer(string name)
    {
        var player = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(player, name);
        return (player, client);
    }

    private async Task<T> Scheduled<T>(Func<ScheduledGames, IServiceProvider, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ScheduledGames>(), scope.ServiceProvider);
    }

    private Task<ScheduledSearch> Plan(PlayerId player, DateTimeOffset at) => Scheduled(async (games, services) =>
    {
        var list = await services.GetRequiredService<ArmyListService>()
            .ImportAsync(player, Samples.CustodesCode, "Liste du soir", GameFormats.StrikeForce);
        return await games.PlanAsync(player, list.Id, ranked: true, at);
    });

    private static async Task<string> Html(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    [Fact]
    public async Task The_search_form_offers_to_plan_a_game_at_a_given_date_and_time()
    {
        var (me, client) = await NewPlayer("Alice");
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ArmyListService>().ImportAsync(me, Samples.CustodesCode, "Liste", GameFormats.StrikeForce);

        var html = await Html(client, "/play");

        Assert.Contains("Maintenant", html);
        Assert.Contains("À une date et heure précises", html);
    }

    [Fact]
    public async Task An_open_planned_search_is_listed_with_its_local_time_and_can_be_cancelled()
    {
        var (alice, client) = await NewPlayer("Alice");
        await Plan(alice, _evening);

        var html = await Html(client, "/play");

        Assert.Contains("Parties planifiées", html);
        Assert.Contains("Recherche d'un adversaire", html);
        Assert.Contains($"datetime=\"{Iso(_evening)}\"", html);
        Assert.Contains("Annuler", html);
    }

    [Fact]
    public async Task A_planned_game_proposal_shows_the_opponent_the_time_and_the_answer_buttons()
    {
        var ((alice, aliceClient), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening);
        await Plan(bob, _evening.AddMinutes(30));

        var html = await Html(aliceClient, "/play");

        Assert.Contains("Adversaire trouvé pour le", html);
        Assert.Contains("Bob", html);
        Assert.Contains($"datetime=\"{Iso(_evening)}\"", html);
        Assert.Contains("Accepter", html);
        Assert.Contains("Refuser", html);
    }

    [Fact]
    public async Task The_menu_shows_how_many_proposals_are_waiting_for_an_answer()
    {
        var ((alice, aliceClient), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening);
        await Plan(bob, _evening);

        var html = await Html(aliceClient, "/profile");

        Assert.Contains("class=\"nav-badge\"", html);
        Assert.Contains("1 proposition à accepter", html);
    }

    [Fact]
    public async Task Without_proposal_the_menu_has_no_badge()
    {
        var (_, client) = await NewPlayer("Alice");

        Assert.DoesNotContain("class=\"nav-badge\"", await Html(client, "/profile"));
    }

    [Fact]
    public async Task An_accepted_planned_game_shows_its_time_in_my_games_and_on_the_match_page()
    {
        var ((alice, aliceClient), (bob, _)) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        await Plan(alice, _evening);
        await Plan(bob, _evening);
        var proposal = Assert.Single((await Scheduled((games, _) => games.OverviewAsync(alice))).Proposals);
        await Scheduled(async (games, _) => { await games.AcceptAsync(alice, proposal.Id); await games.AcceptAsync(bob, proposal.Id); return 0; });

        var myGames = await Html(aliceClient, "/matches");
        var matchPage = await Html(aliceClient, $"/matches/{proposal.Id}");

        Assert.Contains("Prévue le", myGames);
        Assert.Contains($"datetime=\"{Iso(_evening)}\"", myGames);
        Assert.Contains("Partie prévue le", matchPage);
        Assert.Contains($"datetime=\"{Iso(_evening)}\"", matchPage);
    }

    public void Dispose() => _factory.Dispose();
}
