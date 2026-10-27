using Matchmaker.Web.Matchmaking;

namespace Matchmaker.Web.Matches;

/// <summary>Confirme chaque minute les résultats déclarés restés sans réponse plus de 24 h.</summary>
public sealed class ResultConfirmationService(
    IServiceScopeFactory scopes, PlayerNotifier notifier, ILogger<ResultConfirmationService> logger) : BackgroundService
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
                var confirmed = await scope.ServiceProvider.GetRequiredService<MatchService>().ConfirmOverdueResultsAsync(stoppingToken);
                notifier.Notify(confirmed.SelectMany(m => new[] { m.First.Player, m.Second.Player }));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Échec de la confirmation automatique des résultats.");
            }
        }
    }
}
