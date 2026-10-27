using Matchmaker.Domain.ArmyLists;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.ArmyLists;

public sealed class ArmyListService(MatchmakerDbContext db, IRosterProvider rosters, TimeProvider time)
{
    /// <exception cref="RosterImportException">La liste n'a pas pu être récupérée.</exception>
    public async Task<ArmyList> ImportAsync(PlayerId owner, string code, string? name, GameFormat format, string? detachment = null,
        CancellationToken cancellationToken = default)
    {
        var roster = await rosters.FetchAsync(code, cancellationToken);
        var list = ArmyList.Import(owner, name, format, roster, time.GetUtcNow(), detachment);

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
    public async Task<bool> UpdateAsync(PlayerId owner, Guid id, string name, string? detachment, CancellationToken cancellationToken = default)
    {
        var list = await FindOwnedAsync(owner, id, cancellationToken);
        if (list is null) return false;

        list.Rename(name);
        list.ChangeDetachment(detachment);
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
