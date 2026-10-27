using Matchmaker.Domain.Players;

namespace Matchmaker.Web.Push;

/// <summary>Abonnement d'un navigateur aux notifications push (une ligne par appareil).</summary>
public sealed class PushSubscription(PlayerId player, string endpoint, string p256dh, string auth, DateTimeOffset createdAt)
{
    public const int EndpointMaxLength = 1024;
    public const int KeyMaxLength = 256;

    public Guid Id { get; } = Guid.NewGuid();
    public PlayerId Player { get; private set; } = player;
    public string Endpoint { get; } = endpoint;
    public string P256dh { get; private set; } = p256dh;
    public string Auth { get; private set; } = auth;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;

    /// <summary>Même navigateur, réabonné (nouvelles clés ou autre compte connecté).</summary>
    public void Renew(PlayerId player, string p256dh, string auth, DateTimeOffset now)
    {
        Player = player;
        P256dh = p256dh;
        Auth = auth;
        CreatedAt = now;
    }
}
