using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Domain.Tests.ArmyLists;

public class RosterUnitTests
{
    private static ModelProfile Profile(string name, string toughness = "7", string wounds = "6") =>
        new(name, "8\"", toughness, "2+", wounds, "5+", "3", "4+");

    private static RosterUnit Unit(IReadOnlyList<ModelProfile> profiles, params string[] modelNames) =>
        new("Custodian Wardens", ["Adeptus Custodes"], [], 3, profiles,
            [.. modelNames.Select(name => new ModelGroup(name, 1, []))], [], []);

    [Fact]
    public void Identical_profiles_are_shown_once_under_the_model_name()
    {
        var unit = Unit([Profile("Custodian Wardens"), Profile("Custodian Warden")], "Custodian Warden");

        var profile = Assert.Single(unit.DistinctProfiles);
        Assert.Equal("Custodian Warden", profile.Name);
    }

    [Fact]
    public void Without_matching_model_the_first_identical_profile_is_kept()
    {
        var unit = Unit([Profile("Prosecutor Squad"), Profile("Prosecutor Brother")], "Somebody else");

        Assert.Equal("Prosecutor Squad", Assert.Single(unit.DistinctProfiles).Name);
    }

    [Fact]
    public void Different_profiles_are_all_kept_in_order()
    {
        var unit = Unit(
            [Profile("Shield-Captain", wounds: "7"), Profile("Custodian Wardens"), Profile("Custodian Warden")],
            "Shield-Captain", "Custodian Warden");

        Assert.Equal(["Shield-Captain", "Custodian Warden"], unit.DistinctProfiles.Select(p => p.Name));
    }
}
