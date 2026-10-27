using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using static Matchmaker.Domain.Tests.Tickets;

namespace Matchmaker.Domain.Tests.Ratings;

public class MatchRatingTests
{
    private readonly Player _alice = new(new PlayerId("alice"));
    private readonly Player _bob = new(new PlayerId("bob"));

    private Match Played(int aliceVp, int bobVp, bool ranked = true, bool confirmed = true)
    {
        var match = Match.Propose(For("alice", ranked: ranked), For("bob", ranked: ranked), Noon);
        match.Accept(_alice.Id, Noon);
        match.Accept(_bob.Id, Noon);
        match.DeclareResult(_alice.Id, aliceVp, bobVp);
        if (confirmed) match.ConfirmResult(_bob.Id);
        else match.DisputeResult(_bob.Id);
        return match;
    }

    [Fact]
    public void A_new_player_starts_at_the_initial_rating_with_no_ranked_game()
    {
        Assert.Equal(EloRating.Initial, _alice.Rating);
        Assert.Equal(0, _alice.RankedGamesPlayed);
    }

    [Fact]
    public void A_completed_ranked_match_updates_both_ratings()
    {
        var applied = MatchRating.Apply(Played(aliceVp: 100, bobVp: 40), _alice, _bob);

        Assert.True(applied);
        Assert.Equal(1520, _alice.Rating.Value);
        Assert.Equal(1480, _bob.Rating.Value);
        Assert.Equal(1, _alice.RankedGamesPlayed);
        Assert.Equal(1, _bob.RankedGamesPlayed);
    }

    [Fact]
    public void Both_ratings_are_computed_from_the_ratings_before_the_match()
    {
        var match = Played(aliceVp: 100, bobVp: 40);

        MatchRating.Apply(match, _bob, _alice);

        Assert.Equal(1520, _alice.Rating.Value);
        Assert.Equal(1480, _bob.Rating.Value);
    }

    [Fact]
    public void An_unranked_match_changes_nothing()
    {
        var applied = MatchRating.Apply(Played(100, 40, ranked: false), _alice, _bob);

        Assert.False(applied);
        Assert.Equal(EloRating.Initial, _alice.Rating);
        Assert.Equal(0, _alice.RankedGamesPlayed);
    }

    [Fact]
    public void A_disputed_match_changes_nothing()
    {
        var applied = MatchRating.Apply(Played(100, 40, confirmed: false), _alice, _bob);

        Assert.False(applied);
        Assert.Equal(EloRating.Initial, _bob.Rating);
    }

    [Fact]
    public void Players_must_be_the_participants_of_the_match()
    {
        var stranger = new Player(new PlayerId("stranger"));

        Assert.Throws<InvalidOperationException>(() => MatchRating.Apply(Played(100, 40), _alice, stranger));
    }
}
