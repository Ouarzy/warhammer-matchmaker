namespace Matchmaker.Web.Matchmaking;

/// <summary>Fait expirer toutes les quelques secondes les propositions de match non acceptées à temps.</summary>
public sealed class MatchExpiryService(MatchmakingCoordinator coordinator, ILogger<MatchExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                coordinator.ExpireOverdueMatches();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Échec de l'expiration des propositions de match.");
            }
        }
    }
}
