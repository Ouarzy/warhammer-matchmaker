using System.Net;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Matchmaker.Web.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class DevelopmentLoginTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    [Fact]
    public async Task In_development_a_test_player_with_a_sample_list_can_sign_in()
    {
        var name = $"Bot{Random.Shared.Next(100000)}";

        var response = await _factory.CreateAnonymousClient().GetAsync($"/dev/login?name={name}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/play", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Cookies"));

        using var scope = _factory.Services.CreateScope();
        var id = new PlayerId($"dev-{name}");
        Assert.Equal(name, (await scope.ServiceProvider.GetRequiredService<PlayerAccounts>().FindAsync(id))!.DisplayName);
        var list = Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(id));
        Assert.Equal("Adeptus Custodes", list.Faction);
    }

    [Fact]
    public async Task Signing_in_again_does_not_duplicate_the_sample_list()
    {
        var name = $"Bot{Random.Shared.Next(100000)}";
        var client = _factory.CreateAnonymousClient();

        await client.GetAsync($"/dev/login?name={name}");
        await client.GetAsync($"/dev/login?name={name}");

        using var scope = _factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<ArmyListService>().ListOwnedByAsync(new PlayerId($"dev-{name}")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a-b")]
    [InlineData("NomBeaucoupTropLong")]
    public async Task The_test_player_name_is_short_and_alphanumeric(string name)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync($"/dev/login?name={name}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Outside_development_the_test_login_does_not_exist()
    {
        using var production = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var response = await production.CreateClient().GetAsync("/dev/login?name=Bot");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
