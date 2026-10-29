using System.Net;
using System.Net.Http.Json;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Tests.Infrastructure;
using Matchmaker.Web.Tts;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Tts;

[Collection(PostgresCollection.Name)]
public sealed class TtsLiveScoreApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task<(PlayerId Alice, PlayerId Bob, Guid MatchId, string Code)> MatchInProgress()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        await _factory.CreateClientAsAsync(alice, "Alice");
        await _factory.CreateClientAsAsync(bob, "Bob");
        var matchId = await _factory.StartMatchAsync(alice, bob);
        using var scope = _factory.Services.CreateScope();
        return (alice, bob, matchId, (await scope.ServiceProvider.GetRequiredService<TtsReporting>().CodeOfAsync(matchId))!);
    }

    private static object Report(string code, PlayerId red, int redVp, PlayerId blue, int blueVp, int roundsPlayed = 3) => new
    {
        version = 1,
        code,
        hostSteamId = red.SteamId,
        red = new { steamId = red.SteamId, vp = redVp },
        blue = new { steamId = blue.SteamId, vp = blueVp },
        roundsPlayed,
    };

    private Task<HttpResponseMessage> SendLive(object report) =>
        _factory.CreateAnonymousClient().PostAsJsonAsync("/api/tts/live", report);

    private async Task<string> MatchPage(Guid matchId) =>
        WebUtility.HtmlDecode(await _factory.CreateAnonymousClient().GetStringAsync($"/matches/{matchId}"));

    [Fact]
    public async Task The_score_in_progress_is_shown_live_on_the_match_page()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();

        var response = await SendLive(Report(code, red: bob, redVp: 42, blue: alice, blueVp: 37, roundsPlayed: 3));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await MatchPage(matchId);
        Assert.Contains("Score en direct", html);
        Assert.Contains("Alice 37 – 42 Bob", html);
        Assert.Contains("round 3", html);
    }

    [Fact]
    public async Task A_player_finds_the_live_score_in_the_result_form_ready_to_declare()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();
        await SendLive(Report(code, red: bob, redVp: 42, blue: alice, blueVp: 37, roundsPlayed: 3));

        var client = await _factory.CreateClientAsAsync(alice, "Alice");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/matches/{matchId}"));

        Assert.DoesNotContain("live-value", html);
        Assert.Contains("Valider la partie", html);
        Assert.Contains("value=\"37\"", html);
        Assert.Contains("value=\"42\"", html);
        Assert.Contains("round 3", html);
    }

    [Fact]
    public async Task The_live_score_does_not_declare_any_result()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();

        await SendLive(Report(code, alice, 80, bob, 20));

        Match? match = null;
        await _factory.WithMatchesAsync(async matches => match = await matches.FindAsync(matchId));
        Assert.Equal(MatchStatus.Accepted, match!.Status);
    }

    [Fact]
    public async Task The_latest_live_score_replaces_the_previous_one()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();

        await SendLive(Report(code, alice, 10, bob, 5, roundsPlayed: 1));
        await SendLive(Report(code, alice, 25, bob, 30, roundsPlayed: 2));

        var html = await MatchPage(matchId);
        Assert.Contains("Alice 25 – 30 Bob", html);
        Assert.DoesNotContain("Alice 10 – 5 Bob", html);
    }

    [Fact]
    public async Task The_live_score_is_checked_like_a_final_score()
    {
        var (alice, _, _, code) = await MatchInProgress();

        var response = await SendLive(Report(code, alice, 10, MatchmakerWebFactory.NewPlayerId(), 5));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task No_live_score_once_the_result_is_declared()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();
        await SendLive(Report(code, alice, 10, bob, 5));
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 60, 50));

        var response = await SendLive(Report(code, alice, 12, bob, 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Score en direct", await MatchPage(matchId));
    }

    public void Dispose() => _factory.Dispose();
}
