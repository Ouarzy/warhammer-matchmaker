using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.Ratings;

public static class MatchRating
{
    /// <returns>true si le match a modifié l'Elo des joueurs (match classé et résultat confirmé).</returns>
    /// <exception cref="InvalidOperationException">L'Elo a déjà été mis à jour pour ce match.</exception>
    public static bool Apply(Match match, Player first, Player second)
    {
        if (!match.AffectsRating)
            return false;
        if (match.RatingApplied)
            throw new InvalidOperationException("L'Elo a déjà été mis à jour pour ce match.");

        var firstScore = match.ScoreOf(first.Id);
        var secondScore = match.ScoreOf(second.Id);
        var (firstBefore, secondBefore) = (first.Rating, second.Rating);

        first.RecordRankedGame(secondBefore, firstScore);
        second.RecordRankedGame(firstBefore, secondScore);

        match.RecordRatingChange(first.Id, first.Rating.Value - firstBefore.Value);
        match.RecordRatingChange(second.Id, second.Rating.Value - secondBefore.Value);
        return true;
    }
}
