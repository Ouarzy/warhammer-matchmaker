using System.Text.Json;
using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Web.ArmyLists.YellowScribe;

/// <summary>
/// Lit le JSON renvoyé par YellowScribe (get_army_by_id). Format non documenté, déduit du code source
/// de YellowScribe et d'une liste réelle : voir PLAN.md, itération 0.
/// </summary>
public static class YellowScribeRosterParser
{
    public static Roster Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var armyData = root.GetProperty("armyData");

            var units = root.GetProperty("order")
                .EnumerateArray()
                .Select(uuid => ParseUnit(armyData.GetProperty(uuid.GetString()!)))
                .ToList();

            return new Roster(root.GetProperty("edition").GetString()!, units);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new RosterImportException(RosterImportError.UnreadableContent, "Le contenu de la liste YellowScribe est illisible.", e);
        }
    }

    private static RosterUnit ParseUnit(JsonElement unit)
    {
        var models = unit.GetProperty("models");

        return new RosterUnit(
            Name: unit.GetProperty("name").GetString()!,
            FactionKeywords: Strings(unit.GetProperty("factionKeywords")),
            Keywords: Strings(unit.GetProperty("keywords")),
            ModelCount: models.GetProperty("totalNumberOfModels").GetInt32(),
            Profiles: [.. Values(unit.GetProperty("modelProfiles")).Select(ParseProfile)],
            Models: [.. Values(models.GetProperty("models")).Select(ParseModelGroup)],
            Weapons: [.. Values(unit.GetProperty("weapons")).Select(ParseWeapon)],
            Abilities: [.. Values(unit.GetProperty("abilities")).Select(ParseAbility)]);
    }

    private static ModelProfile ParseProfile(JsonElement profile) => new(
        Text(profile, "name"), Text(profile, "m"), Text(profile, "t"), Text(profile, "sv"),
        Text(profile, "w"), Text(profile, "ld"), Text(profile, "oc"),
        profile.TryGetProperty("insv", out var insv) && !string.IsNullOrWhiteSpace(insv.GetString()) ? insv.GetString() : null);

    private static ModelGroup ParseModelGroup(JsonElement model) => new(
        Text(model, "name"),
        model.GetProperty("number").GetInt32(),
        [.. model.GetProperty("weapons").EnumerateArray().Select(WeaponLoadout)]);

    private static string WeaponLoadout(JsonElement weapon)
    {
        var count = weapon.GetProperty("number").GetInt32();
        var name = Text(weapon, "name");
        return count > 1 ? $"{count}x {name}" : name;
    }

    private static Weapon ParseWeapon(JsonElement weapon) => new(
        Text(weapon, "name"), Text(weapon, "range"), Text(weapon, "a"), Text(weapon, "bsws"),
        Text(weapon, "s"), Text(weapon, "ap"), Text(weapon, "d"), Text(weapon, "abilities"));

    private static Ability ParseAbility(JsonElement ability) => new(Text(ability, "name"), Text(ability, "desc"));

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "" : "";

    private static IReadOnlyList<string> Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(item => item.GetString()!)];

    private static IEnumerable<JsonElement> Values(JsonElement map) =>
        map.EnumerateObject().Select(property => property.Value);
}
