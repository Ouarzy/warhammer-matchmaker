using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Accounts;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.Matches;

namespace Matchmaker.Web.Matchmaking;

public enum SearchKind
{
    All,
    Live,
    Planned,
}

/// <summary>Recherche en cours d'un joueur ; <paramref name="PlannedFor"/> est null pour une partie immédiate.</summary>
public sealed record OpenSearch(QueueTicket Ticket, DateTimeOffset? PlannedFor);

/// <summary>Recherches immédiates (en mémoire) et planifiées (en base), vues ensemble.</summary>
public sealed class GameSearches(MatchmakingCoordinator coordinator, ScheduledGames scheduled, MatchService matches,
    PlayerAccounts accounts, ArmyListService lists, TimeProvider time)
{
    /// <returns>Les recherches, de la plus ancienne à la plus récente.</returns>
    public async Task<IReadOnlyList<OpenSearch>> OpenAsync(SearchKind kind = SearchKind.All, CancellationToken cancellationToken = default)
    {
        var live = kind == SearchKind.Planned
            ? []
            : coordinator.OpenSearches().Select(ticket => new OpenSearch(ticket, null));
        var planned = kind == SearchKind.Live
            ? []
            : (await scheduled.OpenSearchesAsync(cancellationToken)).Select(search => new OpenSearch(search.Ticket, search.PlannedFor));
        return [.. live.Concat(planned).OrderBy(search => search.Ticket.EnqueuedAt)];
    }

    public async Task SearchNowAsync(PlayerId player, Guid armyListId, bool ranked, CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(player, cancellationToken);
        var list = await lists.FindAsync(armyListId, cancellationToken);
        if (!await matches.CanLookForAnotherOpponentAsync(player, cancellationToken))
            throw new InvalidOperationException($"Vous avez déjà {MatchService.MaxActiveMatches} parties en cours, planifiées ou recherchées.");
        if (account is null || list is null)
            throw new InvalidOperationException("Liste introuvable.");

        coordinator.Search(account, list, ranked);
    }

    /// <summary>Lance avec cette liste la même recherche qu'un autre joueur : même format, même type de partie, même heure.</summary>
    public async Task JoinAsync(PlayerId player, Guid armyListId, OpenSearch search, CancellationToken cancellationToken = default)
    {
        var list = await lists.FindAsync(armyListId, cancellationToken);
        if (list is null || !list.IsCompatibleWith(search.Ticket.Format))
            throw new InvalidOperationException($"Choisissez une liste au format {search.Ticket.Format.Name}.");

        if (search.PlannedFor is not { } plannedFor)
        {
            await SearchNowAsync(player, armyListId, search.Ticket.Ranked, cancellationToken);
            return;
        }

        if (plannedFor < time.GetUtcNow() + Match.ScheduledAcceptanceNotice)
            throw new InvalidOperationException("Cette partie commence dans moins d'une heure : il est trop tard pour la rejoindre.");
        await scheduled.PlanAsync(player, armyListId, search.Ticket.Ranked, plannedFor, cancellationToken);
    }
}
