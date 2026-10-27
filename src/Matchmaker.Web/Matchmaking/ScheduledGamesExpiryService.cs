namespace Matchmaker.Web.Matchmaking;

/// <summary>
/// Chaque minute : fait expirer les propositions de partie planifiée non acceptées à temps
/// et les recherches planifiées devenues trop proches de leur heure.
/// </summary>
public sealed class ScheduledGamesExpiryService(IServiceScopeFactory scopes, ILogger<ScheduledGamesExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ScheduledGames>().ExpireOverdueAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Échec de l'expiration des parties planifiées.");
            }
        }
    }
}
