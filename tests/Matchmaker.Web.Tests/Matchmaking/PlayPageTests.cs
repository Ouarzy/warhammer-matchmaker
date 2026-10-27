using System.Net;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Web.ArmyLists;
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

    [Fact]
    public async Task The_matches_in_progress_are_listed_and_the_player_can_still_search()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        foreach (var name in new[] { "Bob", "Carol" })
        {
            var opponent = MatchmakerWebFactory.NewPlayerId();
            await _factory.CreateClientAsAsync(opponent, name);
            await _factory.StartMatchAsync(me, opponent);
        }
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ArmyListService>()
                .ImportAsync(me, Samples.CustodesCode, null, GameFormats.StrikeForce);

        var html = await client.GetStringAsync("/play");

        Assert.Contains("Mes parties en cours (2/5)", html);
        Assert.Contains("Bob", html);
        Assert.Contains("Carol", html);
        Assert.Contains("Rechercher un adversaire", html);
    }

    [Fact]
    public async Task With_5_matches_in_progress_the_player_cannot_search()
    {
        var me = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(me);
        for (var i = 0; i < 5; i++)
        {
            var opponent = MatchmakerWebFactory.NewPlayerId();
            await _factory.CreateClientAsAsync(opponent);
            await _factory.StartMatchAsync(me, opponent);
        }

        var html = await client.GetStringAsync("/play");

        Assert.Contains("Mes parties en cours (5/5)", html);
        Assert.DoesNotContain("Rechercher un adversaire", html);
        Assert.Contains("Terminez une partie", html);
    }

    public void Dispose() => _factory.Dispose();
}
