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
public sealed class TtsResultsApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private sealed record Answer(string Message);

    private async Task<(PlayerId First, PlayerId Second, Guid MatchId, string Code)> MatchInProgress()
    {
        var (first, second) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        await _factory.CreateClientAsAsync(first, "Alice");
        await _factory.CreateClientAsAsync(second, "Bob");
        var matchId = await _factory.StartMatchAsync(first, second);
        using var scope = _factory.Services.CreateScope();
        var code = (await scope.ServiceProvider.GetRequiredService<TtsReporting>().CodeOfAsync(matchId))!;
        return (first, second, matchId, code);
    }

    private static object Report(string code, PlayerId host, PlayerId red, int redVp, PlayerId blue, int blueVp, int version = 1) => new
    {
        version,
        code,
        hostSteamId = host.SteamId,
        red = new { steamId = red.SteamId, vp = redVp },
        blue = new { steamId = blue.SteamId, vp = blueVp },
        roundsPlayed = 5,
    };

    private Task<HttpResponseMessage> Send(object report, MatchmakerWebFactory? factory = null) =>
        (factory ?? _factory).CreateAnonymousClient().PostAsJsonAsync("/api/tts/results", report);

    private async Task<Match> Reload(Guid matchId)
    {
        Match? match = null;
        await _factory.WithMatchesAsync(async matches => match = await matches.FindAsync(matchId));
        return match!;
    }

    [Fact]
    public async Task A_score_sent_from_tts_is_declared_on_behalf_of_the_host()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();

        var response = await Send(Report(code, host: alice, red: alice, redVp: 85, blue: bob, blueVp: 70));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("en attente de confirmation", (await response.Content.ReadFromJsonAsync<Answer>())!.Message);
        var match = await Reload(matchId);
        Assert.Equal(MatchStatus.ResultDeclared, match.Status);
        Assert.Equal(alice, match.Declarer);
        Assert.Equal(85, match.VictoryPointsOf(alice));
        Assert.Equal(70, match.VictoryPointsOf(bob));
    }

    [Fact]
    public async Task Colors_do_not_have_to_follow_the_order_of_the_match()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();

        await Send(Report(code, host: bob, red: bob, redVp: 40, blue: alice, blueVp: 90));

        var match = await Reload(matchId);
        Assert.Equal(bob, match.Declarer);
        Assert.Equal(90, match.VictoryPointsOf(alice));
        Assert.Equal(40, match.VictoryPointsOf(bob));
    }

    [Fact]
    public async Task The_code_may_be_typed_in_lower_case_with_spaces()
    {
        var (alice, bob, matchId, code) = await MatchInProgress();

        var response = await Send(Report($"  {code.ToLowerInvariant()} ", alice, alice, 85, bob, 70));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_code_is_refused()
    {
        var (alice, bob, _, _) = await MatchInProgress();

        var response = await Send(Report("ZZZZZZZZ", alice, alice, 85, bob, 70));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_seated_players_must_be_the_players_of_the_match()
    {
        var (alice, _, matchId, code) = await MatchInProgress();
        var intruder = MatchmakerWebFactory.NewPlayerId();

        var response = await Send(Report(code, alice, alice, 85, intruder, 70));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(MatchStatus.Accepted, (await Reload(matchId)).Status);
    }

    [Fact]
    public async Task The_game_must_be_hosted_by_one_of_the_players()
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await Send(Report(code, host: MatchmakerWebFactory.NewPlayerId(), alice, 85, bob, 70));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_result_cannot_be_sent_twice()
    {
        var (alice, bob, _, code) = await MatchInProgress();
        await Send(Report(code, alice, alice, 85, bob, 70));

        var response = await Send(Report(code, bob, alice, 10, bob, 90));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(50, 101)]
    public async Task Victory_points_must_be_between_0_and_100(int redVp, int blueVp)
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await Send(Report(code, alice, alice, redVp, bob, blueVp));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_object_version_is_refused()
    {
        var (alice, bob, _, code) = await MatchInProgress();

        var response = await Send(Report(code, alice, alice, 85, bob, 70, version: 99));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sending_is_limited_per_minute_to_prevent_guessing_codes()
    {
        using var limited = new MatchmakerWebFactory(postgres);
        using var strict = limited.WithWebHostBuilder(builder => builder.UseSetting("Tts:RequestsPerMinute", "3"));
        var report = Report("ZZZZZZZZ", MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId(), 1, MatchmakerWebFactory.NewPlayerId(), 2);
        var client = strict.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            statuses.Add((await client.PostAsJsonAsync("/api/tts/results", report)).StatusCode);

        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);
    }

    public void Dispose() => _factory.Dispose();
}
