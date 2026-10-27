using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using static Matchmaker.Domain.Tests.Tickets;

namespace Matchmaker.Domain.Tests.Matches;

public class MatchTests
{
    private static readonly QueueTicket Alice = For("alice");
    private static readonly QueueTicket Bob = For("bob");
    private static readonly PlayerId Stranger = new("stranger");

    private static Match Proposed(bool ranked = true) =>
        Match.Propose(Alice with { Ranked = ranked }, Bob with { Ranked = ranked }, Noon);

    private static Match Accepted(bool ranked = true)
    {
        var match = Proposed(ranked);
        match.Accept(Alice.Player, Noon);
        match.Accept(Bob.Player, Noon);
        return match;
    }

    private static Match Declared(int aliceVp = 85, int bobVp = 70)
    {
        var match = Accepted();
        match.DeclareResult(Alice.Player, aliceVp, bobVp, Noon);
        return match;
    }

    public class Acceptance
    {
        [Fact]
        public void Players_have_5_minutes_to_accept()
        {
            Assert.Equal(Noon.AddMinutes(5), Proposed().AcceptanceDeadline);
        }

        [Fact]
        public void The_match_stays_proposed_until_both_players_accept()
        {
            var match = Proposed();

            match.Accept(Alice.Player, Noon.AddMinutes(1));

            Assert.Equal(MatchStatus.Proposed, match.Status);
            Assert.True(match.HasAccepted(Alice.Player));
            Assert.False(match.HasAccepted(Bob.Player));
        }

        [Fact]
        public void The_match_is_on_once_both_players_accept()
        {
            var match = Proposed();

            match.Accept(Alice.Player, Noon.AddMinutes(1));
            match.Accept(Bob.Player, Noon.AddMinutes(5));

            Assert.Equal(MatchStatus.Accepted, match.Status);
        }

        [Fact]
        public void Accepting_twice_is_harmless()
        {
            var match = Proposed();

            match.Accept(Alice.Player, Noon);
            match.Accept(Alice.Player, Noon);

            Assert.Equal(MatchStatus.Proposed, match.Status);
        }

        [Fact]
        public void Accepting_after_the_deadline_is_refused()
        {
            var match = Proposed();

            Assert.Throws<InvalidOperationException>(() => match.Accept(Alice.Player, Noon.AddMinutes(5).AddSeconds(1)));
        }

        [Fact]
        public void Only_participants_can_accept()
        {
            Assert.Throws<InvalidOperationException>(() => Proposed().Accept(Stranger, Noon));
        }

        [Fact]
        public void Each_player_sees_the_ticket_of_their_opponent()
        {
            var match = Proposed();

            Assert.Equal(Bob.Player, match.OpponentOf(Alice.Player).Player);
            Assert.Equal(Alice.ArmyListId, match.OpponentOf(Bob.Player).ArmyListId);
        }
    }

    public class Decline
    {
        [Fact]
        public void Declining_puts_the_other_player_back_in_the_queue()
        {
            var match = Proposed();

            var requeued = match.Decline(Alice.Player);

            Assert.Equal(MatchStatus.Declined, match.Status);
            Assert.Equal([Bob.Player], requeued.Select(t => t.Player));
        }

        [Fact]
        public void A_match_already_on_cannot_be_declined()
        {
            Assert.Throws<InvalidOperationException>(() => Accepted().Decline(Alice.Player));
        }
    }

    public class Expiry
    {
        [Fact]
        public void Only_players_who_accepted_go_back_in_the_queue()
        {
            var match = Proposed();
            match.Accept(Bob.Player, Noon.AddMinutes(2));

            var requeued = match.Expire(Noon.AddMinutes(6));

            Assert.Equal(MatchStatus.Expired, match.Status);
            Assert.Equal([Bob.Player], requeued.Select(t => t.Player));
        }

        [Fact]
        public void Nobody_goes_back_in_the_queue_when_nobody_accepted()
        {
            Assert.Empty(Proposed().Expire(Noon.AddMinutes(6)));
        }

        [Fact]
        public void A_match_cannot_expire_before_its_deadline()
        {
            Assert.Throws<InvalidOperationException>(() => Proposed().Expire(Noon.AddMinutes(4)));
        }

        [Fact]
        public void An_accepted_match_cannot_expire()
        {
            Assert.Throws<InvalidOperationException>(() => Accepted().Expire(Noon.AddHours(1)));
        }
    }

    public class Result
    {
        [Fact]
        public void A_player_declares_the_victory_points_of_both_sides()
        {
            var match = Declared(aliceVp: 85, bobVp: 70);

            Assert.Equal(MatchStatus.ResultDeclared, match.Status);
        }

        [Fact]
        public void The_result_cannot_be_declared_before_the_match_is_on()
        {
            Assert.Throws<InvalidOperationException>(() => Proposed().DeclareResult(Alice.Player, 85, 70, Noon));
        }

