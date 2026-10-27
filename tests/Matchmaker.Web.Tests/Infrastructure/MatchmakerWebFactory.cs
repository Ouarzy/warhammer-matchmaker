using System.Security.Claims;
using System.Text.Encodings.Web;
using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Ratings;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matches;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Matchmaker.Web.Tests.Infrastructure;

public sealed class MatchmakerWebFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Matchmaker", postgres.ConnectionString);
        builder.UseSetting("Steam:ApplicationKey", "test-key");
        builder.UseSetting("Tts:Enabled", "true");

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Scheme, null);
            services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = TestAuthentication.Scheme);
            services.AddSingleton<IRosterProvider, StubRosterProvider>();
        });
    }

    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Un client connecté en tant que ce joueur (créé en base s'il n'existe pas).</summary>
    public async Task<HttpClient> CreateClientAsAsync(PlayerId player, string displayName = "Joueur")
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PlayerAccounts>().SignInAsync(player, displayName, null);

        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestAuthentication.Header, player.SteamId);
        return client;
    }

    /// <summary>
    /// Enregistre un match accepté par les deux joueurs (déjà créés), chacun jouant une liste Custodes
    /// importée pour l'occasion et nommée « Liste de match ».
    /// </summary>
    public async Task<Guid> StartMatchAsync(PlayerId first, PlayerId second, bool ranked = true)
    {
        using var scope = Services.CreateScope();
        var lists = scope.ServiceProvider.GetRequiredService<ArmyListService>();
        var now = DateTimeOffset.UtcNow;
        async Task<QueueTicket> Ticket(PlayerId player)
        {
            var list = await lists.ImportAsync(player, Samples.CustodesCode, "Liste de match", GameFormats.StrikeForce);
            return new QueueTicket(player, list.Id, list.Format, ranked, EloRating.Initial, now);
        }

        var match = Match.Propose(await Ticket(first), await Ticket(second), now);
        match.Accept(first, now);
        match.Accept(second, now);
        await scope.ServiceProvider.GetRequiredService<MatchService>().StartAsync(match);
        return match.Id;
    }

    public async Task<ArchivedLists> ArchivedListsOfAsync(Guid matchId)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MatchService>().ArchivedListsOfAsync([matchId]);
    }

    public async Task WithMatchesAsync(Func<MatchService, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<MatchService>());
    }

    public static PlayerId NewPlayerId() => new(Random.Shared.NextInt64(76561197960265728, 76561199999999999).ToString());
}

/// <summary>Authentifie la requête avec le Steam ID passé dans un en-tête : remplace Steam dans les tests.</summary>
internal sealed class TestAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "Test";
    public const string Header = "X-Test-SteamId";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var steamId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "Joueur"), new Claim(SteamAuthentication.SteamIdClaim, steamId.ToString())],
            Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}
