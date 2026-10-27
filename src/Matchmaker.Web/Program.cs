using Matchmaker.Web.Accounts;
using Matchmaker.Web.Components;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<MatchmakerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Matchmaker")));
builder.Services.AddScoped<PlayerAccounts>();
builder.Services.AddSteamAuthentication(builder.Configuration);

var app = builder.Build();

// Une seule instance pour l'instant : on applique les migrations au démarrage.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<MatchmakerDbContext>().Database.MigrateAsync();

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

app.MapStaticAssets();
app.MapSteamAuthentication();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
