using System.Collections.Concurrent;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Push;

namespace Matchmaker.Web.Tests.Infrastructure;

/// <summary>Remplace les services push : garde les notifications « envoyées ». Les endpoints en .../gone sont expirés.</summary>
public sealed class RecordingPushSender : IPushSender
{
    public ConcurrentQueue<(PlayerId Player, string Endpoint, PushMessage Message)> Sent { get; } = new();

    public Task<PushDelivery> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken)
    {
        if (subscription.Endpoint.EndsWith("/gone", StringComparison.Ordinal))
            return Task.FromResult(PushDelivery.Gone);
        Sent.Enqueue((subscription.Player, subscription.Endpoint, message));
        return Task.FromResult(PushDelivery.Delivered);
    }

    /// <summary>Attend (l'envoi se fait en arrière-plan) les notifications reçues par ce joueur.</summary>
    public async Task<IReadOnlyList<PushMessage>> ReceivedByAsync(PlayerId player, int atLeast = 1)
    {
        for (var i = 0; i < 100; i++)
        {
            var received = Sent.Where(s => s.Player == player).Select(s => s.Message).ToList();
            if (received.Count >= atLeast) return received;
            await Task.Delay(50);
        }
        return [.. Sent.Where(s => s.Player == player).Select(s => s.Message)];
    }
}
