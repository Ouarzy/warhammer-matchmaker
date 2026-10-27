using System.Net;
using Matchmaker.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Matchmaker.Web.Tests.Hosting;

[Collection(PostgresCollection.Name)]
public sealed class ProductionSetupTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), $"matchmaker-keys-{Guid.NewGuid():N}");

    [Fact]
    public async Task The_health_endpoint_reports_a_healthy_site_and_database()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Behind_the_reverse_proxy_steam_sends_players_back_over_https()
    {
        using var proxied = _factory.WithWebHostBuilder(builder => builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true"));
        var client = proxied.CreateClient(new() { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/login");
        request.Headers.Host = "warhammer-matchmaker.com";
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        var steamUrl = WebUtility.UrlDecode(response.Headers.Location!.ToString());
        Assert.Contains("openid.return_to=https://warhammer-matchmaker.com/signin-steam", steamUrl);
    }

    [Fact]
    public async Task Sign_in_keys_are_kept_in_the_configured_folder_to_survive_restarts()
    {
        using var configured = _factory.WithWebHostBuilder(builder => builder.UseSetting("DataProtection:KeysPath", _keysPath));

        // La connexion de développement signe un cookie : la clé est créée à ce moment-là.
        await configured.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/dev/login?name=Cle");

        Assert.NotEmpty(Directory.GetFiles(_keysPath, "key-*.xml"));
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_keysPath)) Directory.Delete(_keysPath, recursive: true);
    }
}
