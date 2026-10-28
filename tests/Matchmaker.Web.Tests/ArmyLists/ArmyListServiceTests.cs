using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Data;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.ArmyLists;

[Collection(PostgresCollection.Name)]
public class ArmyListServiceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Roster Custodes = Samples.Custodes;

    private readonly FixedTime _time = new(Noon);

    private ArmyListService Service(MatchmakerDbContext db) => new(db, new StubRosterProvider(), new ImportLimiter(_time), _time);

    private async Task<PlayerId> NewPlayer()
    {
        var id = new PlayerId(Random.Shared.NextInt64(76561197960265728, 76561199999999999).ToString());
        await using var db = postgres.CreateDbContext();
        await new PlayerAccounts(db).SignInAsync(id, "Joueur", null);
        return id;
    }

    [Fact]
    public async Task An_imported_list_is_stored_with_its_full_content()
    {
        var owner = await NewPlayer();
        Guid id;
        await using (var db = postgres.CreateDbContext())
            id = (await Service(db).ImportAsync(owner, "0c6cfa93", "Custodes 2000", GameFormats.StrikeForce)).Id;

        await using var reloaded = postgres.CreateDbContext();
        var list = await Service(reloaded).FindAsync(id);

        Assert.NotNull(list);
        Assert.Equal(owner, list.Owner);
        Assert.Equal("Custodes 2000", list.Name);
        Assert.Equal(GameFormats.StrikeForce, list.Format);
        Assert.Equal(Noon, list.ImportedAt);
        Assert.Equal("Adeptus Custodes", list.Faction);
        Assert.Equal(Custodes.Units.Select(u => u.Name), list.Roster.Units.Select(u => u.Name));
        Assert.Equal(Custodes.Units[4].Weapons, list.Roster.Units[4].Weapons);
        Assert.Equal(Custodes.Units[0].Abilities, list.Roster.Units[0].Abilities);
    }

    [Fact]
    public async Task A_player_cannot_keep_more_than_50_lists()
    {
        var owner = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        for (var i = 0; i < ArmyListService.MaxListsPerPlayer; i++)
            db.ArmyLists.Add(ArmyList.Import(owner, $"Liste {i}", GameFormats.StrikeForce, Custodes, Noon));
        await db.SaveChangesAsync();

        var refused = await Assert.ThrowsAsync<RosterImportException>(() =>
            Service(db).ImportAsync(owner, Samples.CustodesCode, "Une de trop", GameFormats.StrikeForce));

        Assert.Equal(RosterImportError.LimitReached, refused.Error);
    }

    [Fact]
    public async Task Imports_are_limited_to_10_every_10_minutes()
    {
        var owner = await NewPlayer();
        var limiter = new ImportLimiter(_time);
        await using var db = postgres.CreateDbContext();
        var service = new ArmyListService(db, new StubRosterProvider(), limiter, _time);
        for (var i = 0; i < ImportLimiter.MaxImports; i++)
            await service.ImportAsync(owner, Samples.CustodesCode, $"Liste {i}", GameFormats.StrikeForce);

        var refused = await Assert.ThrowsAsync<RosterImportException>(() =>
            service.ImportAsync(owner, Samples.CustodesCode, "Une de trop", GameFormats.StrikeForce));
        Assert.Equal(RosterImportError.LimitReached, refused.Error);

        _time.Now = Noon + ImportLimiter.Window;
        await service.ImportAsync(owner, Samples.CustodesCode, "Plus tard", GameFormats.StrikeForce);
        Assert.True(limiter.TryAcquire(await NewPlayer()));
    }

    [Fact]
    public async Task An_import_failure_stores_nothing()
    {
        var owner = await NewPlayer();
        await using var db = postgres.CreateDbContext();

        await Assert.ThrowsAsync<RosterImportException>(() =>
            Service(db).ImportAsync(owner, "deadbeef", "Perdue", GameFormats.StrikeForce));

        Assert.Empty(await Service(db).ListOwnedByAsync(owner));
    }

    [Fact]
    public async Task A_player_sees_their_own_lists_most_recent_first()
    {
        var owner = await NewPlayer();
        var someoneElse = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        var service = Service(db);
        await service.ImportAsync(owner, "0c6cfa93", "Ancienne", GameFormats.StrikeForce);
        _time.Now = Noon.AddHours(1);
        await service.ImportAsync(owner, "0c6cfa93", "Récente", GameFormats.Incursion);
        await service.ImportAsync(someoneElse, "0c6cfa93", "Pas à moi", GameFormats.StrikeForce);

        var lists = await service.ListOwnedByAsync(owner);

        Assert.Equal(["Récente", "Ancienne"], lists.Select(l => l.Name));
    }

    [Fact]
    public async Task The_owner_can_rename_a_list()
    {
        var owner = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        var list = await Service(db).ImportAsync(owner, "0c6cfa93", "Custodes", GameFormats.StrikeForce);

        Assert.True(await Service(db).UpdateAsync(owner, list.Id, "Custodes tournoi", null));

        await using var reloaded = postgres.CreateDbContext();
        Assert.Equal("Custodes tournoi", (await Service(reloaded).FindAsync(list.Id))!.Name);
    }

    [Fact]
    public async Task The_owner_can_delete_a_list()
    {
        var owner = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        var list = await Service(db).ImportAsync(owner, "0c6cfa93", "Custodes", GameFormats.StrikeForce);

        Assert.True(await Service(db).DeleteAsync(owner, list.Id));

        await using var reloaded = postgres.CreateDbContext();
        Assert.Null(await Service(reloaded).FindAsync(list.Id));
    }

    [Fact]
    public async Task Another_player_can_neither_rename_nor_delete_a_list()
    {
        var owner = await NewPlayer();
        var intruder = await NewPlayer();
        await using var db = postgres.CreateDbContext();
        var list = await Service(db).ImportAsync(owner, "0c6cfa93", "Custodes", GameFormats.StrikeForce);

        Assert.False(await Service(db).UpdateAsync(intruder, list.Id, "Volée", null));
        Assert.False(await Service(db).DeleteAsync(intruder, list.Id));

        await using var reloaded = postgres.CreateDbContext();
        Assert.Equal("Custodes", (await Service(reloaded).FindAsync(list.Id))!.Name);
    }
}
