using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.Mvc;
using Matchmaker.Web.Accounts;

namespace Matchmaker.Web.Push;

/// <summary>Notifications push : abonnement des navigateurs et envoi en arrière-plan.</summary>
public static class PushEndpoints
{
    public sealed record SubscriptionRequest(string? Endpoint, SubscriptionKeys? Keys);

    public sealed record SubscriptionKeys(string? P256dh, string? Auth);

    public static IServiceCollection AddPushNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Push").Get<PushOptions>() ?? new PushOptions();
        services.AddSingleton(options);
        services.AddSingleton<PushOutbox>();
        services.AddScoped<PushSubscriptions>();
        services.AddHttpClient<PushServiceClient>(http => http.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IPushSender, WebPushSender>();
        services.AddHostedService<PushDeliveryService>();
        return services;
    }

    public static void MapPushNotifications(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/push").RequireAuthorization().DisableAntiforgery();

        api.MapPost("/subscriptions", async (SubscriptionRequest request, HttpContext context, PushSubscriptions subscriptions) =>
        {
            try
            {
                await subscriptions.SubscribeAsync(SteamAuthentication.PlayerIdOf(context.User),
                    request.Endpoint ?? "", request.Keys?.P256dh ?? "", request.Keys?.Auth ?? "", context.RequestAborted);
                return Results.NoContent();
            }
            catch (ArgumentException e)
            {
                return Results.BadRequest(new { message = e.Message });
            }
        });

        api.MapDelete("/subscriptions", async ([FromBody] SubscriptionRequest request, HttpContext context, PushSubscriptions subscriptions) =>
        {
            await subscriptions.UnsubscribeAsync(SteamAuthentication.PlayerIdOf(context.User), request.Endpoint ?? "", context.RequestAborted);
            return Results.NoContent();
        });

        api.MapPost("/test", (HttpContext context, PushOutbox outbox) =>
        {
            outbox.Send(SteamAuthentication.PlayerIdOf(context.User), PushMessages.Test());
            return Results.NoContent();
        });
    }
}
