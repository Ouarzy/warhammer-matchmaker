using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Matchmaking;

public class MatchmakingCoordinatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTime _time = new(Noon);
    private readonly MatchmakingCoordinator _coordinator;
    private readonly List<PlayerId> _notified = [];

    private readonly Player _alice = new(new PlayerId("alice"));
    private readonly Player _bob = new(new PlayerId("bob"));
    private readonly Player _carol = new(new PlayerId("carol"));

    public MatchmakingCoordinatorTests()
    {
        _coordinator = new MatchmakingCoordinator(_time);
        _coordinator.PlayerChanged += _notified.Add;
    }

    private static ArmyList ListOf(Player player, GameFormat? format = null) =>
        ArmyList.Import(player.Id, null, format ?? GameFormats.StrikeForce, Samples.Custodes, Noon);

    private Guid ProposeAliceVersusBob()
    {
        _coordinator.Search(_alice, ListOf(_alice), ranked: true);
        _coordinator.Search(_bob, ListOf(_bob), ranked: true);
        return _coordinator.StatusOf(_alice.Id).Match!.Id;
    }

    [Fact]
    public void A_player_is_idle_by_default()
    {
        Assert.Equal(PlayerActivity.Idle, _coordinator.StatusOf(_alice.Id).Activity);
    }

    [Fact]
    public void Searching_queues_the_player_with_the_format_of_their_list_and_their_rating()
    {
        var list = ListOf(_alice, GameFormats.Incursion);

        _coordinator.Search(_alice, list, ranked: false);

        var status = _coordinator.StatusOf(_alice.Id);
        Assert.Equal(PlayerActivity.Searching, status.Activity);
        Assert.Equal(list.Id, status.Search!.ArmyListId);
        Assert.Equal(GameFormats.Incursion, status.Search.Format);
        Assert.False(status.Search.Ranked);
        Assert.Equal(_alice.Rating, status.Search.Rating);
        Assert.Equal([_alice.Id], _notified);
    }

    [Fact]
    public void Two_compatible_players_are_both_offered_the_match()
    {
        var matchId = ProposeAliceVersusBob();

        var alice = _coordinator.StatusOf(_alice.Id);
        var bob = _coordinator.StatusOf(_bob.Id);
        Assert.Equal(PlayerActivity.MatchProposed, alice.Activity);
        Assert.Equal(PlayerActivity.MatchProposed, bob.Activity);
        Assert.Equal(matchId, bob.Match!.Id);
        Assert.Equal(_bob.Id, alice.Match!.Opponent.Player);
        Assert.Equal(Noon.AddMinutes(5), alice.Match.AcceptanceDeadline);
        Assert.Contains(_bob.Id, _notified);
        Assert.Equal(2, _notified.Count(p => p == _alice.Id));
    }

    [Fact]
    public void A_player_cannot_search_twice()
    {
        _coordinator.Search(_alice, ListOf(_alice), ranked: true);

        Assert.Throws<InvalidOperationException>(() => _coordinator.Search(_alice, ListOf(_alice), ranked: true));
    }

    [Fact]
    public void A_player_cannot_search_while_a_match_is_proposed_to_them()
    {
        ProposeAliceVersusBob();

        Assert.Throws<InvalidOperationException>(() => _coordinator.Search(_alice, ListOf(_alice), ranked: true));
    }

    [Fact]
    public void A_player_cannot_search_with_someone_else_s_list()
    {
        Assert.Throws<InvalidOperationException>(() => _coordinator.Search(_alice, ListOf(_bob), ranked: true));
    }

    [Fact]
    public void A_player_can_stop_searching()
    {
        _coordinator.Search(_alice, ListOf(_alice), ranked: true);

        _coordinator.CancelSearch(_alice.Id);

        Assert.Equal(PlayerActivity.Idle, _coordinator.StatusOf(_alice.Id).Activity);
    }

    [Fact]
    public void Each_player_sees_who_accepted()
    {
        var matchId = ProposeAliceVersusBob();

        _coordinator.Accept(_alice.Id, matchId);

        Assert.True(_coordinator.StatusOf(_alice.Id).Match!.IAccepted);
        Assert.True(_coordinator.StatusOf(_bob.Id).Match!.OpponentAccepted);
        Assert.False(_coordinator.StatusOf(_bob.Id).Match!.IAccepted);
    }

    [Fact]
    public void Once_both_accept_the_players_are_in_the_match()
    {
        var matchId = ProposeAliceVersusBob();

        _coordinator.Accept(_alice.Id, matchId);
        _coordinator.Accept(_bob.Id, matchId);

        Assert.Equal(PlayerActivity.InMatch, _coordinator.StatusOf(_alice.Id).Activity);
        Assert.Equal(PlayerActivity.InMatch, _coordinator.StatusOf(_bob.Id).Activity);
    }

    [Fact]
    public void Declining_frees_the_decliner_and_puts_the_opponent_back_in_the_queue()
    {
        var matchId = ProposeAliceVersusBob();

        _coordinator.Decline(_alice.Id, matchId);

        Assert.Equal(PlayerActivity.Idle, _coordinator.StatusOf(_alice.Id).Activity);
        Assert.Equal(PlayerActivity.Searching, _coordinator.StatusOf(_bob.Id).Activity);
    }

    [Fact]
    public void A_player_put_back_in_the_queue_is_matched_again_right_away_if_possible()
    {
        var matchId = ProposeAliceVersusBob();
        _coordinator.Search(_carol, ListOf(_carol), ranked: true);

        _coordinator.Decline(_alice.Id, matchId);

        Assert.Equal(_carol.Id, _coordinator.StatusOf(_bob.Id).Match!.Opponent.Player);
    }

    [Fact]
    public void Overdue_proposals_expire_and_only_players_who_accepted_keep_searching()
    {
        var matchId = ProposeAliceVersusBob();
        _coordinator.Accept(_bob.Id, matchId);
        _time.Now = Noon.AddMinutes(5).AddSeconds(1);

        _coordinator.ExpireOverdueMatches();

        Assert.Equal(PlayerActivity.Idle, _coordinator.StatusOf(_alice.Id).Activity);
        Assert.Equal(PlayerActivity.Searching, _coordinator.StatusOf(_bob.Id).Activity);
    }

    [Fact]
    public void Proposals_within_their_deadline_do_not_expire()
    {
        ProposeAliceVersusBob();
        _time.Now = Noon.AddMinutes(5);

        _coordinator.ExpireOverdueMatches();

        Assert.Equal(PlayerActivity.MatchProposed, _coordinator.StatusOf(_alice.Id).Activity);
    }

    [Fact]
    public void Abandoning_a_match_frees_both_players()
    {
        var matchId = ProposeAliceVersusBob();
        _coordinator.Accept(_alice.Id, matchId);
        _coordinator.Accept(_bob.Id, matchId);

        _coordinator.Abandon(_bob.Id, matchId);

        Assert.Equal(PlayerActivity.Idle, _coordinator.StatusOf(_alice.Id).Activity);
        Assert.Equal(PlayerActivity.Idle, _coordinator.StatusOf(_bob.Id).Activity);
    }

    [Fact]
    public void A_player_cannot_act_on_a_match_that_is_not_theirs()
    {
        var matchId = ProposeAliceVersusBob();

        Assert.Throws<InvalidOperationException>(() => _coordinator.Accept(_carol.Id, matchId));
    }
}
