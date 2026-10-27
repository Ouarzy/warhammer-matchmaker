using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Domain.Results;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public class PlayerAccountsTests(PostgresFixture postgres)
{
    private static PlayerId NewSteamId() => new(Random.Shared.NextInt64(76561197960265728, 76561199999999999).ToString());

    [Fact]
    public async Task First_sign_in_creates_the_player_at_the_initial_rating()
    {
        var id = NewSteamId();

        await using (var db = postgres.CreateDbContext())
            await new PlayerAccounts(db).SignInAsync(id, "Ouarzy", "https://avatars.steamstatic.com/a.jpg");

        await using var reloaded = postgres.CreateDbContext();
        var player = await new PlayerAccounts(reloaded).FindAsync(id);
        Assert.NotNull(player);
        Assert.Equal("Ouarzy", player.DisplayName);
        Assert.Equal("https://avatars.steamstatic.com/a.jpg", player.AvatarUrl);
        Assert.Equal(EloRating.Initial, player.Rating);
        Assert.Equal(0, player.RankedGamesPlayed);
    }

    [Fact]
    public async Task Next_sign_ins_refresh_the_profile_and_keep_the_rating()
    {
        var id = NewSteamId();
        await using (var db = postgres.CreateDbContext())
        {
            var player = await new PlayerAccounts(db).SignInAsync(id, "Ouarzy", null);
            player.RecordRankedGame(EloRating.Initial, new WtcScore(20));
            await db.SaveChangesAsync();
        }

        await using (var db = postgres.CreateDbContext())
            await new PlayerAccounts(db).SignInAsync(id, "Ouarzy le Grand", "https://avatars.steamstatic.com/b.jpg");

        await using var reloaded = postgres.CreateDbContext();
        var refreshed = await new PlayerAccounts(reloaded).FindAsync(id);
        Assert.Equal("Ouarzy le Grand", refreshed!.DisplayName);
        Assert.Equal("https://avatars.steamstatic.com/b.jpg", refreshed.AvatarUrl);
        Assert.Equal(1520, refreshed.Rating.Value);
        Assert.Equal(1, refreshed.RankedGamesPlayed);
    }

    [Fact]
    public async Task An_unknown_player_is_not_found()
    {
        await using var db = postgres.CreateDbContext();

        Assert.Null(await new PlayerAccounts(db).FindAsync(NewSteamId()));
    }
}
