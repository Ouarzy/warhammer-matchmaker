using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Push;

/// <summary>Abonnements des navigateurs des joueurs.</summary>
public sealed class PushSubscriptions(MatchmakerDbContext db, TimeProvider time)
{
    public async Task SubscribeAsync(PlayerId player, string endpoint, string p256dh, string auth, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !IsKnownPushService(uri)
            || endpoint.Length > PushSubscription.EndpointMaxLength
            || string.IsNullOrWhiteSpace(p256dh) || p256dh.Length > PushSubscription.KeyMaxLength
            || string.IsNullOrWhiteSpace(auth) || auth.Length > PushSubscription.KeyMaxLength)
            throw new ArgumentException("Abonnement push invalide.");

        var now = time.GetUtcNow();
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, cancellationToken);
        if (existing is null)
            db.PushSubscriptions.Add(new PushSubscription(player, endpoint, p256dh, auth, now));
        else
            existing.Renew(player, p256dh, auth, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    // Le serveur envoie ses notifications à l'adresse fournie par le navigateur : seuls les services push des
    // navigateurs sont acceptés, pour qu'un joueur ne puisse pas lui faire appeler une autre adresse.
    private static readonly string[] PushServices =
        ["fcm.googleapis.com", "push.services.mozilla.com", "push.apple.com", "notify.windows.com"];

    private static bool IsKnownPushService(Uri uri) =>
        uri.IsDefaultPort && PushServices.Any(host => uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.Ordinal));

    public async Task UnsubscribeAsync(PlayerId player, string endpoint, CancellationToken cancellationToken = default) =>
        await db.PushSubscriptions.Where(s => s.Player == player && s.Endpoint == endpoint).ExecuteDeleteAsync(cancellationToken);

    public async Task<int> CountAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        await db.PushSubscriptions.CountAsync(s => s.Player == player, cancellationToken);
}
