using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;

namespace Matchmaker.Web.ArmyLists;

/// <summary>Tri du tableau « Mes listes », porté par l'adresse de la page.</summary>
public sealed record ArmyListOrder(string Column, bool Descending)
{
    public const string Name = "name";
    public const string Faction = "faction";
    public const string Detachment = "detachment";
    public const string Format = "format";
    public const string Type = "type";
    public const string Units = "units";
    public const string Imported = "imported";

    /// <summary>Les plus récentes d'abord.</summary>
    public static readonly ArmyListOrder Default = new(Imported, true);

    private static readonly Dictionary<string, Func<ArmyList, object?>> Keys = new()
    {
        [Name] = l => l.Name,
        [Faction] = l => l.Faction,
        [Detachment] = l => l.Detachment,
        // Par points : l'ordre alphabétique des noms de format n'aurait pas de sens pour le joueur.
        [Format] = l => GameFormats.PointsOf(l.Format),
        [Type] = l => l.Type.Label(),
        [Units] = l => l.Roster.Units.Count,
        [Imported] = l => l.ImportedAt,
    };

    private static readonly Comparer<object?> Comparison = Comparer<object?>.Create((a, b) =>
        a is string first && b is string second ? ArmyList.AlphabeticalOrder.Compare(first, second) : Comparer<object?>.Default.Compare(a, b));

    /// <summary>Une adresse inconnue retombe sur le tri par défaut : elle peut être modifiée à la main.</summary>
    public static ArmyListOrder Parse(string? column, string? direction) =>
        column is not null && Keys.ContainsKey(column) ? new(column, direction == "desc") : Default;

    /// <summary>Le tri obtenu en cliquant sur une colonne : l'inverse si elle est déjà triée.</summary>
    public ArmyListOrder Next(string column) =>
        column == Column ? this with { Descending = !Descending } : new(column, column == Imported);

    /// <summary>Les valeurs manquantes restent à la fin, quel que soit le sens.</summary>
    public IReadOnlyList<ArmyList> Apply(IEnumerable<ArmyList> lists)
    {
        var key = Keys[Column];
        var missingLast = lists.OrderBy(l => key(l) is null);
        return Descending ? [.. missingLast.ThenByDescending(key, Comparison)] : [.. missingLast.ThenBy(key, Comparison)];
    }
}
