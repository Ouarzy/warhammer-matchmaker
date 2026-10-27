using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using static Matchmaker.Domain.Tests.Tickets;

namespace Matchmaker.Domain.Tests.ArmyLists;

public class ArmyListTests
{
    private static readonly PlayerId Owner = new("76561197960435530");

    private static RosterUnit Unit(string name, params string[] factions) =>
        new(name, factions, [], 1, [], [], [], []);

    private static Roster Custodes() => new("11e",
    [
        Unit("Custodian Wardens", "Adeptus Custodes"),
        Unit("Trajann Valoris", "Adeptus Custodes"),
        Unit("Inquisitor", "Agents of the Imperium"),
    ]);

    private static ArmyList Imported(string? name = "Custodes 2000") =>
        ArmyList.Import(Owner, name, StrikeForce, Custodes(), Noon);

    [Fact]
    public void An_imported_list_belongs_to_its_owner_with_its_format()
    {
        var list = Imported();

        Assert.Equal(Owner, list.Owner);
        Assert.Equal("Custodes 2000", list.Name);
        Assert.Equal(StrikeForce, list.Format);
        Assert.Equal(Noon, list.ImportedAt);
        Assert.Equal(3, list.Roster.Units.Count);
    }

    [Fact]
    public void The_faction_is_the_most_represented_faction_keyword()
    {
        Assert.Equal("Adeptus Custodes", Imported().Faction);
    }

    [Fact]
    public void A_roster_without_faction_keyword_has_an_unknown_faction()
    {
        var roster = new Roster("11e", [Unit("Mystery")]);

        Assert.Null(roster.Faction);
    }

    [Fact]
    public void The_name_is_trimmed()
    {
        Assert.Equal("Custodes", Imported("  Custodes  ").Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_name_the_list_is_named_after_its_faction_and_format(string? name)
    {
        Assert.Equal("Adeptus Custodes – Strike Force", Imported(name).Name);
    }

    [Fact]
    public void Without_a_name_nor_a_faction_the_list_is_named_after_its_format()
    {
        var list = ArmyList.Import(Owner, null, StrikeForce, new Roster("11e", [Unit("Mystery")]), Noon);

        Assert.Equal("Liste – Strike Force", list.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_list_cannot_be_renamed_with_a_blank_name(string name)
    {
        Assert.Throws<ArgumentException>(() => Imported().Rename(name));
    }

    [Fact]
    public void The_name_is_limited_to_60_characters()
    {
        Assert.Throws<ArgumentException>(() => Imported(new string('x', 61)));
    }

    [Fact]
    public void An_empty_roster_cannot_be_imported()
    {
        Assert.Throws<ArgumentException>(() =>
            ArmyList.Import(Owner, "Vide", StrikeForce, new Roster("11e", []), Noon));
    }

    [Fact]
    public void A_list_can_be_renamed()
    {
        var list = Imported();

        list.Rename("Custodes tournoi");

        Assert.Equal("Custodes tournoi", list.Name);
    }

    [Fact]
    public void A_list_is_only_compatible_with_its_own_format()
    {
        var list = Imported();

        Assert.True(list.IsCompatibleWith(StrikeForce));
        Assert.False(list.IsCompatibleWith(Incursion));
    }

    [Fact]
    public void Only_its_owner_can_use_a_list()
    {
        var list = Imported();

        Assert.True(list.IsOwnedBy(Owner));
        Assert.False(list.IsOwnedBy(new PlayerId("76561197960435531")));
    }
}
