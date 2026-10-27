namespace Matchmaker.Domain.ArmyLists;

/// <summary>Contenu d'une liste d'armée, tel qu'importé depuis un outil de liste.</summary>
public sealed record Roster(string Edition, IReadOnlyList<RosterUnit> Units)
{
    /// <summary>Le mot-clé de faction le plus représenté (les alliés sont minoritaires), ou null.</summary>
    public string? Faction => Units
        .SelectMany(unit => unit.FactionKeywords)
        .GroupBy(faction => faction)
        .OrderByDescending(group => group.Count())
        .Select(group => group.Key)
        .FirstOrDefault();
}

public sealed record RosterUnit(
    string Name,
    IReadOnlyList<string> FactionKeywords,
    IReadOnlyList<string> Keywords,
    int ModelCount,
    IReadOnlyList<ModelProfile> Profiles,
    IReadOnlyList<ModelGroup> Models,
    IReadOnlyList<Weapon> Weapons,
    IReadOnlyList<Ability> Abilities)
{
    /// <summary>
    /// Un profil par jeu de caractéristiques. YellowScribe envoie souvent un profil au nom de l'unité en plus de
    /// celui de la figurine, avec les mêmes caractéristiques : on garde le nom de la figurine.
    /// </summary>
    public IReadOnlyList<ModelProfile> DistinctProfiles =>
    [
        .. Profiles
            .GroupBy(p => (p.M, p.T, p.Sv, p.W, p.Ld, p.Oc, p.InvulnerableSave))
            .Select(same => same.FirstOrDefault(p => Models.Any(m => m.Name == p.Name)) ?? same.First()),
    ];
}

public sealed record ModelProfile(string Name, string M, string T, string Sv, string W, string Ld, string Oc, string? InvulnerableSave);

/// <summary>Un groupe de figurines identiques au sein d'une unité, avec leur équipement.</summary>
public sealed record ModelGroup(string Name, int Count, IReadOnlyList<string> Weapons);

public sealed record Weapon(string Name, string Range, string A, string Skill, string S, string Ap, string D, string Abilities)
{
    public bool IsMelee => Range.Equals("Melee", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Les aptitudes, sans les rappels vides que YellowScribe ajoute à la ligne (« Close-quarters: ») :
    /// seuls les rappels qui ont une description sont gardés.
    /// </summary>
    public string DisplayedAbilities => string.Join(" · ", Abilities
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where((line, index) => index == 0 || !line.EndsWith(':')));
}

public sealed record Ability(string Name, string Description);
