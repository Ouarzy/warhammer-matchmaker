using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OpenId;
using AspNet.Security.OpenId.Steam;
using Matchmaker.Domain.Players;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Matchmaker.Web.Accounts;

public static class SteamAuthentication
{
    public const string SteamIdClaim = "steam_id";

    public static IServiceCollection AddSteamAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services
            // Les pages protégées redirigent vers /login, seul point d'entrée vers Steam.
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
            })
            .AddSteam(options =>
            {
                options.ApplicationKey = configuration["Steam:ApplicationKey"]
                    ?? throw new InvalidOperationException("Clé d'API Steam manquante (Steam:ApplicationKey).");
                // Point d'entrée Steam fixe : évite une requête de découverte OpenID à chaque connexion.
                options.Configuration = new OpenIdAuthenticationConfiguration
                {
                    AuthenticationEndpoint = "https://steamcommunity.com/openid/login",
                };
                options.Events.OnAuthenticated = OnSteamAuthenticated;
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        return services;
    }

    public static IEndpointRouteBuilder MapSteamAuthentication(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = IsLocal(returnUrl) ? returnUrl : "/profile" },
                [SteamAuthenticationDefaults.AuthenticationScheme]));

        endpoints.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/");
        });

        return endpoints;
    }

    public static PlayerId PlayerIdOf(ClaimsPrincipal user) =>
        new(user.FindFirstValue(SteamIdClaim) ?? throw new InvalidOperationException("Utilisateur non connecté via Steam."));

    private static async Task OnSteamAuthenticated(OpenIdAuthenticatedContext context)
    {
        var id = PlayerId.FromSteamOpenId(context.Identifier ?? "");
        // Profil public renvoyé par l'API Steam ; s'il manque (API indisponible), on se connecte avec le seul Steam ID.
        var profile = PublicProfileOf(context.UserPayload?.RootElement);
        var displayName = profile?.TryGetProperty("personaname", out var name) == true ? name.GetString() ?? id.SteamId : id.SteamId;
        var avatarUrl = profile?.TryGetProperty("avatarfull", out var avatar) == true ? avatar.GetString() : null;

        var accounts = context.HttpContext.RequestServices.GetRequiredService<PlayerAccounts>();
        await accounts.SignInAsync(id, displayName, avatarUrl, context.HttpContext.RequestAborted);

        context.Identity!.AddClaim(new Claim(SteamIdClaim, id.SteamId));
    }

    private static JsonElement? PublicProfileOf(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty("response", out var response)
        && response.TryGetProperty("players", out var players)
        && players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0
            ? players[0]
            : null;

    private static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
}
