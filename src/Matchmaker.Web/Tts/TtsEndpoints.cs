using System.Threading.RateLimiting;
using Matchmaker.Web.Matchmaking;

namespace Matchmaker.Web.Tts;

/// <summary>API appelée par l'objet TTS, depuis l'ordinateur de l'hôte de la partie.</summary>
public static class TtsEndpoints
{
    private const string RateLimitPolicy = "tts";

    // Le code de partie est le seul secret : on limite les essais pour qu'il ne puisse pas être deviné.
    public static IServiceCollection AddTtsReporting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TtsReporting>();
        services.AddSingleton<LiveScores>();
        var requestsPerMinute = configuration.GetValue("Tts:RequestsPerMinute", 10);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Un corps évite que la page d'erreur du site ne remplace la réponse, et l'objet affiche le message.
            options.OnRejected = (context, cancellationToken) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                new { message = "Trop d'envois : réessayez dans une minute." }, cancellationToken));
            options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });
    }

    public static void MapTtsReporting(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/tts").RequireRateLimiting(RateLimitPolicy).DisableAntiforgery();
        api.MapPost("/results", (TtsReport report, TtsReporting reporting, PlayerNotifier notifier, CancellationToken cancellationToken) =>
            Answer(reporting.ReportAsync(report, cancellationToken), notifier));
        api.MapPost("/live", (TtsReport report, TtsReporting reporting, PlayerNotifier notifier, CancellationToken cancellationToken) =>
            Answer(reporting.ShareLiveAsync(report, cancellationToken), notifier));
    }

    private static async Task<IResult> Answer(Task<TtsReportOutcome> reporting, PlayerNotifier notifier)
    {
        var outcome = await reporting;
        if (outcome.Match is { } match)
            notifier.Notify([match.First.Player, match.Second.Player]);

        var status = outcome.Status switch
        {
            TtsReportStatus.Accepted => StatusCodes.Status200OK,
            TtsReportStatus.Invalid => StatusCodes.Status400BadRequest,
            TtsReportStatus.UnknownCode => StatusCodes.Status404NotFound,
            TtsReportStatus.NotTheirMatch => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status409Conflict,
        };
        return Results.Json(new { message = outcome.Message }, statusCode: status);
    }
}
