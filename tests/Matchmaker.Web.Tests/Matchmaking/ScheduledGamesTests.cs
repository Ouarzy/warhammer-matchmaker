using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Data;
using Matchmaker.Web.Matches;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Push;
using Matchmaker.Web.Tests.Infrastructure;
using Matchmaker.Web.Tts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Matchmaking;

[Collection(PostgresCollection.Name)]
public sealed class ScheduledGamesTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly FixedTime _time = new(DateTimeOffset.UtcNow);
    private readonly List<PlayerId> _notified = [];

    // La base est partagée entre les tests : chacun planifie à une date à lui, loin des autres.
    private readonly DateTimeOffset _evening = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero)
        .AddDays(Random.Shared.Next(30, 20_000)).AddHours(20);

    private async Task<T> Using<T>(Func<ScheduledGames, IServiceProvider, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MatchmakerDbContext>();
        var notifier = new PlayerNotifier();
        notifier.PlayerChanged += _notified.Add;
        var outbox = services.GetRequiredService<PushOutbox>();
        var games = new ScheduledGames(db, new MatchService(db, _time, outbox), notifier, outbox, _time);
        return await work(games, services);
    }

    private Task Using(Func<ScheduledGames, Task> work) => Using(async (games, _) => { await work(games); return 0; });

    private async Task<(PlayerId Player, ArmyList List)> NewPlayer(string name, GameFormat? format = null)
    {
        var player = MatchmakerWebFactory.NewPlayerId();
        await _factory.CreateClientAsAsync(player, name);
        var list = await Using((_, services) =>
            services.GetRequiredService<ArmyListService>().ImportAsync(player, Samples.CustodesCode, "Liste", format ?? GameFormats.StrikeForce));
        return (player, list);
    }

    private Task<ScheduledSearch> Plan((PlayerId Player, ArmyList List) who, DateTimeOffset at, bool ranked = true) =>
        Using((games, _) => games.PlanAsync(who.Player, who.List.Id, ranked, at));

    private Task<ScheduledOverview> Overview(PlayerId player) => Using((games, _) => games.OverviewAsync(player));

    private async Task<Match> ProposalBetweenAliceAndBob(
        (PlayerId Player, ArmyList List) alice, (PlayerId Player, ArmyList List) bob)
    {
        await Plan(alice, _evening);
        await Plan(bob, _evening.AddMinutes(40));
        return Assert.Single((await Overview(alice.Player)).Proposals);
    }

    [Fact]
    public async Task Without_a_compatible_player_the_search_stays_open()
    {
        var alice = await NewPlayer("Alice");

        var search = await Plan(alice, _evening);

        Assert.Equal(ScheduledSearchStatus.Open, search.Status);
        var overview = await Overview(alice.Player);
        Assert.Equal(search.Id, Assert.Single(overview.OpenSearches).Id);
        Assert.Empty(overview.Proposals);
    }

    [Fact]
    public async Task A_time_given_in_the_time_zone_of_the_player_is_stored_as_the_same_instant()
    {
        var alice = await NewPlayer("Alice");
        var inParis = _evening.ToOffset(TimeSpan.FromHours(1));

        await Plan(alice, inParis);

        Assert.Equal(_evening, Assert.Single((await Overview(alice.Player)).OpenSearches).PlannedFor);
    }

    [Fact]
    public async Task A_player_planning_close_to_the_same_time_gets_a_proposal_at_the_time_of_the_first()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));

        var proposal = await ProposalBetweenAliceAndBob(alice, bob);

        Assert.Equal(_evening, proposal.ScheduledFor);
        Assert.Equal(MatchStatus.Proposed, proposal.Status);
        Assert.Equal(proposal.Id, Assert.Single((await Overview(bob.Player)).Proposals).Id);
        Assert.Empty((await Overview(alice.Player)).OpenSearches);
        Assert.Contains(alice.Player, _notified);
        Assert.Contains(bob.Player, _notified);
    }

    [Fact]
    public async Task Players_two_hours_apart_are_not_matched()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));

        await Plan(alice, _evening);
        await Plan(bob, _evening.AddHours(2));

        Assert.Empty((await Overview(alice.Player)).Proposals);
        Assert.Single((await Overview(bob.Player)).OpenSearches);
    }

    [Fact]
    public async Task Once_both_players_accept_the_game_is_on_with_lists_archived_and_a_tts_code()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        var proposal = await ProposalBetweenAliceAndBob(alice, bob);

        await Using(games => games.AcceptAsync(alice.Player, proposal.Id));
        await Using(games => games.AcceptAsync(bob.Player, proposal.Id));

        await Using(async (games, services) =>
        {
            var match = await services.GetRequiredService<MatchService>().FindAsync(proposal.Id);
            Assert.Equal(MatchStatus.Accepted, match!.Status);
            Assert.Equal(_evening, match.ScheduledFor);
            Assert.Contains(await services.GetRequiredService<MatchService>().ActiveMatchesOfAsync(alice.Player), m => m.Id == proposal.Id);
            var archives = await services.GetRequiredService<MatchService>().ArchivedListsOfAsync([proposal.Id]);
            Assert.NotNull(archives.For(proposal.Id, alice.Player));
            Assert.NotNull(archives.For(proposal.Id, bob.Player));
            Assert.NotNull(await services.GetRequiredService<TtsReporting>().CodeOfAsync(proposal.Id));
            Assert.Empty((await games.OverviewAsync(alice.Player)).Proposals);
            return 0;
        });
    }

    [Fact]
    public async Task Declining_reopens_the_search_of_the_opponent_and_ends_mine()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        var proposal = await ProposalBetweenAliceAndBob(alice, bob);

        await Using(games => games.DeclineAsync(bob.Player, proposal.Id));

        Assert.Single((await Overview(alice.Player)).OpenSearches);
        Assert.Empty((await Overview(alice.Player)).Proposals);
        Assert.Empty((await Overview(bob.Player)).OpenSearches);
    }

    [Fact]
    public async Task A_reopened_search_is_matched_again_with_another_waiting_player()
    {
        var (alice, bob, carol) = (await NewPlayer("Alice"), await NewPlayer("Bob"), await NewPlayer("Carol"));
        await Plan(alice, _evening);
        await Plan(bob, _evening.AddMinutes(10));
        await Plan(carol, _evening.AddMinutes(30));
        var proposal = Assert.Single((await Overview(alice.Player)).Proposals);

        await Using(games => games.DeclineAsync(bob.Player, proposal.Id));

        var next = Assert.Single((await Overview(alice.Player)).Proposals);
        Assert.Equal(carol.Player, next.OpponentOf(alice.Player).Player);
    }

    [Fact]
    public async Task An_open_search_can_be_cancelled()
    {
        var alice = await NewPlayer("Alice");
        var search = await Plan(alice, _evening);

        await Using(games => games.CancelAsync(alice.Player, search.Id));

        Assert.Empty((await Overview(alice.Player)).OpenSearches);
    }

    [Fact]
    public async Task A_proposal_left_unanswered_expires_and_only_the_player_who_accepted_keeps_searching()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        var proposal = await ProposalBetweenAliceAndBob(alice, bob);
        await Using(games => games.AcceptAsync(alice.Player, proposal.Id));

        _time.Now = proposal.AcceptanceDeadline.AddMinutes(1);
        await Using(games => games.ExpireOverdueAsync());

        Assert.Single((await Overview(alice.Player)).OpenSearches);
        Assert.Empty((await Overview(alice.Player)).Proposals);
        Assert.Empty((await Overview(bob.Player)).OpenSearches);
    }

    [Fact]
    public async Task An_open_search_expires_one_hour_before_its_time()
    {
        var alice = await NewPlayer("Alice");
        await Plan(alice, _evening);

        _time.Now = _evening.AddMinutes(-59);
        await Using(games => games.ExpireOverdueAsync());

        Assert.Empty((await Overview(alice.Player)).OpenSearches);
    }

    [Fact]
    public async Task The_number_of_proposals_to_answer_is_counted_for_the_menu()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));
        var proposal = await ProposalBetweenAliceAndBob(alice, bob);
        await Using(games => games.AcceptAsync(alice.Player, proposal.Id));

        Assert.Equal(0, await Using((games, _) => games.ProposalsToAnswerAsync(alice.Player)));
        Assert.Equal(1, await Using((games, _) => games.ProposalsToAnswerAsync(bob.Player)));
    }

    [Fact]
    public async Task A_player_cannot_plan_more_games_than_the_parallel_limit()
    {
        var alice = await NewPlayer("Alice");
        for (var i = 0; i < MatchService.MaxActiveMatches; i++)
            await Plan(alice, _evening.AddDays(i * 7));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Plan(alice, _evening.AddDays(100)));
    }

    [Fact]
    public async Task A_player_can_only_plan_with_their_own_list()
    {
        var (alice, bob) = (await NewPlayer("Alice"), await NewPlayer("Bob"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Using((games, _) => games.PlanAsync(alice.Player, bob.List.Id, true, _evening)));
    }

    [Fact]
    public async Task A_game_cannot_be_planned_less_than_one_hour_ahead()
    {
        var alice = await NewPlayer("Alice");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Plan(alice, _time.Now.AddMinutes(30)));
    }

    public void Dispose() => _factory.Dispose();
}
