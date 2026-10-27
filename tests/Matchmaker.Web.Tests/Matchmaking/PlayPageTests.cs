using System.Net;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matches;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Matchmaking;

[Collection(PostgresCollection.Name)]
public sealed class PlayPageTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    [Fact]
    public async Task Playing_requires_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/play");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task A_player_without_list_is_invited_to_import_one()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync("/play");

        Assert.Contains("Importez d'abord une liste", html);
        Assert.Contains("href=\"/lists\"", html);
    }

    [Fact]
    public async Task A_player_chooses_one_of_their_lists_and_whether_the_game_is_ranked()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ArmyListService>()
                .ImportAsync(me, Samples.CustodesCode, "Mes Custodes", GameFormats.StrikeForce);

        var html = await client.GetStringAsync("/play");

        Assert.Contains("Mes Custodes (Strike Force)", html);
        Assert.Contains("Partie classée", html);
        Assert.Contains("Rechercher un adversaire", html);
    }

    private async Task<(HttpClient Alice, HttpClient Bob, PlayerId AliceId, Guid MatchId)> MatchInProgress()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var aliceClient = await _factory.CreateClientAsAsync(alice, "Alice");
        var bobClient = await _factory.CreateClientAsAsync(bob, "Bob");

        var now = DateTimeOffset.UtcNow;
        QueueTicket Ticket(PlayerId player) => new(player, Guid.NewGuid(), GameFormats.StrikeForce, true, EloRating.Initial, now);
        var match = Match.Propose(Ticket(alice), Ticket(bob), now);
        match.Accept(alice, now);
        match.Accept(bob, now);
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MatchService>().StartAsync(match);
        return (aliceClient, bobClient, alice, match.Id);
    }

    [Fact]
    public async Task During_a_match_the_players_can_declare_the_result()
    {
        var (alice, _, _, _) = await MatchInProgress();

        var html = await alice.GetStringAsync("/play");

        Assert.Contains("Match accepté", html);
        Assert.Contains("Bob", html);
        Assert.Contains("Déclarer le résultat", html);
        Assert.DoesNotContain("Rechercher un adversaire", html);
    }

    [Fact]
    public async Task Once_declared_the_opponent_is_asked_to_confirm()
    {
        var (_, bob, alice, matchId) = await MatchInProgress();
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<MatchService>().DeclareResultAsync(alice, matchId, 85, 70);

        var html = await bob.GetStringAsync("/play");

        Assert.Contains("Votre adversaire a déclaré le résultat", html);
        Assert.Contains("Petite défaite", html);
        Assert.Contains("Confirmer", html);
    }

    public void Dispose() => _factory.Dispose();
}
