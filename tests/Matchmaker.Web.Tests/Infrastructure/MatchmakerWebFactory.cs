using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Matchmaker.Web.Tests.Infrastructure;

public sealed class MatchmakerWebFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Matchmaker", postgres.ConnectionString);
        builder.UseSetting("Steam:ApplicationKey", "test-key");
    }
}
