using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Players;
using static Matchmaker.Domain.Tests.Tickets;

namespace Matchmaker.Domain.Tests.ArmyLists;

public class ArchivedArmyListTests
{
    private static readonly PlayerId Owner = new("76561197960435530");
    private static readonly Guid MatchId = Guid.NewGuid();

    private static readonly Roster Custodes = new("11e",
        [new RosterUnit("Custodian Wardens", ["Adeptus Custodes"], [], 3, [], [], [], [])]);

    [Fact]
    public void An_archive_is_a_copy_of_the_list_as_played_in_a_match()
    {
        var list = ArmyList.Import(Owner, "Custodes 2000", StrikeForce, Custodes, Noon, "Shield Host", ArmyListType.Team);

        var archive = list.ArchiveFor(MatchId, Noon.AddDays(1));

        Assert.NotEqual(list.Id, archive.Id);
        Assert.Equal(MatchId, archive.MatchId);
        Assert.Equal(list.Id, archive.OriginalListId);
        Assert.Equal(Owner, archive.Owner);
        Assert.Equal("Custodes 2000", archive.Name);
        Assert.Equal("Shield Host", archive.Detachment);
        Assert.Equal(ArmyListType.Team, archive.Type);
        Assert.Equal(StrikeForce, archive.Format);
        Assert.Same(Custodes, archive.Roster);
        Assert.Equal("Adeptus Custodes", archive.Faction);
        Assert.Equal(Noon.AddDays(1), archive.ArchivedAt);
    }

    [Fact]
    public void Renaming_the_list_afterwards_does_not_change_the_archive()
    {
        var list = ArmyList.Import(Owner, "Custodes 2000", StrikeForce, Custodes, Noon);
        var archive = list.ArchiveFor(MatchId, Noon);

        list.Rename("Custodes v2");
        list.ChangeDetachment("Talons of the Emperor");

        Assert.Equal("Custodes 2000", archive.Name);
        Assert.Null(archive.Detachment);
    }
}
