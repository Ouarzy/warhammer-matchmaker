using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using static Matchmaker.Domain.Tests.Tickets;

namespace Matchmaker.Domain.Tests.Matchmaking;

public class MatchmakingQueueTests
{
    private readonly MatchmakingQueue _queue = new();

    [Fact]
    public void A_lone_player_waits_in_the_queue()
    {
        var match = _queue.Enqueue(For("alice"), Noon);

        Assert.Null(match);
        Assert.True(_queue.Contains(For("alice").Player));
    }

    [Fact]
    public void Two_compatible_players_are_proposed_a_match()
    {
        var alice = For("alice");
        var bob = For("bob");
        _queue.Enqueue(alice, Noon);

        var match = _queue.Enqueue(bob, Noon);

        Assert.NotNull(match);
        Assert.Equal(MatchStatus.Proposed, match.Status);
        Assert.Equal([alice, bob], [match.First, match.Second]);
        Assert.False(_queue.Contains(alice.Player));
        Assert.False(_queue.Contains(bob.Player));
    }

    [Fact]
    public void Players_looking_for_different_formats_are_not_paired()
    {
        _queue.Enqueue(For("alice", format: StrikeForce), Noon);

        Assert.Null(_queue.Enqueue(For("bob", format: Incursion), Noon));
    }

    [Fact]
    public void Ranked_and_unranked_players_are_not_paired()
    {
        _queue.Enqueue(For("alice", ranked: true), Noon);

        Assert.Null(_queue.Enqueue(For("bob", ranked: false), Noon));
    }

    [Fact]
    public void The_opponent_with_the_closest_rating_is_preferred()
    {
        // Écart max choisi pour que "far" et "close" ne s'apparient pas entre eux.
        var queue = new MatchmakingQueue(maxRatingGap: 400);
        queue.Enqueue(For("far", rating: 1100), Noon);
        queue.Enqueue(For("close", rating: 1550), Noon);

        var match = queue.Enqueue(For("alice", rating: 1500), Noon);

        Assert.Equal("close", match!.First.Player.SteamId);
    }

    [Fact]
    public void Among_equally_close_opponents_the_one_waiting_longest_is_preferred()
    {
        // Écart max choisi pour que "recent" et "oldest" ne s'apparient pas entre eux.
        var queue = new MatchmakingQueue(maxRatingGap: 300);
        queue.Enqueue(For("recent", rating: 1700, enqueuedAt: Noon.AddMinutes(2)), Noon);
        queue.Enqueue(For("oldest", rating: 1300, enqueuedAt: Noon), Noon);

        var match = queue.Enqueue(For("alice", rating: 1500), Noon);

        Assert.Equal("oldest", match!.First.Player.SteamId);
    }

    [Fact]
    public void Opponents_beyond_the_maximum_rating_gap_are_not_paired()
    {
        var queue = new MatchmakingQueue(maxRatingGap: 300);
        queue.Enqueue(For("alice", rating: 1500), Noon);

        Assert.Null(queue.Enqueue(For("bob", rating: 1801), Noon));
        Assert.NotNull(queue.Enqueue(For("carol", rating: 1800), Noon));
    }

    [Fact]
    public void A_player_cannot_be_queued_twice()
    {
        _queue.Enqueue(For("alice"), Noon);

        Assert.Throws<InvalidOperationException>(() => _queue.Enqueue(For("alice", format: Incursion), Noon));
    }

    [Fact]
    public void A_player_can_leave_the_queue()
    {
        _queue.Enqueue(For("alice"), Noon);

        Assert.True(_queue.Leave(For("alice").Player));
        Assert.False(_queue.Contains(For("alice").Player));
        Assert.Null(_queue.Enqueue(For("bob"), Noon));
    }

    [Fact]
    public void Leaving_when_not_queued_does_nothing()
    {
        Assert.False(_queue.Leave(For("alice").Player));
    }

    [Fact]
    public void The_match_is_proposed_at_the_time_of_pairing()
    {
        _queue.Enqueue(For("alice"), Noon);

        var match = _queue.Enqueue(For("bob"), Noon.AddMinutes(3));

        Assert.Equal(Noon.AddMinutes(3), match!.ProposedAt);
    }
}
