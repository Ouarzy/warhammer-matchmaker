using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Web.Components.Matchmaking;

/// <summary>Lien vers une liste d'armée, vivante ou archivée avec un match.</summary>
public sealed record ListLink(string Name, string? Faction, string? Detachment, string Url)
{
    public static ListLink? To(ArmyList? list) =>
        list is null ? null : new(list.Name, list.Faction, list.Detachment, $"/lists/{list.Id}");

    public static ListLink? To(ArchivedArmyList? archive) =>
        archive is null ? null : new(archive.Name, archive.Faction, archive.Detachment, $"/archived-lists/{archive.Id}");
}
