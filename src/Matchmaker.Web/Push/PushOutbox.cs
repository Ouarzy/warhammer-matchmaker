using System.Threading.Channels;
using Matchmaker.Domain.Players;

namespace Matchmaker.Web.Push;

/// <summary>
/// File des notifications à envoyer, vidée en arrière-plan par <see cref="PushDeliveryService"/> :
/// un service push lent ou en panne ne ralentit jamais une action sur le site.
/// </summary>
public sealed class PushOutbox
{
    private readonly Channel<(PlayerId Player, PushMessage Message)> _queue =
        Channel.CreateBounded<(PlayerId, PushMessage)>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Send(PlayerId player, PushMessage message) => _queue.Writer.TryWrite((player, message));

    public void Send(IEnumerable<(PlayerId Player, PushMessage Message)> notifications)
    {
        foreach (var (player, message) in notifications)
            Send(player, message);
    }

    internal ChannelReader<(PlayerId Player, PushMessage Message)> Reader => _queue.Reader;
}
