using Matchmaker.Domain.ArmyLists;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.ArmyLists.YellowScribe;

namespace Matchmaker.Web.Tests.ArmyLists;

public class YellowScribeRosterParserTests
{
    // Liste réelle récupérée sur YellowScribe le 2026-10-27 (Adeptus Custodes, 11e édition).
    private static readonly Roster Custodes =
        YellowScribeRosterParser.Parse(File.ReadAllText(Path.Combine("Samples", "yellowscribe-0c6cfa93.json")));

    [Fact]
    public void Reads_the_edition_and_every_unit_in_order()
    {
        Assert.Equal("11e", Custodes.Edition);
        Assert.Equal(
            ["Custodian Wardens", "Shield-Captain in Allarus Terminator Armour", "Aquilon Terminators with Solarite Power Gauntlets",
             "Venatari with Verutum Lances", "Trajann Valoris", "Aquilon Terminators with Solarite Power Talons",
             "Witchseeker Squad", "Allarus Custodians", "Prosecutor Squad"],
            Custodes.Units.Select(u => u.Name));
    }

    [Fact]
    public void Reads_the_faction_and_keywords()
    {
        var wardens = Custodes.Units[0];

        Assert.Equal("Adeptus Custodes", Custodes.Faction);
        Assert.Equal(["Adeptus Custodes"], wardens.FactionKeywords);
        Assert.Contains("Infantry", wardens.Keywords);
    }

    [Fact]
    public void Reads_the_model_count_and_profiles()
    {
        var wardens = Custodes.Units[0];

        Assert.Equal(3, wardens.ModelCount);
        Assert.Contains(new ModelProfile("Custodian Warden", "8\"", "7", "2+", "6", "5+", "3", "4+"), wardens.Profiles);
    }

    [Fact]
    public void Reads_the_weapons_with_their_profile()
    {
        var trajann = Custodes.Units.Single(u => u.Name == "Trajann Valoris");

        Assert.Equal(new Weapon("Eagle's Scream", "24\"", "2", "2+", "6", "-2", "2", "Assault, Rapid Fire 2"), trajann.Weapons[0]);
        Assert.True(trajann.Weapons.Single(w => w.Name == "Watcher's Axe").IsMelee);
    }

    [Fact]
    public void Reads_the_models_with_their_equipment()
    {
        var trajann = Custodes.Units.Single(u => u.Name == "Trajann Valoris");

        var model = Assert.Single(trajann.Models);
        Assert.Equal("Trajann Valoris", model.Name);
        Assert.Equal(1, model.Count);
        Assert.Equal(["Eagle's Scream", "Watcher's Axe"], model.Weapons);
    }

    [Fact]
    public void Reads_the_abilities_with_their_description()
    {
        Assert.Contains(new Ability("Living Fortress", "Attacks targeting this unit have -1 AP."), Custodes.Units[0].Abilities);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ \"err\": \"Roster not found!\" }")]
    public void Unreadable_content_is_reported(string json)
    {
        var error = Assert.Throws<RosterImportException>(() => YellowScribeRosterParser.Parse(json));

        Assert.Equal(RosterImportError.UnreadableContent, error.Error);
    }
}
