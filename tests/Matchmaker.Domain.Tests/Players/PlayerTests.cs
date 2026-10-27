using Matchmaker.Domain.Players;

namespace Matchmaker.Domain.Tests.Players;

public class PlayerTests
{
    [Fact]
    public void The_steam_id_is_read_from_the_steam_openid_identifier()
    {
        var id = PlayerId.FromSteamOpenId("https://steamcommunity.com/openid/id/76561197960435530");

        Assert.Equal("76561197960435530", id.SteamId);
    }

    [Theory]
    [InlineData("https://example.com/openid/id/76561197960435530")]
    [InlineData("https://steamcommunity.com/openid/id/")]
    [InlineData("https://steamcommunity.com/openid/id/not-a-number")]
    [InlineData("")]
    public void Anything_else_than_a_steam_openid_identifier_is_refused(string claim)
    {
        Assert.Throws<ArgumentException>(() => PlayerId.FromSteamOpenId(claim));
    }

    [Fact]
    public void A_new_player_is_named_after_their_steam_id_until_the_profile_is_known()
    {
        var player = new Player(new PlayerId("76561197960435530"));

        Assert.Equal("76561197960435530", player.DisplayName);
        Assert.Null(player.AvatarUrl);
    }

    [Fact]
    public void The_profile_is_refreshed_from_steam()
    {
        var player = new Player(new PlayerId("76561197960435530"));

        player.UpdateProfile("Ouarzy", "https://avatars.steamstatic.com/abc_full.jpg");

        Assert.Equal("Ouarzy", player.DisplayName);
        Assert.Equal("https://avatars.steamstatic.com/abc_full.jpg", player.AvatarUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_display_name_is_refused(string displayName)
    {
        var player = new Player(new PlayerId("76561197960435530"));

        Assert.Throws<ArgumentException>(() => player.UpdateProfile(displayName, null));
    }
}
