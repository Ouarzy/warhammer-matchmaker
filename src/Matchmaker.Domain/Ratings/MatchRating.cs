using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.Ratings;

public static class MatchRating
{
    /// <returns>true si le match a modifié l'Elo des joueurs (match classé et résultat confirmé).</returns>
    public static bool Apply(Match match, Player first, Player second)
    {
        if (!match.AffectsRating)
            return false;

        var firstScore = match.ScoreOf(first.Id);
        var secondScore = match.ScoreOf(second.Id);
        var (firstBefore, secondBefore) = (first.Rating, second.Rating);

        first.RecordRankedGame(secondBefore, firstScore);
        second.RecordRankedGame(firstBefore, secondScore);
        return true;
    }
}
