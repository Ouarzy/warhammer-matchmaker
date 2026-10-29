using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.ArmyLists;

public sealed class ArmyListService(MatchmakerDbContext db, IRosterProvider rosters, ImportLimiter limiter, TimeProvider time)
{
    public const int MaxListsPerPlayer = 50;

    /// <exception cref="RosterImportException">La liste n'a pas pu être récupérée, ou le joueur a atteint une limite.</exception>
    public async Task<ArmyList> ImportAsync(PlayerId owner, string code, string? name, GameFormat format, string? detachment = null,
        bool isTeamList = false, CancellationToken cancellationToken = default)
    {
        if (await db.ArmyLists.CountAsync(l => l.Owner == owner, cancellationToken) >= MaxListsPerPlayer)
            throw new RosterImportException(RosterImportError.LimitReached,
                $"Vous avez déjà {MaxListsPerPlayer} listes : supprimez-en une avant d'en importer une autre.");
        // Chaque import interroge YellowScribe : on ne le fait pas appeler en boucle par un même joueur.
        if (!limiter.TryAcquire(owner))
            throw new RosterImportException(RosterImportError.LimitReached,
                $"Trop d'imports à la suite : réessayez dans quelques minutes ({ImportLimiter.MaxImports} imports par tranche de {ImportLimiter.Window.TotalMinutes} minutes).");

        var roster = await rosters.FetchAsync(code, cancellationToken);
        var list = ArmyList.Import(owner, name, format, roster, time.GetUtcNow(), detachment, isTeamList);

        db.ArmyLists.Add(list);
        await db.SaveChangesAsync(cancellationToken);
        return list;
    }

    public async Task<ArmyList?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.ArmyLists.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<ArmyList>> ListOwnedByAsync(PlayerId owner, CancellationToken cancellationToken = default) =>
        await db.ArmyLists
            .Where(l => l.Owner == owner)
            .OrderByDescending(l => l.ImportedAt)
            .ToListAsync(cancellationToken);

    /// <returns>false si la liste n'existe pas ou n'appartient pas à ce joueur.</returns>
    /// <param name="detachment">Vide pour retirer le détachement.</param>
    public async Task<bool> UpdateAsync(PlayerId owner, Guid id, string name, string? detachment, bool isTeamList,
        CancellationToken cancellationToken = default)
    {
        var list = await FindOwnedAsync(owner, id, cancellationToken);
        if (list is null) return false;

        list.Rename(name);
        list.ChangeDetachment(detachment);
        list.ChangeTeamList(isTeamList);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Fait de cette liste la favorite du joueur (l'ancienne ne l'est plus), ou la retire si elle l'était déjà.</summary>
    /// <returns>false si la liste n'existe pas ou n'appartient pas à ce joueur.</returns>
    public async Task<bool> ToggleFavoriteAsync(PlayerId owner, Guid id, CancellationToken cancellationToken = default)
    {
        var list = await FindOwnedAsync(owner, id, cancellationToken);
        if (list is null) return false;

        if (list.IsFavorite)
            list.UnmarkAsFavorite();
        else
        {
            foreach (var previous in await db.ArmyLists.Where(l => l.Owner == owner && l.IsFavorite).ToListAsync(cancellationToken))
                previous.UnmarkAsFavorite();
            list.MarkAsFavorite();
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <returns>false si la liste n'existe pas ou n'appartient pas à ce joueur.</returns>
    public async Task<bool> DeleteAsync(PlayerId owner, Guid id, CancellationToken cancellationToken = default)
    {
        var list = await FindOwnedAsync(owner, id, cancellationToken);
        if (list is null) return false;

        db.ArmyLists.Remove(list);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<ArmyList?> FindOwnedAsync(PlayerId owner, Guid id, CancellationToken cancellationToken)
    {
        var list = await FindAsync(id, cancellationToken);
        return list is not null && list.IsOwnedBy(owner) ? list : null;
    }
}
