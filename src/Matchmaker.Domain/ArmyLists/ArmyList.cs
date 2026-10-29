using System.Globalization;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.ArmyLists;

/// <summary>
/// Liste d'armée d'un joueur. Le format est déclaré par le joueur :
/// les données importées ne contiennent pas les points.
/// </summary>
public sealed class ArmyList
{
    public const int NameMaxLength = 512;
    public const int DetachmentMaxLength = 128;

    private static readonly StringComparer AlphabeticalOrder = StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true);

    private ArmyList(Guid id, PlayerId owner, string name, GameFormat format, Roster roster, DateTimeOffset importedAt)
    {
        Id = id;
        Owner = owner;
        Name = name;
        Format = format;
        Roster = roster;
        ImportedAt = importedAt;
    }

    // Pour EF Core, qui renseigne les propriétés lui-même.
    private ArmyList()
    {
        Name = null!;
        Format = null!;
        Roster = null!;
    }

    public Guid Id { get; }
    public PlayerId Owner { get; }
    public string Name { get; private set; }
    /// <summary>Saisi par le joueur : YellowScribe ne transmet pas le détachement.</summary>
    public string? Detachment { get; private set; }
    /// <summary>Déclaré par le joueur : liste préparée pour un tournoi par équipes.</summary>
    public bool IsTeamList { get; private set; }
    /// <summary>Choisie par défaut pour une nouvelle partie. Une seule par joueur : voir ArmyListService.</summary>
    public bool IsFavorite { get; private set; }
    public GameFormat Format { get; }
    public Roster Roster { get; }
    public DateTimeOffset ImportedAt { get; }
    public string? Faction => Roster.Faction;

    /// <param name="name">Facultatif : YellowScribe ne transmet pas le nom de la liste.
    /// Par défaut, la liste porte le nom de sa faction et de son format.</param>
    public static ArmyList Import(PlayerId owner, string? name, GameFormat format, Roster roster, DateTimeOffset now, string? detachment = null,
        bool isTeamList = false)
    {
        if (roster.Units.Count == 0)
            throw new ArgumentException("La liste importée ne contient aucune unité.", nameof(roster));

        var chosenName = string.IsNullOrWhiteSpace(name) ? $"{roster.Faction ?? "Liste"} – {format.Name}" : name;
        return new ArmyList(Guid.NewGuid(), owner, ValidName(chosenName), format, roster, now) { Detachment = ValidDetachment(detachment), IsTeamList = isTeamList };
    }

    public void Rename(string name) => Name = ValidName(name);

    /// <param name="detachment">Vide pour retirer le détachement.</param>
    public void ChangeDetachment(string? detachment) => Detachment = ValidDetachment(detachment);

    public void ChangeTeamList(bool isTeamList) => IsTeamList = isTeamList;

    public void MarkAsFavorite() => IsFavorite = true;

    public void UnmarkAsFavorite() => IsFavorite = false;

    /// <summary>Ordre proposé pour choisir une liste : la favorite, puis les autres par ordre alphabétique.</summary>
    public static IReadOnlyList<ArmyList> FavoriteFirstThenByName(IEnumerable<ArmyList> lists) =>
        [.. lists.OrderByDescending(l => l.IsFavorite).ThenBy(l => l.Name, AlphabeticalOrder)];

    public bool IsCompatibleWith(GameFormat format) => Format == format;

    public bool IsOwnedBy(PlayerId player) => Owner == player;

    /// <summary>Fige la liste telle qu'elle est jouée dans ce match.</summary>
    public ArchivedArmyList ArchiveFor(Guid matchId, DateTimeOffset now) => new(matchId, this, now);

    private static string? ValidDetachment(string? detachment)
    {
        if (string.IsNullOrWhiteSpace(detachment)) return null;
        var trimmed = detachment.Trim();
        if (trimmed.Length > DetachmentMaxLength)
            throw new ArgumentException($"Le nom d'un détachement est limité à {DetachmentMaxLength} caractères.", nameof(detachment));
        return trimmed;
    }

    private static string ValidName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Le nom d'une liste est limité à {NameMaxLength} caractères.", nameof(name));
        return trimmed;
    }
}