        [Fact]
        public void Negative_victory_points_are_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Accepted().DeclareResult(Alice.Player, -1, 70, Noon));
        }

        [Fact]
        public void The_opponent_confirms_the_result()
        {
            var match = Declared(aliceVp: 85, bobVp: 70);

            match.ConfirmResult(Bob.Player);

            Assert.Equal(MatchStatus.Completed, match.Status);
            Assert.Equal(85, match.VictoryPointsOf(Alice.Player));
            Assert.Equal(70, match.VictoryPointsOf(Bob.Player));
            Assert.Equal(12, match.ScoreOf(Alice.Player).Points);
            Assert.Equal(8, match.ScoreOf(Bob.Player).Points);
        }

        [Fact]
        public void The_declaring_player_cannot_confirm_their_own_result()
        {
            Assert.Throws<InvalidOperationException>(() => Declared().ConfirmResult(Alice.Player));
        }

        [Fact]
        public void The_opponent_can_dispute_the_result()
        {
            var match = Declared();

            match.DisputeResult(Bob.Player);

            Assert.Equal(MatchStatus.Disputed, match.Status);
            Assert.False(match.AffectsRating);
        }

        [Fact]
        public void The_declared_result_is_visible_to_the_opponent_before_confirmation()
        {
            var match = Declared(aliceVp: 85, bobVp: 70);

            Assert.Equal(Alice.Player, match.Declarer);
            Assert.Equal(85, match.VictoryPointsOf(Alice.Player));
            Assert.Equal(70, match.VictoryPointsOf(Bob.Player));
        }

        [Fact]
        public void A_disputed_result_stays_readable()
        {
            var match = Declared(aliceVp: 85, bobVp: 70);
            match.DisputeResult(Bob.Player);

            Assert.Equal(70, match.VictoryPointsOf(Bob.Player));
        }

        [Fact]
        public void Victory_points_are_unknown_before_the_declaration()
        {
            var match = Accepted();

            Assert.Null(match.Declarer);
            Assert.Throws<InvalidOperationException>(() => match.VictoryPointsOf(Alice.Player));
        }

        [Fact]
        public void The_score_is_unknown_until_the_result_is_confirmed()
        {
            Assert.Throws<InvalidOperationException>(() => Declared().ScoreOf(Alice.Player));
        }
    }

    public class Abandon
    {
        [Fact]
        public void Abandoning_gives_a_20_0_victory_to_the_opponent()
        {
            var match = Accepted();

            match.Abandon(Bob.Player);

            Assert.Equal(MatchStatus.Abandoned, match.Status);
            Assert.Equal(Bob.Player, match.AbandonedBy);
            Assert.Equal(20, match.ScoreOf(Alice.Player).Points);
            Assert.Equal(0, match.ScoreOf(Bob.Player).Points);
        }

        [Fact]
        public void An_abandoned_ranked_match_affects_rating()
        {
            var match = Accepted();

            match.Abandon(Bob.Player);

            Assert.True(match.AffectsRating);
        }

        [Fact]
        public void An_abandoned_unranked_match_does_not_affect_rating()
        {
            var match = Accepted(ranked: false);

            match.Abandon(Bob.Player);

            Assert.False(match.AffectsRating);
        }

        [Fact]
        public void Abandoning_overrides_a_result_awaiting_confirmation()
        {
            var match = Declared(aliceVp: 40, bobVp: 90);

            match.Abandon(Bob.Player);

            Assert.Equal(20, match.ScoreOf(Alice.Player).Points);
        }

        [Fact]
        public void A_completed_match_cannot_be_abandoned()
        {
            var match = Declared();
            match.ConfirmResult(Bob.Player);

            Assert.Throws<InvalidOperationException>(() => match.Abandon(Bob.Player));
        }

        [Fact]
        public void Only_participants_can_abandon()
        {
            Assert.Throws<InvalidOperationException>(() => Accepted().Abandon(Stranger));
        }
    }

    public class AutomaticConfirmation
    {
        [Fact]
        public void The_opponent_has_24_hours_to_answer_a_declared_result()
        {
            Assert.Equal(Noon.AddHours(24), Declared().ResultConfirmationDeadline);
        }

        [Fact]
        public void There_is_no_confirmation_deadline_before_the_declaration()
        {
            Assert.Null(Accepted().ResultConfirmationDeadline);
        }

        [Fact]
        public void Without_answer_after_24_hours_the_result_is_confirmed_automatically()
        {
            var match = Declared(aliceVp: 85, bobVp: 70);

            match.ConfirmAutomatically(Noon.AddHours(24).AddSeconds(1));

            Assert.Equal(MatchStatus.Completed, match.Status);
            Assert.True(match.ConfirmedAutomatically);
            Assert.Equal(12, match.ScoreOf(Alice.Player).Points);
        }

        [Fact]
        public void The_result_cannot_be_confirmed_automatically_before_24_hours()
        {
            Assert.Throws<InvalidOperationException>(() => Declared().ConfirmAutomatically(Noon.AddHours(24)));
        }

        [Fact]
        public void A_result_answered_by_the_opponent_is_not_confirmed_automatically()
        {
            var match = Declared();
            match.DisputeResult(Bob.Player);

            Assert.Throws<InvalidOperationException>(() => match.ConfirmAutomatically(Noon.AddDays(2)));
        }

        [Fact]
        public void A_result_confirmed_by_the_opponent_is_not_marked_automatic()
        {
            var match = Declared();
            match.ConfirmResult(Bob.Player);

            Assert.False(match.ConfirmedAutomatically);
        }
    }

    public class Rating
    {
        [Fact]
        public void A_completed_ranked_match_affects_rating()
        {
            var match = Declared();
            match.ConfirmResult(Bob.Player);

            Assert.True(match.AffectsRating);
        }

        [Fact]
        public void A_completed_unranked_match_does_not_affect_rating()
        {
            var match = Accepted(ranked: false);
            match.DeclareResult(Alice.Player, 85, 70, Noon);
            match.ConfirmResult(Bob.Player);

            Assert.False(match.AffectsRating);
        }
    }
}
