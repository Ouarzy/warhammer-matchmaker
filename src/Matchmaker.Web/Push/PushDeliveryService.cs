using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Push;

/// <summary>Envoie les notifications de la file à tous les appareils abonnés du joueur ; oublie les abonnements expirés.</summary>
public sealed class PushDeliveryService(PushOutbox outbox, IServiceScopeFactory scopes, ILogger<PushDeliveryService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (player, message) in outbox.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MatchmakerDbContext>();
                var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
                var subscriptions = await db.PushSubscriptions.Where(s => s.Player == player).ToListAsync(stoppingToken);
                foreach (var subscription in subscriptions)
                    if (await sender.SendAsync(subscription, message, stoppingToken) == PushDelivery.Gone)
                        db.PushSubscriptions.Remove(subscription);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Échec de l'envoi d'une notification push.");
            }
        }
    }
}
