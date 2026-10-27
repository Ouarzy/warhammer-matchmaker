using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;

namespace Matchmaker.Web.Accounts;

public sealed class PlayerAccounts(MatchmakerDbContext db)
{
    public async Task<Player?> FindAsync(PlayerId id, CancellationToken cancellationToken = default) =>
        await db.Players.FindAsync([id], cancellationToken);

    /// <summary>Crée le joueur à sa première connexion Steam, puis rafraîchit son profil à chaque connexion.</summary>
    public async Task<Player> SignInAsync(PlayerId id, string displayName, string? avatarUrl, CancellationToken cancellationToken = default)
    {
        var player = await FindAsync(id, cancellationToken);
        if (player is null)
        {
            player = new Player(id);
            db.Players.Add(player);
        }

        player.UpdateProfile(displayName, avatarUrl);
        await db.SaveChangesAsync(cancellationToken);
        return player;
    }
}
