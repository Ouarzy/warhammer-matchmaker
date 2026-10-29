using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Web.ArmyLists;

public static class ArmyListTypes
{
    /// <summary>Ordre de la liste déroulante.</summary>
    public static readonly IReadOnlyList<ArmyListType> Offered = [ArmyListType.Team, ArmyListType.Solo, ArmyListType.Hybrid];

    public static string Label(this ArmyListType type) => type switch
    {
        ArmyListType.Team => "Liste d'équipe",
        ArmyListType.Solo => "Liste solo",
        _ => "Liste hybride",
    };
}
