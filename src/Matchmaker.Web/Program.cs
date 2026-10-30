using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Matchmaker.Web;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.ArmyLists.YellowScribe;
using Matchmaker.Web.Components;
using Matchmaker.Web.Data;
using Matchmaker.Web.Matches;
using Matchmaker.Web.Matchmaking;
using Matchmaker.Web.Tts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
// Site en français : les accents sont écrits tels quels plutôt qu'encodés (&#xE9;).
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddDbContext<MatchmakerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Matchmaker")));
builder.Services.AddScoped<PlayerAccounts>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IRosterProvider, YellowScribeClient>(http =>
{
    http.BaseAddress = YellowScribeClient.BaseAddress;
    http.Timeout = TimeSpan.FromSeconds(10);
    // Une liste fait environ 100 Ko : une réponse bien plus grosse n'est pas une liste.
    http.MaxResponseContentBufferSize = 2 * 1024 * 1024;
    http.DefaultRequestHeaders.UserAgent.ParseAdd("WarhammerMatchmaker/1.0 (+https://warhammer-matchmaker.com)");
});
builder.Services.AddSingleton<ImportLimiter>();
builder.Services.AddScoped<ArmyListService>();
builder.Services.AddSingleton<PlayerNotifier>();
builder.Services.AddSingleton<MatchmakingCoordinator>();
builder.Services.AddScoped<MatchService>();
builder.Services.AddScoped<ScheduledGames>();
builder.Services.AddTtsReporting(builder.Configuration);
builder.Services.AddHostedService<MatchExpiryService>();
builder.Services.AddHostedService<ResultConfirmationService>();
builder.Services.AddHostedService<ScheduledGamesExpiryService>();
builder.Services.AddSteamAuthentication(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<MatchmakerDbContext>();

// En production, les clés qui signent les cookies de connexion sont gardées sur un volume :
// sans cela, chaque redémarrage du site déconnecterait tous les joueurs.
if (builder.Configuration["DataProtection:KeysPath"] is { } keysPath)
    builder.Services.AddDataProtection()
        .SetApplicationName("Matchmaker")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

var app = builder.Build();

// Une seule instance pour l'instant : on applique les migrations au démarrage.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<MatchmakerDbContext>().Database.MigrateAsync();

app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapSteamAuthentication();
app.MapDevelopmentLogin();
app.MapTtsReporting();
app.MapRazorComponents<App>()
    // Blazor n'ajoute pas sa propre politique frame-ancestors : celle de SecurityHeaders, plus stricte, s'applique.
    .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null);

app.Run();
