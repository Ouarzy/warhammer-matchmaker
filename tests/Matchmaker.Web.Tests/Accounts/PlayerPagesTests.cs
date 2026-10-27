using System.Net;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class PlayerPagesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    private async Task<PlayerId> NewPlayer(string name)
    {
        var id = MatchmakerWebFactory.NewPlayerId();
        await _factory.CreateClientAsAsync(id, $"{name}{_suffix}");
        return id;
    }

    private async Task Abandoned(PlayerId winner, PlayerId loser)
    {
        var matchId = await _factory.StartMatchAsync(winner, loser);
        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(loser, matchId));
    }

    private static async Task<string> Text(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Anyone_can_search_a_player_by_name()
    {
        var ultramarine = await NewPlayer("Ultramarine");
        await NewPlayer("Necron");

        var response = await _factory.CreateAnonymousClient().GetAsync($"/players?q=marine{_suffix}");
        var html = await Text(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"href=\"/players/{ultramarine.SteamId}\"", html);
        Assert.DoesNotContain($"Necron{_suffix}", html);
    }

    [Fact]
    public async Task A_search_without_result_says_so()
    {
        var html = await Text(await _factory.CreateAnonymousClient().GetAsync($"/players?q=Personne{_suffix}"));

        Assert.Contains("Aucun joueur", html);
    }

    [Fact]
    public async Task The_lists_of_a_profile_show_their_detachment_when_known()
    {
        var player = await NewPlayer("Custodes");
        using (var scope = _factory.Services.CreateScope())
        {
            var lists = scope.ServiceProvider.GetRequiredService<ArmyListService>();
            await lists.ImportAsync(player, Samples.CustodesCode, "Avec détachement", GameFormats.StrikeForce, "Shield Host");
            await lists.ImportAsync(player, Samples.CustodesCode, "Sans détachement", GameFormats.StrikeForce);
        }

        var html = await Text(await _factory.CreateAnonymousClient().GetAsync($"/players/{player.SteamId}"));

        Assert.Contains("· Adeptus Custodes · Shield Host · Strike Force", html);
        Assert.Contains("· Adeptus Custodes · Strike Force", html);
    }

    [Fact]
    public async Task A_player_profile_shows_rating_lists_and_history()
    {
        var (custodes, rival) = (await NewPlayer("Custodes"), await NewPlayer("Rival"));
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ArmyListService>()
                .ImportAsync(custodes, Samples.CustodesCode, $"Liste tournoi {_suffix}", GameFormats.StrikeForce);
        var matchId = await _factory.StartMatchAsync(custodes, rival);
        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(rival, matchId));

        var response = await _factory.CreateAnonymousClient().GetAsync($"/players/{custodes.SteamId}");
        var html = await Text(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Custodes{_suffix}", html);
        Assert.Contains("<span class=\"stat-label\">Elo</span> <strong>1520</strong>", html);
        Assert.Contains($"Liste tournoi {_suffix}", html);
        Assert.Contains("Victoire écrasante", html);
        Assert.Contains($"contre Rival{_suffix}", html);
        Assert.Contains($"href=\"/matches/{matchId}\"", html);
    }

    [Fact]
    public async Task The_history_of_a_player_is_browsed_page_by_page()
    {
        var veteran = await NewPlayer("Veteran");
        for (var i = 0; i < 21; i++)
            await Abandoned(veteran, await NewPlayer($"Adversaire{i}x"));

        var first = await Text(await _factory.CreateAnonymousClient().GetAsync($"/players/{veteran.SteamId}"));
        var second = await Text(await _factory.CreateAnonymousClient().GetAsync($"/players/{veteran.SteamId}?page=2"));

        Assert.Contains("21 parties", first);
        Assert.Contains("Page 1 / 2", first);
        Assert.Contains($"href=\"/players/{veteran.SteamId}?page=2\"", first);
        Assert.Contains($"Adversaire0x{_suffix}", second);
        Assert.DoesNotContain($"Adversaire0x{_suffix}", first);
    }

    [Fact]
    public async Task An_unknown_player_is_not_found()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/players/76561190000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Names_in_the_ranking_lead_to_the_player_profiles()
    {
        var (winner, loser) = (await NewPlayer("Champion"), await NewPlayer("Second"));
        await Abandoned(winner, loser);

        var html = await Text(await _factory.CreateAnonymousClient().GetAsync("/ranking"));

        Assert.Contains($"href=\"/players/{winner.SteamId}\"", html);
    }

    public void Dispose() => _factory.Dispose();
}
