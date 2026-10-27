using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.ArmyLists;

/// <summary>
/// Copie figée d'une liste d'armée, telle que jouée dans un match. Elle ne suit plus la liste
/// d'origine : un renommage ou une suppression ultérieurs ne changent pas l'historique.
/// </summary>
public sealed class ArchivedArmyList
{
    internal ArchivedArmyList(Guid matchId, ArmyList original, DateTimeOffset archivedAt)
    {
        Id = Guid.NewGuid();
        MatchId = matchId;
        OriginalListId = original.Id;
        Owner = original.Owner;
        Name = original.Name;
        Format = original.Format;
        Roster = original.Roster;
        ArchivedAt = archivedAt;
    }

    // Pour EF Core, qui renseigne les propriétés lui-même.
    private ArchivedArmyList()
    {
        Name = null!;
        Format = null!;
        Roster = null!;
    }

    public Guid Id { get; }
    public Guid MatchId { get; }
    public Guid OriginalListId { get; }
    public PlayerId Owner { get; }
    public string Name { get; }
    public GameFormat Format { get; }
    public Roster Roster { get; }
    public DateTimeOffset ArchivedAt { get; }
    public string? Faction => Roster.Faction;
}
