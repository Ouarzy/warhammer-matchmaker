using System.Net;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Matchmaker.Web.Tests.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class SteamLoginTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Login_redirects_to_steam()
    {
        var response = await Client().GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://steamcommunity.com/openid/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_profile_page_requires_to_be_signed_in()
    {
        var response = await Client().GetAsync("/profile");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task The_home_page_invites_to_sign_in_with_steam()
    {
        var html = await Client().GetStringAsync("/");

        Assert.Contains("Se connecter avec Steam", html);
    }

    public void Dispose() => _factory.Dispose();
}
