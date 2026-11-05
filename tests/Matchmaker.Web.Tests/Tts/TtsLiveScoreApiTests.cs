using System.Net;
using System.Net.Http.Json;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Tests.Infrastructure;
using Matchmaker.Web.Tts;
using Microsoft.AspNetCore.Hosting;
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

    private static object Report(string code, PlayerId red, int redVp, PlayerId blue, int blueVp, int roundsPlayed = 3,
        PlayerId? host = null, int version = 1) => new
    {
        version,
        code,
        hostSteamId = (host ?? red).SteamId,
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
    public async Task The_code_may_be_typed_in_lower_case_with_spaces()
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await SendLive(Report($"  {code.ToLowerInvariant()} ", alice, 10, bob, 5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_code_is_refused()
    {
        var (alice, bob, _, _) = await MatchInProgress();

        var response = await SendLive(Report("ZZZZZZZZ", alice, 10, bob, 5));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_seated_players_must_be_the_players_of_the_match()
    {
        var (alice, _, _, code) = await MatchInProgress();

        var response = await SendLive(Report(code, alice, 10, MatchmakerWebFactory.NewPlayerId(), 5));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_game_must_be_hosted_by_one_of_the_players()
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await SendLive(Report(code, alice, 10, bob, 5, host: MatchmakerWebFactory.NewPlayerId()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, 101)]
    public async Task Victory_points_must_be_between_0_and_100(int redVp, int blueVp)
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await SendLive(Report(code, alice, redVp, bob, blueVp));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_object_version_is_refused()
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await SendLive(Report(code, alice, 10, bob, 5, version: 99));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task No_live_score_once_a_final_score_is_proposed()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();
        await SendLive(Report(code, alice, 10, bob, 5));
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 60, 50));

        var response = await SendLive(Report(code, alice, 12, bob, 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("Score en direct", await MatchPage(matchId));
    }

    [Fact]
    public async Task The_live_score_resumes_once_the_proposed_score_is_cancelled()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();
        await _factory.WithMatchesAsync(async matches =>
        {
            await matches.DeclareResultAsync(alice, matchId, 60, 50);
            await matches.CancelResultAsync(bob, matchId);
        });

        var response = await SendLive(Report(code, alice, 62, bob, 50, roundsPlayed: 4));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alice 62 – 50 Bob", await MatchPage(matchId));
    }

    [Fact]
    public async Task Sending_is_limited_per_minute_to_prevent_guessing_codes()
    {
        using var limited = new MatchmakerWebFactory(postgres);
        using var strict = limited.WithWebHostBuilder(builder => builder.UseSetting("Tts:RequestsPerMinute", "3"));
        var report = Report("ZZZZZZZZ", MatchmakerWebFactory.NewPlayerId(), 1, MatchmakerWebFactory.NewPlayerId(), 2);
        var client = strict.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/tts/live", report)).StatusCode);

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
    }

    public void Dispose() => _factory.Dispose();
}
