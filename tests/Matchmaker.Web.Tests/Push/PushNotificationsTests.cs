using System.Net;
using System.Net.Http.Json;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Push;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Push;

[Collection(PostgresCollection.Name)]
public sealed class PushNotificationsTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    // Base partagée entre les tests : chacun planifie à une date à lui.
    private readonly DateTimeOffset _evening = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
        .AddDays(Random.Shared.Next(30, 20_000)).AddHours(20);

    private static object Subscription(string endpoint) => new { endpoint, keys = new { p256dh = "BKey", auth = "secret" } };

    private async Task<(PlayerId Player, HttpClient Client)> SubscribedPlayer(string name, string? endpoint = null)
    {
        var player = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(player, name);
        var response = await client.PostAsJsonAsync("/api/push/subscriptions", Subscription(endpoint ?? $"https://fcm.googleapis.com/fcm/send/{player.SteamId}"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return (player, client);
    }

    private async Task Plan(PlayerId player, DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        var list = await scope.ServiceProvider.GetRequiredService<ArmyListService>()
            .ImportAsync(player, Samples.CustodesCode, "Liste", GameFormats.StrikeForce);
        await scope.ServiceProvider.GetRequiredService<ScheduledGames>().PlanAsync(player, list.Id, true, at);
    }

    private async Task<T> Scheduled<T>(Func<ScheduledGames, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ScheduledGames>());
    }

    [Fact]
    public async Task Subscribing_requires_to_be_signed_in()
    {
        var response = await _factory.CreateAnonymousClient().PostAsJsonAsync("/api/push/subscriptions", Subscription("https://fcm.googleapis.com/fcm/send/x"));

        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task A_subscription_must_be_an_https_push_endpoint()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.PostAsJsonAsync("/api/push/subscriptions", Subscription("http://evil.example/x"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://10.0.0.1/x")]
    public async Task Only_the_push_services_of_the_browsers_are_accepted(string endpoint)
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.PostAsJsonAsync("/api/push/subscriptions", Subscription(endpoint));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://web.push.apple.com/abc")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc")]
    public async Task The_push_services_of_chrome_firefox_safari_and_edge_are_accepted(string endpoint)
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var response = await client.PostAsJsonAsync("/api/push/subscriptions", Subscription(endpoint));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Only_the_10_most_recent_devices_of_a_player_are_kept()
    {
        var player = MatchmakerWebFactory.NewPlayerId();
        var client = await _factory.CreateClientAsAsync(player);
        for (var i = 0; i <= PushSubscriptions.MaxDevicesPerPlayer; i++)
            await client.PostAsJsonAsync("/api/push/subscriptions", Subscription($"https://fcm.googleapis.com/fcm/send/{player.SteamId}-{i}"));

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(PushSubscriptions.MaxDevicesPerPlayer, await scope.ServiceProvider.GetRequiredService<PushSubscriptions>().CountAsync(player));
    }

    [Fact]
    public async Task A_test_notification_reaches_every_device_of_the_player()
    {
        var (alice, client) = await SubscribedPlayer("Alice");
        await client.PostAsJsonAsync("/api/push/subscriptions", Subscription($"https://fcm.googleapis.com/fcm/send/{alice.SteamId}/phone"));

        await client.PostAsync("/api/push/test", null);

        var received = await _factory.Push.ReceivedByAsync(alice, atLeast: 2);
        Assert.Equal(2, received.Count);
        Assert.All(received, message => Assert.Equal("Notifications activées", message.Title));
    }

    [Fact]
    public async Task An_unsubscribed_device_receives_nothing()
    {
        var endpoint = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid()}";
        var (alice, client) = await SubscribedPlayer("Alice", endpoint);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscriptions") { Content = JsonContent.Create(Subscription(endpoint)) });
        await client.PostAsync("/api/push/test", null);

        await Task.Delay(300);
        Assert.DoesNotContain(_factory.Push.Sent, s => s.Player == alice);
    }

    [Fact]
    public async Task An_expired_subscription_is_forgotten()
    {
        var (alice, client) = await SubscribedPlayer("Alice", $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid()}/gone");

        await client.PostAsync("/api/push/test", null);

        using var scope = _factory.Services.CreateScope();
        var subscriptions = scope.ServiceProvider.GetRequiredService<PushSubscriptions>();
        for (var i = 0; i < 100 && await subscriptions.CountAsync(alice) > 0; i++)
            await Task.Delay(50);
        Assert.Equal(0, await subscriptions.CountAsync(alice));
    }

    [Fact]
    public async Task Both_players_are_notified_of_a_planned_game_proposal_then_of_its_confirmation()
    {
        var ((alice, _), (bob, _)) = (await SubscribedPlayer("Alice"), await SubscribedPlayer("Bob"));
        await Plan(alice, _evening);
        await Plan(bob, _evening.AddMinutes(20));

        var proposal = Assert.Single(await _factory.Push.ReceivedByAsync(alice));
        Assert.Equal("Adversaire trouvé", proposal.Title);
        Assert.Equal(_evening, proposal.At);
        Assert.Contains("{at}", proposal.Body);
        Assert.Equal("Adversaire trouvé", Assert.Single(await _factory.Push.ReceivedByAsync(bob)).Title);

        var match = Assert.Single((await Scheduled(games => games.OverviewAsync(alice))).Proposals);
        await Scheduled(games => games.AcceptAsync(alice, match.Id));
        await Scheduled(games => games.AcceptAsync(bob, match.Id));

        var confirmed = (await _factory.Push.ReceivedByAsync(alice, atLeast: 2))[1];
        Assert.Equal("Partie confirmée", confirmed.Title);
        Assert.Equal($"/matches/{match.Id}", confirmed.Url);
    }

    [Fact]
    public async Task The_player_still_searching_is_told_when_the_opponent_declines()
    {
        var ((alice, _), (bob, _)) = (await SubscribedPlayer("Alice"), await SubscribedPlayer("Bob"));
        await Plan(alice, _evening);
        await Plan(bob, _evening);
        var match = Assert.Single((await Scheduled(games => games.OverviewAsync(alice))).Proposals);

        await Scheduled(async games => { await games.DeclineAsync(bob, match.Id); return 0; });

        Assert.Equal("Proposition annulée", (await _factory.Push.ReceivedByAsync(alice, atLeast: 2))[1].Title);
    }

    [Fact]
    public async Task The_opponent_is_notified_when_a_result_is_declared()
    {
        var ((alice, _), (bob, _)) = (await SubscribedPlayer("Alice"), await SubscribedPlayer("Bob"));
        var matchId = await _factory.StartMatchAsync(alice, bob);

        await _factory.WithMatchesAsync(matches => matches.DeclareResultAsync(alice, matchId, 80, 60));

        var message = Assert.Single(await _factory.Push.ReceivedByAsync(bob));
        Assert.Equal("Résultat à confirmer", message.Title);
        Assert.Equal($"/matches/{matchId}", message.Url);
        await Task.Delay(200);
        Assert.DoesNotContain(_factory.Push.Sent, s => s.Player == alice);
    }

    [Fact]
    public async Task The_opponent_is_notified_of_an_abandon()
    {
        var ((alice, _), (bob, _)) = (await SubscribedPlayer("Alice"), await SubscribedPlayer("Bob"));
        var matchId = await _factory.StartMatchAsync(alice, bob);

        await _factory.WithMatchesAsync(matches => matches.AbandonAsync(alice, matchId));

        Assert.Equal("Victoire par abandon", Assert.Single(await _factory.Push.ReceivedByAsync(bob)).Title);
    }

    [Fact]
    public async Task My_account_offers_to_enable_notifications_on_this_device()
    {
        var (_, client) = await SubscribedPlayer("Alice");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/profile"));

        Assert.Contains("<push-settings", html);
        Assert.Contains($"data-key=\"{MatchmakerWebFactory.TestPublicKey}\"", html);
    }

    public void Dispose() => _factory.Dispose();
}
