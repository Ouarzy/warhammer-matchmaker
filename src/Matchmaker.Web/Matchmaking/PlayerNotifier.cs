using Matchmaker.Domain.Players;

namespace Matchmaker.Web.Matchmaking;

/// <summary>Prévient les pages ouvertes qu'un joueur doit rafraîchir sa situation.</summary>
public sealed class PlayerNotifier
{
    public event Action<PlayerId>? PlayerChanged;

    public void Notify(IEnumerable<PlayerId> players)
    {
        foreach (var player in players.Distinct())
            PlayerChanged?.Invoke(player);
    }
}
