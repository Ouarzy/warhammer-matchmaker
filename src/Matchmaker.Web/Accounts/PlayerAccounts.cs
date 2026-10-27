using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Accounts;

public sealed class PlayerAccounts(MatchmakerDbContext db)
{
    public async Task<Player?> FindAsync(PlayerId id, CancellationToken cancellationToken = default) =>
        await db.Players.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyDictionary<PlayerId, Player>> FindManyAsync(IEnumerable<PlayerId> ids, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Players.Where(p => wanted.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
    }

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
