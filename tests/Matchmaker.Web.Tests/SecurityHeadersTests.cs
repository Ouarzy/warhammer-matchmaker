using Matchmaker.Web.Tests.Infrastructure;

namespace Matchmaker.Web.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SecurityHeadersTests(PostgresFixture postgres) : IDisposable
{
    private readonly MatchmakerWebFactory _factory = new(postgres);

    [Theory]
    [InlineData("/")]
    [InlineData("/ranking")]
    [InlineData("/health")]
    public async Task Every_response_carries_the_security_headers(string url)
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(url);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task The_site_cannot_be_framed_nor_load_scripts_from_elsewhere()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync("/");

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("script-src 'self' 'unsafe-inline'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("base-uri 'self'", policy);
        Assert.Contains("https://*.steamstatic.com", policy);
    }

    public void Dispose() => _factory.Dispose();
}
