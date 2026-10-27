using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.Data;
using Matchmaker.Web.Matches;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Tests.Matches;

[Collection(PostgresCollection.Name)]
public class MatchServiceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTime _time = new(Noon);

    private MatchService Service(MatchmakerDbContext db) => new(db, _time);

    private async Task<PlayerId> NewPlayer()
    {
        var id = MatchmakerWebFactory.NewPlayerId();
        await using var db = postgres.CreateDbContext();
        await new PlayerAccounts(db).SignInAsync(id, $"Joueur {id.SteamId[^4..]}", null);
        return id;
    }

    private static Match AcceptedMatch(PlayerId first, PlayerId second, bool ranked = true, DateTimeOffset? at = null)
    {
        var now = at ?? Noon;
        QueueTicket Ticket(PlayerId player) =>
            new(player, Guid.NewGuid(), GameFormats.StrikeForce, ranked, EloRating.Initial, now);

        var match = Match.Propose(Ticket(first), Ticket(second), now);
        match.Accept(first, now);
        match.Accept(second, now);
        return match;
    }

    private async Task<(PlayerId Alice, PlayerId Bob, Guid MatchId)> StartedMatch(bool ranked = true)
    {
        var (alice, bob) = (await NewPlayer(), await NewPlayer());
        var match = AcceptedMatch(alice, bob, ranked);
        await using var db = postgres.CreateDbContext();
        await Service(db).StartAsync(match);
        return (alice, bob, match.Id);
    }

    private async Task<Player> Reload(PlayerId id)
    {
        await using var db = postgres.CreateDbContext();
        return (await new PlayerAccounts(db).FindAsync(id))!;
    }

    [Fact]
    public async Task A_started_match_is_in_progress_for_both_players()
    {
        var (alice, bob, matchId) = await StartedMatch();

        await using var db = postgres.CreateDbContext();
        Assert.Equal([matchId], (await Service(db).ActiveMatchesOfAsync(alice)).Select(m => m.Id));
        Assert.Equal([matchId], (await Service(db).ActiveMatchesOfAsync(bob)).Select(m => m.Id));
        Assert.Empty(await Service(db).ActiveMatchesOfAsync(await NewPlayer()));
    }

    [Fact]
    public async Task A_player_can_play_several_matches_in_parallel_most_recent_first()
    {
        var (alice, bob, carol) = (await NewPlayer(), await NewPlayer(), await NewPlayer());
        var withBob = AcceptedMatch(alice, bob, at: Noon);
        var withCarol = AcceptedMatch(carol, alice, at: Noon.AddDays(3));
        await using (var db = postgres.CreateDbContext())
        {
            await Service(db).StartAsync(withBob);
            await Service(db).StartAsync(withCarol);
        }

        await using var reloaded = postgres.CreateDbContext();
        Assert.Equal([withCarol.Id, withBob.Id], (await Service(reloaded).ActiveMatchesOfAsync(alice)).Select(m => m.Id));
    }

    [Fact]
    public async Task A_player_can_look_for_another_opponent_until_5_matches_are_in_progress()
    {
        var alice = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        for (var i = 0; i < 4; i++)
            await Service(db).StartAsync(AcceptedMatch(alice, await NewPlayer()));
        Assert.True(await Service(db).CanLookForAnotherOpponentAsync(alice));

        await Service(db).StartAsync(AcceptedMatch(alice, await NewPlayer()));

        Assert.False(await Service(db).CanLookForAnotherOpponentAsync(alice));
    }

    [Fact]
    public async Task A_finished_match_frees_a_slot()
    {
        var alice = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        var matches = new List<Match>();
        for (var i = 0; i < 5; i++)
        {
            var match = AcceptedMatch(alice, await NewPlayer());
            await Service(db).StartAsync(match);
            matches.Add(match);
        }

        await Service(db).AbandonAsync(alice, matches[0].Id);

        Assert.True(await Service(db).CanLookForAnotherOpponentAsync(alice));
    }

    [Fact]
    public async Task Only_an_accepted_match_can_be_started()
    {
        var (alice, bob) = (await NewPlayer(), await NewPlayer());
        var ticket = new QueueTicket(alice, Guid.NewGuid(), GameFormats.StrikeForce, true, EloRating.Initial, Noon);
        var proposed = Match.Propose(ticket, ticket with { Player = bob }, Noon);
        await using var db = postgres.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).StartAsync(proposed));
    }

    [Fact]
    public async Task A_confirmed_ranked_result_updates_both_ratings_once()
    {
        var (alice, bob, matchId) = await StartedMatch();

        await using (var db = postgres.CreateDbContext())
            await Service(db).DeclareResultAsync(alice, matchId, 100, 40);
        await using (var db = postgres.CreateDbContext())
            await Service(db).ConfirmResultAsync(bob, matchId);

        Assert.Equal(1520, (await Reload(alice)).Rating.Value);
        Assert.Equal(1480, (await Reload(bob)).Rating.Value);
        Assert.Equal(1, (await Reload(bob)).RankedGamesPlayed);
        await using var reloaded = postgres.CreateDbContext();
        var match = (await reloaded.Matches.FindAsync(matchId))!;
        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.Equal(20, match.RatingChangeOf(alice));
        Assert.Equal(100, match.VictoryPointsOf(alice));
        Assert.Empty(await Service(reloaded).ActiveMatchesOfAsync(alice));
    }

    [Fact]
    public async Task A_confirmed_unranked_result_leaves_ratings_untouched()
    {
        var (alice, bob, matchId) = await StartedMatch(ranked: false);

        await using (var db = postgres.CreateDbContext())
            await Service(db).DeclareResultAsync(alice, matchId, 100, 40);
        await using (var db = postgres.CreateDbContext())
            await Service(db).ConfirmResultAsync(bob, matchId);

        var reloaded = await Reload(alice);
        Assert.Equal(EloRating.Initial, reloaded.Rating);
        Assert.Equal(0, reloaded.RankedGamesPlayed);
    }

    [Fact]
    public async Task A_disputed_result_leaves_ratings_untouched()
    {
        var (alice, bob, matchId) = await StartedMatch();

        await using (var db = postgres.CreateDbContext())
            await Service(db).DeclareResultAsync(alice, matchId, 100, 40);
        await using (var db = postgres.CreateDbContext())
            await Service(db).DisputeResultAsync(bob, matchId);

        Assert.Equal(EloRating.Initial, (await Reload(alice)).Rating);
        await using var reloaded = postgres.CreateDbContext();
        Assert.Equal(MatchStatus.Disputed, (await reloaded.Matches.FindAsync(matchId))!.Status);
        Assert.Empty(await Service(reloaded).ActiveMatchesOfAsync(bob));
    }

    [Fact]
    public async Task Abandoning_a_ranked_match_gives_the_opponent_a_20_0_victory()
    {
        var (alice, bob, matchId) = await StartedMatch();

        await using (var db = postgres.CreateDbContext())
            await Service(db).AbandonAsync(bob, matchId);

        Assert.Equal(1520, (await Reload(alice)).Rating.Value);
        Assert.Equal(1480, (await Reload(bob)).Rating.Value);
        await using var reloaded = postgres.CreateDbContext();
        Assert.Empty(await Service(reloaded).ActiveMatchesOfAsync(alice));
        Assert.Equal(bob, (await reloaded.Matches.FindAsync(matchId))!.AbandonedBy);
    }

    [Fact]
    public async Task Unanswered_results_are_confirmed_automatically_after_24_hours()
    {
        var (alice, bob, overdue) = await StartedMatch();
        var (carol, dave, recent) = await StartedMatch();
        await using (var db = postgres.CreateDbContext())
        {
            await Service(db).DeclareResultAsync(alice, overdue, 100, 40);
            _time.Now = Noon.AddHours(12);
            await Service(db).DeclareResultAsync(carol, recent, 100, 40);
        }

        _time.Now = Noon.AddHours(24).AddMinutes(1);
        IReadOnlyList<Match> confirmed;
        await using (var db = postgres.CreateDbContext())
            confirmed = await Service(db).ConfirmOverdueResultsAsync();

        Assert.Contains(overdue, confirmed.Select(m => m.Id));
        Assert.DoesNotContain(recent, confirmed.Select(m => m.Id));
        Assert.Equal(1480, (await Reload(bob)).Rating.Value);
        await using var reloaded = postgres.CreateDbContext();
        var match = (await reloaded.Matches.FindAsync(overdue))!;
        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.True(match.ConfirmedAutomatically);
        Assert.Equal(MatchStatus.ResultDeclared, (await reloaded.Matches.FindAsync(recent))!.Status);
    }

    [Fact]
    public async Task Players_cannot_act_on_a_match_they_do_not_play()
    {
        var (_, _, matchId) = await StartedMatch();
        var stranger = await NewPlayer();
        await using var db = postgres.CreateDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).DeclareResultAsync(stranger, matchId, 100, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).AbandonAsync(stranger, Guid.NewGuid()));
    }

    [Fact]
    public async Task Concurrent_updates_of_a_match_are_refused()
    {
        var (alice, bob, matchId) = await StartedMatch();
        await using (var db = postgres.CreateDbContext())
            await Service(db).DeclareResultAsync(alice, matchId, 100, 40);

        await using var stale = postgres.CreateDbContext();
        var staleMatch = (await stale.Matches.FindAsync(matchId))!;
        await using (var db = postgres.CreateDbContext())
            await Service(db).ConfirmResultAsync(bob, matchId);

        staleMatch.ConfirmResult(bob);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public async Task The_history_lists_finished_matches_most_recent_first()
    {
        var (alice, bob) = (await NewPlayer(), await NewPlayer());
        var older = AcceptedMatch(alice, bob, at: Noon);
        var newer = AcceptedMatch(alice, bob, at: Noon.AddHours(2));
        var ongoing = AcceptedMatch(alice, bob, at: Noon.AddHours(3));
        await using (var db = postgres.CreateDbContext())
        {
            foreach (var match in new[] { older, newer, ongoing })
                await Service(db).StartAsync(match);
            await Service(db).AbandonAsync(alice, older.Id);
            await Service(db).DeclareResultAsync(bob, newer.Id, 70, 85);
            await Service(db).ConfirmResultAsync(alice, newer.Id);
        }

        await using var reloaded = postgres.CreateDbContext();
        var history = await Service(reloaded).HistoryOfAsync(alice);

        Assert.Equal([newer.Id, older.Id], history.Select(m => m.Id));
    }

    [Fact]
    public async Task The_whole_history_is_kept_and_browsed_page_by_page()
    {
        var alice = await NewPlayer();
        await using (var db = postgres.CreateDbContext())
        {
            for (var i = 0; i < 25; i++)
            {
                var match = AcceptedMatch(alice, await NewPlayer(), at: Noon.AddMinutes(i));
                await Service(db).StartAsync(match);
                await Service(db).AbandonAsync(alice, match.Id);
            }
        }

        await using var reloaded = postgres.CreateDbContext();
        var firstPage = await Service(reloaded).HistoryPageAsync(alice, page: 1, pageSize: 20);
        var secondPage = await Service(reloaded).HistoryPageAsync(alice, page: 2, pageSize: 20);

        Assert.Equal(25, firstPage.Total);
        Assert.Equal(20, firstPage.Items.Count);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal(2, firstPage.PageCount);
        Assert.Equal(Noon.AddMinutes(24), firstPage.Items[0].ProposedAt);
        Assert.Equal(Noon, secondPage.Items[^1].ProposedAt);
    }

    [Fact]
    public async Task The_ranking_lists_ranked_players_by_rating()
    {
        var (winner, loser, matchId) = await StartedMatch();
        var unranked = await NewPlayer();
        await using (var db = postgres.CreateDbContext())
        {
            await Service(db).DeclareResultAsync(winner, matchId, 100, 40);
            await Service(db).ConfirmResultAsync(loser, matchId);
        }

        await using var reloaded = postgres.CreateDbContext();
        var ranking = (await Service(reloaded).RankingAsync(limit: 1000)).Select(p => p.Id).ToList();

        Assert.True(ranking.IndexOf(winner) < ranking.IndexOf(loser));
        Assert.DoesNotContain(unranked, ranking);
    }
}
