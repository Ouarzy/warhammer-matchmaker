using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using static Matchmaker.Domain.Tests.Tickets;

namespace Matchmaker.Domain.Tests.Matchmaking;

public class ScheduledSearchTests
{
    private static readonly DateTimeOffset Evening = Noon.AddDays(2).AddHours(9);

    private static ScheduledSearch Search(string player, DateTimeOffset plannedFor, GameFormatChoice? choice = null, int rating = 1500) =>
        ScheduledSearch.Open(For(player, choice?.Format, choice?.Ranked ?? true, rating), plannedFor, Noon);

    public sealed record GameFormatChoice(GameFormat Format, bool Ranked);

    [Fact]
    public void A_search_is_open_until_an_opponent_is_found()
    {
        var search = Search("alice", Evening);

        Assert.Equal(ScheduledSearchStatus.Open, search.Status);
        Assert.Equal(Evening, search.PlannedFor);
        Assert.Null(search.MatchId);
    }

    [Fact]
    public void A_game_cannot_be_planned_less_than_one_hour_ahead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Search("alice", Noon.AddMinutes(30)));
    }

    [Fact]
    public void The_opponent_is_the_player_planning_closest_to_the_same_time()
    {
        var bob = Search("bob", Evening.AddMinutes(50));
        var carol = Search("carol", Evening.AddMinutes(-20));
        var alice = Search("alice", Evening);

        Assert.Same(carol, alice.BestOpponentAmong([bob, carol], Noon));
    }

    [Fact]
    public void Players_more_than_one_hour_apart_are_not_matched()
    {
        var bob = Search("bob", Evening.AddMinutes(61));

        Assert.Null(Search("alice", Evening).BestOpponentAmong([bob], Noon));
    }

    [Fact]
    public void Exactly_one_hour_apart_still_matches()
    {
        var bob = Search("bob", Evening.AddHours(-1));

        Assert.Same(bob, Search("alice", Evening).BestOpponentAmong([bob], Noon));
    }

    [Fact]
    public void Format_and_ranked_mode_must_be_the_same()
    {
        var incursion = Search("bob", Evening, new GameFormatChoice(Incursion, true));
        var friendly = Search("carol", Evening, new GameFormatChoice(StrikeForce, false));

        Assert.Null(Search("alice", Evening).BestOpponentAmong([incursion, friendly], Noon));
    }

    [Fact]
    public void A_player_does_not_face_their_own_search()
    {
        var other = Search("alice", Evening.AddMinutes(10));

        Assert.Null(Search("alice", Evening).BestOpponentAmong([other], Noon));
    }

    [Fact]
    public void At_equal_time_the_closest_rating_wins()
    {
        var far = Search("bob", Evening.AddMinutes(30), rating: 1800);
        var close = Search("carol", Evening.AddMinutes(-30), rating: 1550);

        Assert.Same(close, Search("alice", Evening).BestOpponentAmong([far, close], Noon));
    }

    [Fact]
    public void Matching_proposes_a_game_at_the_time_chosen_by_the_player_who_waited()
    {
        var bob = Search("bob", Evening.AddMinutes(30));
        var alice = Search("alice", Evening);

        var match = alice.MatchWith(bob, Noon.AddHours(1));

        Assert.Equal(Evening.AddMinutes(30), match.ScheduledFor);
        Assert.Equal(MatchStatus.Proposed, match.Status);
        Assert.Equal(bob.Ticket.Player, match.First.Player);
        Assert.Equal(alice.Ticket.Player, match.Second.Player);
        Assert.All([alice, bob], s => Assert.Equal(ScheduledSearchStatus.Matched, s.Status));
        Assert.All([alice, bob], s => Assert.Equal(match.Id, s.MatchId));
    }

    [Fact]
    public void A_matched_search_is_no_longer_a_candidate()
    {
        var bob = Search("bob", Evening);
        Search("carol", Evening).MatchWith(bob, Noon);

        Assert.Null(Search("alice", Evening).BestOpponentAmong([bob], Noon));
    }

    [Fact]
    public void A_search_too_close_to_its_time_is_no_longer_a_candidate()
    {
        var bob = Search("bob", Noon.AddHours(2));

        Assert.Null(Search("alice", Noon.AddHours(2)).BestOpponentAmong([bob], Noon.AddMinutes(61)));
    }

    [Fact]
    public void A_search_is_reopened_when_the_proposed_game_falls_through()
    {
        var bob = Search("bob", Evening);
        Search("alice", Evening).MatchWith(bob, Noon);

        bob.Reopen(Noon.AddHours(3));

        Assert.Equal(ScheduledSearchStatus.Open, bob.Status);
        Assert.Null(bob.MatchId);
    }

    [Fact]
    public void A_search_reopened_too_late_expires()
    {
        var bob = Search("bob", Noon.AddHours(5));
        Search("alice", Noon.AddHours(5)).MatchWith(bob, Noon);

        bob.Reopen(Noon.AddHours(4).AddMinutes(1));

        Assert.Equal(ScheduledSearchStatus.Expired, bob.Status);
    }

    [Fact]
    public void An_open_search_can_be_cancelled()
    {
        var search = Search("alice", Evening);

        search.Cancel();

        Assert.Equal(ScheduledSearchStatus.Cancelled, search.Status);
    }

    [Fact]
    public void A_search_waiting_for_an_answer_cannot_be_cancelled()
    {
        var bob = Search("bob", Evening);
        Search("alice", Evening).MatchWith(bob, Noon);

        Assert.Throws<InvalidOperationException>(bob.Cancel);
    }

    [Fact]
    public void An_open_search_expires_one_hour_before_its_time()
    {
        var search = Search("alice", Noon.AddHours(5));

        Assert.False(search.ExpireIfTooLate(Noon.AddHours(4)));
        Assert.True(search.ExpireIfTooLate(Noon.AddHours(4).AddSeconds(1)));
        Assert.Equal(ScheduledSearchStatus.Expired, search.Status);
    }
}
