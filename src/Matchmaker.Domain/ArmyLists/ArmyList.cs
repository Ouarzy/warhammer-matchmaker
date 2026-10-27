using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.ArmyLists;

/// <summary>
/// Liste d'armée d'un joueur. Le format est déclaré par le joueur :
/// les données importées ne contiennent pas les points.
/// </summary>
public sealed class ArmyList
{
    public const int NameMaxLength = 60;

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
    public GameFormat Format { get; }
    public Roster Roster { get; }
    public DateTimeOffset ImportedAt { get; }
    public string? Faction => Roster.Faction;

    public static ArmyList Import(PlayerId owner, string name, GameFormat format, Roster roster, DateTimeOffset now)
    {
        if (roster.Units.Count == 0)
            throw new ArgumentException("La liste importée ne contient aucune unité.", nameof(roster));

        return new ArmyList(Guid.NewGuid(), owner, ValidName(name), format, roster, now);
    }

    public void Rename(string name) => Name = ValidName(name);

    public bool IsCompatibleWith(GameFormat format) => Format == format;

    public bool IsOwnedBy(PlayerId player) => Owner == player;

    private static string ValidName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Le nom d'une liste est limité à {NameMaxLength} caractères.", nameof(name));
        return trimmed;
    }
}
