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

    [Fact]
    public async Task Sign_in_links_load_a_full_page_so_the_browser_can_follow_the_redirect_to_steam()
    {
        // La navigation améliorée de Blazor suivrait la redirection par fetch, bloqué par la CSP (connect-src 'self').
        var html = await Client().GetStringAsync("/");

        var loginLinks = System.Text.RegularExpressions.Regex.Matches(html, "<a [^>]*href=\"/login\"[^>]*>");
        Assert.NotEmpty(loginLinks);
        Assert.All(loginLinks, link => Assert.Contains("data-enhance-nav=\"false\"", link.Value));
    }

    public void Dispose() => _factory.Dispose();
}
