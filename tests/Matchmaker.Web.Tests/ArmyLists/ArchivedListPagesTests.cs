using System.Net;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.ArmyLists;

[Collection(PostgresCollection.Name)]
public sealed class ArchivedListPagesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly int _suffix = Random.Shared.Next(100000);

    private async Task<(HttpClient Alice, PlayerId AliceId, PlayerId BobId, Guid MatchId)> MatchBetweenAliceAndBob()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var client = await _factory.CreateClientAsAsync(alice, $"Alice{_suffix}");
        await _factory.CreateClientAsAsync(bob, $"Bob{_suffix}");
        return (client, alice, bob, await _factory.StartMatchAsync(alice, bob));
    }

    private static async Task<string> Text(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task An_archived_list_shows_the_list_as_it_was_played()
    {
        var (client, alice, _, matchId) = await MatchBetweenAliceAndBob();
        var archive = (await _factory.ArchivedListsOfAsync(matchId)).For(matchId, alice)!;

        var html = await Text(await client.GetAsync($"/archived-lists/{archive.Id}"));

        Assert.Contains("Liste de match", html);
        Assert.Contains("Liste archivée", html);
        Assert.Contains($"Alice{_suffix}", html);
        Assert.Contains("Trajann Valoris", html);
        Assert.Contains("Watcher's Axe", html);
    }

    [Fact]
    public async Task The_detachment_played_is_shown_on_the_archive_and_the_match_screen()
    {
        var (alice, bob) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var client = await _factory.CreateClientAsAsync(alice, $"Alice{_suffix}");
        await _factory.CreateClientAsAsync(bob, $"Bob{_suffix}");
        var matchId = await _factory.StartMatchAsync(alice, bob, detachment: "Shield Host");
        var archive = (await _factory.ArchivedListsOfAsync(matchId)).For(matchId, alice)!;

        Assert.Contains("Détachement Shield Host", await Text(await client.GetAsync($"/archived-lists/{archive.Id}")));
        Assert.Contains("Shield Host", await Text(await client.GetAsync($"/matches/{matchId}")));
    }

    [Fact]
    public async Task Units_do_not_repeat_their_weapons_below_the_tables()
    {
        var (client, alice, _, matchId) = await MatchBetweenAliceAndBob();
        var archive = (await _factory.ArchivedListsOfAsync(matchId)).For(matchId, alice)!;

        var html = await Text(await client.GetAsync($"/archived-lists/{archive.Id}"));

        Assert.DoesNotContain("class=\"composition\"", html);
        Assert.DoesNotContain("Trajann Valoris : ", html);
    }

    [Fact]
    public async Task The_match_screen_links_to_the_lists_played_by_both_players()
    {
        var (client, alice, bob, matchId) = await MatchBetweenAliceAndBob();
        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(bob, matchId));
        var archives = await _factory.ArchivedListsOfAsync(matchId);

        var html = await Text(await client.GetAsync($"/matches/{matchId}"));

        Assert.Contains($"href=\"/archived-lists/{archives.For(matchId, alice)!.Id}\"", html);
        Assert.Contains($"href=\"/archived-lists/{archives.For(matchId, bob)!.Id}\"", html);
    }

    [Fact]
    public async Task During_a_match_the_opponent_list_link_points_to_the_archive()
    {
        var (client, _, bob, matchId) = await MatchBetweenAliceAndBob();
        var bobArchive = (await _factory.ArchivedListsOfAsync(matchId)).For(matchId, bob)!;

        var html = await Text(await client.GetAsync($"/matches/{matchId}"));

        Assert.Contains($"href=\"/archived-lists/{bobArchive.Id}\"", html);
    }

    [Fact]
    public async Task An_archived_list_requires_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync($"/archived-lists/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_archived_list_is_not_found()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.GetAsync($"/archived-lists/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
