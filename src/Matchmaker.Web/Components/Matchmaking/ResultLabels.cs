using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Results;

namespace Matchmaker.Web.Components.Matchmaking;

public static class ResultLabels
{
    public static string Of(ResultCategory category) => category switch
    {
        ResultCategory.CrushingVictory => "Victoire écrasante",
        ResultCategory.Victory => "Victoire",
        ResultCategory.MinorVictory => "Petite victoire",
        ResultCategory.Draw => "Nul",
        ResultCategory.MinorDefeat => "Petite défaite",
        ResultCategory.Defeat => "Défaite",
        ResultCategory.HeavyDefeat => "Grosse défaite",
        _ => category.ToString(),
    };

    public static string Of(MatchStatus status) => status switch
    {
        MatchStatus.Completed => "Confirmé",
        MatchStatus.Abandoned => "Abandonné",
        MatchStatus.ResultDeclared => "En attente de confirmation",
        MatchStatus.Accepted => "En cours",
        _ => status.ToString(),
    };

    public static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();
}
