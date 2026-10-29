namespace Matchmaker.Domain.ArmyLists;

/// <summary>Usage prévu de la liste, déclaré par le joueur.</summary>
public enum ArmyListType
{
    /// <summary>Par défaut : jouable en solo comme en équipe.</summary>
    Hybrid,
    Team,
    Solo,
}
