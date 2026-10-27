using System.Net;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Matches;

[Collection(PostgresCollection.Name)]
public sealed class MatchPageTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task<(HttpClient Alice, PlayerId AliceId, HttpClient Bob, PlayerId BobId, Guid MatchId)> MatchInProgress()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var aliceClient = await _factory.CreateClientAsAsync(alice, "Alice");
        var bobClient = await _factory.CreateClientAsAsync(bob, "Bob");
        return (aliceClient, alice, bobClient, bob, await _factory.StartMatchAsync(alice, bob));
    }

    [Fact]
    public async Task During_a_match_each_player_can_declare_the_result()
    {
        var (alice, _, _, _, matchId) = await MatchInProgress();

        var html = await alice.GetStringAsync($"/matches/{matchId}");

        Assert.Contains("Bob", html);
        Assert.Contains("Déclarer le résultat", html);
        Assert.Contains("Abandonner", html);
    }

    [Fact]
    public async Task Once_declared_the_opponent_is_asked_to_confirm_within_24_hours()
    {
        var (_, alice, bob, _, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 85, 70));

        var html = await bob.GetStringAsync($"/matches/{matchId}");

        Assert.Contains("Votre adversaire a déclaré le résultat", html);
        Assert.Contains("Petite défaite", html);
        Assert.Contains("Confirmer", html);
        Assert.Contains("confirmé automatiquement", html);
    }

    [Fact]
    public async Task The_declaring_player_waits_for_the_confirmation()
    {
        var (alice, aliceId, _, _, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(aliceId, matchId, 85, 70));

        var html = await alice.GetStringAsync($"/matches/{matchId}");

        Assert.Contains("En attente de la confirmation", html);
        Assert.DoesNotContain(">Confirmer<", html);
    }

    [Fact]
    public async Task A_finished_match_shows_its_outcome()
    {
        var (alice, _, _, bobId, matchId) = await MatchInProgress();
        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(bobId, matchId));

        var html = await alice.GetStringAsync($"/matches/{matchId}");

        Assert.Contains("Victoire écrasante", html);
        Assert.Contains("abandon", html);
        Assert.DoesNotContain("Déclarer le résultat", html);
    }

    [Fact]
    public async Task Only_the_players_of_a_match_can_open_it()
    {
        var (_, _, _, _, matchId) = await MatchInProgress();
        var stranger = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await stranger.GetAsync($"/matches/{matchId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_match_page_requires_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync($"/matches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
