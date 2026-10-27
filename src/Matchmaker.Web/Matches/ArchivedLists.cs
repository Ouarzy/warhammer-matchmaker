using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Players;

namespace Matchmaker.Web.Matches;

/// <summary>Listes archivées d'un ensemble de matchs, retrouvées par match et par joueur.</summary>
public sealed class ArchivedLists(IReadOnlyList<ArchivedArmyList> archives)
{
    public static readonly ArchivedLists None = new([]);

    public ArchivedArmyList? For(Guid matchId, PlayerId owner) =>
        archives.FirstOrDefault(a => a.MatchId == matchId && a.Owner == owner);
}
