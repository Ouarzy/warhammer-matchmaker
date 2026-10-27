using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using WebPushMessage = Lib.Net.Http.WebPush.PushMessage;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace Matchmaker.Web.Push;

public enum PushDelivery { Delivered, Gone, Failed }

/// <summary>Envoie une notification à un navigateur abonné.</summary>
public interface IPushSender
{
    Task<PushDelivery> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken);
}

/// <summary>Clés VAPID qui identifient le site auprès des services push (Google, Mozilla, Apple).</summary>
public sealed class PushOptions
{
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    public string Subject { get; set; } = "mailto:emilien@pecoul.fr";

    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

public sealed class WebPushSender(PushServiceClient client, PushOptions options, ILogger<WebPushSender> logger) : IPushSender
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PushDelivery> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken cancellationToken)
    {
        if (!options.Enabled) return PushDelivery.Failed;

        var target = new WebPushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        var content = new WebPushMessage(JsonSerializer.Serialize(message, Json))
        {
            TimeToLive = (int)TimeSpan.FromDays(1).TotalSeconds,
            Urgency = PushMessageUrgency.High,
        };
        try
        {
            await client.RequestPushMessageDeliveryAsync(target, content,
                new VapidAuthentication(options.PublicKey, options.PrivateKey) { Subject = options.Subject }, cancellationToken);
            return PushDelivery.Delivered;
        }
        catch (PushServiceClientException e) when (e.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            return PushDelivery.Gone;
        }
        catch (Exception e) when (e is PushServiceClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(e, "Notification push non délivrée.");
            return PushDelivery.Failed;
        }
    }
}
