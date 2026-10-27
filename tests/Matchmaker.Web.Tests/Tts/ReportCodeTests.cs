using System.Net;
using Matchmaker.Web.Tests.Infrastructure;
using Matchmaker.Web.Tts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Tts;

[Collection(PostgresCollection.Name)]
public sealed class ReportCodeTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task<string?> CodeOf(Guid matchId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TtsReporting>().CodeOfAsync(matchId);
    }

    private async Task<(HttpClient Alice, HttpClient Bob, Guid MatchId)> MatchInProgress()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var aliceClient = await _factory.CreateClientAsAsync(alice);
        var bobClient = await _factory.CreateClientAsAsync(bob);
        return (aliceClient, bobClient, await _factory.StartMatchAsync(alice, bob));
    }

    [Fact]
    public async Task Each_match_gets_its_own_unambiguous_8_character_code()
    {
        var (_, _, first) = await MatchInProgress();
        var (_, _, second) = await MatchInProgress();

        var (code1, code2) = (await CodeOf(first), await CodeOf(second));

        Assert.Matches("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$", code1);
        Assert.NotEqual(code1, code2);
    }

    [Fact]
    public void Generated_codes_avoid_look_alike_characters()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => ReportCode.Generate()).ToList();

        Assert.All(codes, code => Assert.DoesNotMatch("[01OI]", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public async Task Both_players_see_the_code_on_the_match_screen()
    {
        var (alice, bob, matchId) = await MatchInProgress();
        var code = await CodeOf(matchId);

        Assert.Contains(code!, await alice.GetStringAsync($"/matches/{matchId}"));
        Assert.Contains(code!, await bob.GetStringAsync($"/matches/{matchId}"));
    }

    [Fact]
    public async Task The_code_stays_hidden_until_sending_from_tts_is_opened()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        using var closed = _factory.WithWebHostBuilder(builder => builder.UseSetting("Tts:Enabled", "false"));
        await _factory.CreateClientAsAsync(alice);
        await _factory.CreateClientAsAsync(bob);
        var matchId = await _factory.StartMatchAsync(alice, bob);
        var client = closed.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthentication.Header, alice.SteamId);

        var html = await client.GetStringAsync($"/matches/{matchId}");

        Assert.DoesNotContain((await CodeOf(matchId))!, html);
        Assert.DoesNotContain("Envoyer le score depuis TTS", html);
    }

    [Fact]
    public async Task Spectators_never_see_the_code()
    {
        var (_, _, matchId) = await MatchInProgress();
        var code = await CodeOf(matchId);
        var spectator = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        Assert.DoesNotContain(code!, await spectator.GetStringAsync($"/matches/{matchId}"));
        Assert.DoesNotContain(code!, await (await _factory.CreateAnonymousClient().GetAsync($"/matches/{matchId}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_code_is_no_longer_shown_once_the_result_is_declared()
    {
        var (alice, _, matchId) = await MatchInProgress();
        var code = await CodeOf(matchId);
        await _factory.WithMatchesAsync(async matches =>
        {
            var match = (await matches.FindAsync(matchId))!;
            await matches.DeclareResultAsync(match.First.Player, matchId, 85, 70);
        });

        Assert.DoesNotContain(code!, await alice.GetStringAsync($"/matches/{matchId}"));
    }

    public void Dispose() => _factory.Dispose();
}
