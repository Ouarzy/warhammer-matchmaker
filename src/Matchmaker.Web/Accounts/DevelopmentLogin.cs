using System.Security.Claims;
using System.Text.RegularExpressions;
using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.ArmyLists.YellowScribe;
using Matchmaker.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Accounts;

/// <summary>
/// Développement uniquement : connexion d'un joueur de test, sans Steam, avec une liste d'exemple.
/// Permet de tester le matchmaking à deux joueurs sur une seule machine (une fenêtre privée par joueur).
/// </summary>
public static partial class DevelopmentLogin
{
    private const string SampleRoster = "yellowscribe-0c6cfa93.json";

    public static WebApplication MapDevelopmentLogin(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        app.MapGet("/dev/login", async (string? name, HttpContext context, PlayerAccounts accounts, MatchmakerDbContext db,
            TimeProvider time, IWebHostEnvironment environment) =>
        {
            if (name is null || !NamePattern().IsMatch(name))
                return Results.BadRequest("Nom de joueur de test : 1 à 16 lettres ou chiffres.");

            var player = await accounts.SignInAsync(new PlayerId($"dev-{name}"), name, null);
            if (!await db.ArmyLists.AnyAsync(l => l.Owner == player.Id))
            {
                var path = Path.Combine(environment.ContentRootPath, "..", "..", "docs", "samples", SampleRoster);
                var roster = YellowScribeRosterParser.Parse(await File.ReadAllTextAsync(path));
                db.ArmyLists.Add(ArmyList.Import(player.Id, null, GameFormats.StrikeForce, roster, time.GetUtcNow()));
                await db.SaveChangesAsync();
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, name), new Claim(SteamAuthentication.SteamIdClaim, player.Id.SteamId)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/play");
        });

        return app;
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,16}$")]
    private static partial Regex NamePattern();
}
