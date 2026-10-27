using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Matchmaker.Web.Matches;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Accounts;

public sealed class PlayerAccounts(MatchmakerDbContext db)
{
    public async Task<Player?> FindAsync(PlayerId id, CancellationToken cancellationToken = default) =>
        await db.Players.FindAsync([id], cancellationToken);

    public const int SearchMinLength = 2;

    /// <summary>Joueurs dont le nom contient le texte cherché (sans tenir compte de la casse), meilleurs Elo d'abord.</summary>
    public async Task<IReadOnlyList<Player>> SearchAsync(string query, int limit = 20, CancellationToken cancellationToken = default)
    {
        query = query.Trim();
        if (query.Length < SearchMinLength)
            return [];

        var pattern = $"%{query.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_")}%";
        return await db.Players
            .Where(p => EF.Functions.ILike(p.DisplayName, pattern, @"\"))
            .OrderByDescending(p => p.Rating)
            .ThenBy(p => p.DisplayName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public const int DirectoryPageSize = 50;

    /// <summary>Tous les joueurs, par ordre alphabétique sans tenir compte de la casse, page par page.</summary>
    public async Task<Page<Player>> DirectoryPageAsync(int page, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        var total = await db.Players.CountAsync(cancellationToken);
        var players = await db.Players
            .OrderBy(p => p.DisplayName.ToLower())
            .ThenBy(p => p.Id)
            .Skip((page - 1) * DirectoryPageSize)
            .Take(DirectoryPageSize)
            .ToListAsync(cancellationToken);
        return new Page<Player>(players, page, DirectoryPageSize, total);
    }

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
