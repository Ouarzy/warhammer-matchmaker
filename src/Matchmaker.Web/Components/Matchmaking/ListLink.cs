using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Web.Components.Matchmaking;

/// <summary>Lien vers une liste d'armée, vivante ou archivée avec un match.</summary>
public sealed record ListLink(string Name, string? Faction, string Url)
{
    public static ListLink? To(ArmyList? list) =>
        list is null ? null : new(list.Name, list.Faction, $"/lists/{list.Id}");

    public static ListLink? To(ArchivedArmyList? archive) =>
        archive is null ? null : new(archive.Name, archive.Faction, $"/archived-lists/{archive.Id}");
}
