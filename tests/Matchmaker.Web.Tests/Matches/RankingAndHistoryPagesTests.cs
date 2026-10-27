using System.Net;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Matches;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Matches;

[Collection(PostgresCollection.Name)]
public sealed class RankingAndHistoryPagesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private async Task PlayRankedMatch(PlayerId winner, PlayerId loser, int winnerVp, int loserVp)
    {
        var now = DateTimeOffset.UtcNow;
        QueueTicket Ticket(PlayerId player) => new(player, Guid.NewGuid(), GameFormats.StrikeForce, true, EloRating.Initial, now);
        var match = Match.Propose(Ticket(winner), Ticket(loser), now);
        match.Accept(winner, now);
        match.Accept(loser, now);

        using var scope = _factory.Services.CreateScope();
        var matches = scope.ServiceProvider.GetRequiredService<MatchService>();
        await matches.StartAsync(match);
        await matches.DeclareResultAsync(winner, match.Id, winnerVp, loserVp);
        await matches.ConfirmResultAsync(loser, match.Id);
    }

    [Fact]
    public async Task The_ranking_is_public_and_orders_ranked_players_by_rating()
    {
        var suffix = Random.Shared.Next(100000);
        var (winner, loser, newcomer) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        await _factory.CreateClientAsAsync(winner, $"Gagnant{suffix}");
        await _factory.CreateClientAsAsync(loser, $"Perdant{suffix}");
        await _factory.CreateClientAsAsync(newcomer, $"Nouveau{suffix}");
        await PlayRankedMatch(winner, loser, 100, 40);

        var response = await _factory.CreateAnonymousClient().GetAsync("/ranking");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(html.IndexOf($"Gagnant{suffix}") < html.IndexOf($"Perdant{suffix}"));
        Assert.Contains("1520", html);
        Assert.DoesNotContain($"Nouveau{suffix}", html);
    }

    [Fact]
    public async Task The_profile_shows_the_match_history_with_rating_changes()
    {
        var suffix = Random.Shared.Next(100000);
        var (me, opponent) = (MatchmakerWebFactory.NewPlayerId(), MatchmakerWebFactory.NewPlayerId());
        var client = await _factory.CreateClientAsAsync(me, $"Moi{suffix}");
        await _factory.CreateClientAsAsync(opponent, $"Rival{suffix}");
        await PlayRankedMatch(me, opponent, 85, 70);

        // Texte affiché par le navigateur : l'encodeur HTML écrit « + » sous la forme &#x2B;.
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/profile"));

        Assert.Contains("Historique", html);
        Assert.Contains($"contre Rival{suffix}", html);
        Assert.Contains("Petite victoire", html);
        Assert.Contains("Elo +4", html);
    }

    [Fact]
    public async Task A_new_player_has_an_empty_history()
    {
        var client = await _factory.CreateClientAsAsync(MatchmakerWebFactory.NewPlayerId());

        var html = await client.GetStringAsync("/profile");

        Assert.Contains("Aucun match terminé", html);
    }

    public void Dispose() => _factory.Dispose();
}
