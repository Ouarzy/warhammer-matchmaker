using System.Net;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Matches;

[Collection(PostgresCollection.Name)]
public sealed class MatchPageTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    private async Task<(HttpClient Alice, PlayerId AliceId, HttpClient Bob, PlayerId BobId, Guid MatchId)> MatchInProgress()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var aliceClient = await _factory.CreateClientAsAsync(alice, $"Alice{_suffix}");
        var bobClient = await _factory.CreateClientAsAsync(bob, $"Bob{_suffix}");
        return (aliceClient, alice, bobClient, bob, await _factory.StartMatchAsync(alice, bob));
    }

    private static async Task<string> Text(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    private async Task<string> AnonymousView(Guid matchId) =>
        await Text(await _factory.CreateAnonymousClient().GetAsync($"/matches/{matchId}"));

    [Fact]
    public async Task Anyone_can_see_the_players_and_their_lists()
    {
        var (_, alice, _, bob, matchId) = await MatchInProgress();
        var archives = await _factory.ArchivedListsOfAsync(matchId);

        var html = await AnonymousView(matchId);

        Assert.Contains($"href=\"/players/{alice.SteamId}\"", html);
        Assert.Contains($"href=\"/players/{bob.SteamId}\"", html);
        Assert.Contains($"href=\"/archived-lists/{archives.For(matchId, alice)!.Id}\"", html);
        Assert.Contains($"href=\"/archived-lists/{archives.For(matchId, bob)!.Id}\"", html);
    }

    [Fact]
    public async Task A_match_in_progress_has_no_score_yet()
    {
        var (_, _, _, _, matchId) = await MatchInProgress();

        var html = await AnonymousView(matchId);

        Assert.Contains("Partie en cours", html);
        Assert.Contains("Pas encore de score", html);
    }

    [Fact]
    public async Task A_declared_score_is_shown_as_awaiting_confirmation()
    {
        var (_, alice, _, _, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 85, 70));

        var html = await AnonymousView(matchId);

        Assert.Contains("En attente de confirmation", html);
        Assert.Contains("85 VP", html);
        Assert.Contains("70 VP", html);
    }

    [Fact]
    public async Task A_finished_match_shows_the_final_score_and_rating_changes()
    {
        var (_, alice, _, bob, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(async matches =>
        {
            await matches.DeclareResultAsync(alice, matchId, 85, 70);
            await matches.ConfirmResultAsync(bob, matchId);
        });

        var html = await AnonymousView(matchId);

        Assert.Contains("Partie terminée", html);
        Assert.Contains("85 VP", html);
        Assert.Contains("WTC 12", html);
        Assert.Contains("WTC 8", html);
        Assert.Contains("Petite victoire", html);
        Assert.Contains("Petite défaite", html);
        Assert.Contains("Elo +4", html);
        Assert.Contains("Elo -4", html);
    }

    [Fact]
    public async Task An_abandon_is_shown_as_a_20_0()
    {
        var (_, _, _, bob, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(bob, matchId));

        var html = await AnonymousView(matchId);

        Assert.Contains("WTC 20", html);
        Assert.Contains("WTC 0", html);
        Assert.Contains("Abandon", html);
    }

    [Fact]
    public async Task During_a_match_each_player_can_declare_the_result()
    {
        var (alice, _, _, _, matchId) = await MatchInProgress();

        var html = await Text(await alice.GetAsync($"/matches/{matchId}"));

        Assert.Contains("Valider la partie", html);
        Assert.Contains("Abandonner", html);
    }

    [Fact]
    public async Task Once_declared_the_opponent_is_asked_to_confirm_within_24_hours()
    {
        var (_, alice, bob, _, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 85, 70));

        var html = await Text(await bob.GetAsync($"/matches/{matchId}"));

        Assert.Contains("Votre adversaire a déclaré le résultat", html);
        Assert.Contains("Confirmer", html);
        Assert.Contains("confirmé automatiquement", html);
    }

    [Fact]
    public async Task The_declaring_player_waits_for_the_confirmation()
    {
        var (alice, aliceId, _, _, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(aliceId, matchId, 85, 70));

        var html = await Text(await alice.GetAsync($"/matches/{matchId}"));

        Assert.Contains("En attente de la réponse de votre adversaire", html);
        Assert.DoesNotContain(">Confirmer<", html);
    }

    [Fact]
    public async Task The_opponent_can_contest_by_proposing_another_score()
    {
        var (_, alice, bob, _, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 85, 70));

        var html = await Text(await bob.GetAsync($"/matches/{matchId}"));

        Assert.Contains("Contester et proposer un autre score", html);
    }

    [Fact]
    public async Task A_counter_proposal_is_submitted_to_the_player_who_declared_first()
    {
        var (aliceClient, alice, _, bob, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(async matches =>
        {
            await matches.DeclareResultAsync(alice, matchId, 85, 70);
            await matches.ContestResultAsync(bob, matchId, 80, 75);
        });

        var html = await Text(await aliceClient.GetAsync($"/matches/{matchId}"));

        Assert.Contains("Votre adversaire conteste et propose un autre score", html);
        Assert.Contains("Vous <strong>75</strong>", html);
        Assert.Contains("Confirmer", html);
        Assert.Contains("Contester et proposer un autre score", html);
        Assert.Contains("Score contesté, en discussion", await AnonymousView(matchId));
    }

    [Fact]
    public async Task Spectators_cannot_act_on_the_match()
    {
        var (_, _, _, _, matchId) = await MatchInProgress();
        var spectator = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await Text(await spectator.GetAsync($"/matches/{matchId}"));

        Assert.Contains("Partie en cours", html);
        Assert.DoesNotContain("Valider la partie", html);
        Assert.DoesNotContain("Abandonner", html);
    }

    [Fact]
    public async Task An_unknown_match_is_not_found()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync($"/matches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
