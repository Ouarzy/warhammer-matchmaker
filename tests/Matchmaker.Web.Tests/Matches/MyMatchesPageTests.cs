using System.Net;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Matches;

[Collection(PostgresCollection.Name)]
public sealed class MyMatchesPageTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    private async Task<PlayerId> NewPlayer(string name)
    {
        var id = MatchmakerWebFactory.NewPlayerId();
        await _factory.CreateClientAsAsync(id, $"{name}{_suffix}");
        return id;
    }

    [Fact]
    public async Task My_matches_require_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/matches");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task My_matches_list_the_games_in_progress_and_the_finished_ones()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me, $"Moi{_suffix}");
        var inProgress = await _factory.StartMatchAsync(me, await NewPlayer("Bob"));
        var carol = await NewPlayer("Carol");
        var finished = await _factory.StartMatchAsync(me, carol);
        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(carol, finished));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/matches"));

        Assert.Contains("Parties en cours (1/5)", html);
        Assert.Contains($"Bob{_suffix}", html);
        Assert.Contains($"href=\"/matches/{inProgress}\"", html);
        Assert.Contains("Parties terminées", html);
        Assert.Contains($"contre Carol{_suffix}", html);
        Assert.Contains($"href=\"/matches/{finished}\"", html);
    }

    [Fact]
    public async Task Without_any_game_the_player_is_invited_to_look_for_one()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/matches"));

        Assert.Contains("Aucune partie en cours", html);
        Assert.Contains("href=\"/play\"", html);
    }

    public void Dispose() => _factory.Dispose();
}
