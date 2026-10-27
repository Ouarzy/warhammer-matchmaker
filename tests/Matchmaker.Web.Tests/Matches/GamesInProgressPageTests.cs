using System.Net;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Matches;

[Collection(PostgresCollection.Name)]
public sealed class GamesInProgressPageTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    private async Task<Guid> GameBetween(string first, string second)
    {
        var (a, b) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        await _factory.CreateClientAsAsync(a, $"{first}{_suffix}");
        await _factory.CreateClientAsAsync(b, $"{second}{_suffix}");
        return await _factory.StartMatchAsync(a, b);
    }

    [Fact]
    public async Task Anyone_sees_the_games_in_progress_and_opens_one_in_one_click()
    {
        var matchId = await GameBetween("Alice", "Bob");

        var response = await _factory.CreateAnonymousClient().GetAsync("/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains($"href=\"/matches/{matchId}\"", html);
        Assert.Contains($"Alice{_suffix}", html);
        Assert.Contains($"Bob{_suffix}", html);
        Assert.Contains("Strike Force", html);
    }

    [Fact]
    public async Task Finished_games_are_not_listed()
    {
        var matchId = await GameBetween("Carol", "Dave");
        await _factory.WithMatchesAsync(async matches =>
        {
            var match = (await matches.FindAsync(matchId))!;
            await matches.AbandonAsync(match.First.Player, matchId);
        });

        var html = await _factory.CreateAnonymousClient().GetStringAsync("/live");

        Assert.DoesNotContain($"/matches/{matchId}", html);
    }

    [Fact]
    public async Task The_menu_links_to_the_games_in_progress()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        Assert.Contains("href=\"/live\"", await client.GetStringAsync("/profile"));
        Assert.Contains("href=\"/live\"", await _factory.CreateAnonymousClient().GetStringAsync("/"));
    }

    public void Dispose() => _factory.Dispose();
}
