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

/// <summary>Recherche en cours d'un joueur ; <paramref name="PlannedFor"/> et <paramref name="PlannedSearchId"/> sont null pour une partie immédiate.</summary>
public sealed record OpenSearch(QueueTicket Ticket, DateTimeOffset? PlannedFor, Guid? PlannedSearchId = null)
{
    public static OpenSearch Planned(ScheduledSearch search) => new(search.Ticket, search.PlannedFor, search.Id);
}

/// <summary>Recherches immédiates (en mémoire) et planifiées (en base), vues ensemble.</summary>
public sealed class GameSearches(MatchmakingCoordinator coordinator, ScheduledGames scheduled, MatchService matches,
    PlayerAccounts accounts, ArmyListService lists, TimeProvider time)
{
    /// <param name="viewer">Le joueur qui consulte : une recherche réservée à un joueur n'est visible que de lui et de son auteur.</param>
    /// <returns>Les recherches, de la plus ancienne à la plus récente.</returns>
    public async Task<IReadOnlyList<OpenSearch>> OpenAsync(SearchKind kind = SearchKind.All, PlayerId? viewer = null,
        CancellationToken cancellationToken = default)
    {
        var live = kind == SearchKind.Planned
            ? []
            : coordinator.OpenSearches().Select(ticket => new OpenSearch(ticket, null));
        var planned = kind == SearchKind.Live
            ? []
            : (await scheduled.OpenSearchesAsync(cancellationToken)).Select(OpenSearch.Planned);
        return [.. live.Concat(planned).Where(search => IsVisibleTo(search, viewer)).OrderBy(search => search.Ticket.EnqueuedAt)];
    }

    public async Task<OpenSearch?> FindPlannedAsync(Guid id, PlayerId? viewer = null, CancellationToken cancellationToken = default) =>
        await scheduled.FindOpenSearchAsync(id, cancellationToken) is { } search && IsVisibleTo(OpenSearch.Planned(search), viewer)
            ? OpenSearch.Planned(search)
            : null;

    /// <summary>Parties qu'on propose à ce joueur en particulier, les plus anciennes d'abord.</summary>
    public async Task<IReadOnlyList<OpenSearch>> ProposedToAsync(PlayerId player, CancellationToken cancellationToken = default)
    {
        var live = coordinator.OpenSearches().Where(ticket => ticket.IsReservedFor(player)).Select(ticket => new OpenSearch(ticket, null));
        var planned = (await scheduled.ReservedForAsync(player, cancellationToken)).Select(OpenSearch.Planned);
        return [.. live.Concat(planned).OrderBy(search => search.Ticket.EnqueuedAt)];
    }

    /// <summary>Propositions de parties planifiées et parties proposées auxquelles le joueur n'a pas encore répondu : pastille du menu.</summary>
    public async Task<int> ToAnswerAsync(PlayerId player, CancellationToken cancellationToken = default) =>
        await scheduled.ProposalsToAnswerAsync(player, cancellationToken) + (await ProposedToAsync(player, cancellationToken)).Count;

    /// <param name="reservedFor">Le seul joueur à qui proposer la partie, quel que soit son Elo ; null pour n'importe qui.</param>
    public async Task SearchNowAsync(PlayerId player, Guid armyListId, bool ranked, PlayerId? reservedFor = null,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindAsync(player, cancellationToken);
        var list = await lists.FindAsync(armyListId, cancellationToken);
        if (!await matches.CanLookForAnotherOpponentAsync(player, cancellationToken))
            throw new InvalidOperationException($"Vous avez déjà {MatchService.MaxActiveMatches} parties en cours, planifiées ou recherchées.");
        if (account is null || list is null)
            throw new InvalidOperationException("Liste introuvable.");
        if (reservedFor is { } opponent && (opponent == player || await accounts.FindAsync(opponent, cancellationToken) is null))
            throw new InvalidOperationException("Choisissez un autre joueur inscrit comme adversaire.");

        coordinator.Search(account, list, ranked, reservedFor);
    }

    /// <summary>
    /// Lance avec cette liste la même recherche qu'un autre joueur : même format, même type de partie, même heure.
    /// Une partie qui était proposée à ce joueur lui est réservée en retour, pour l'appariement avec son auteur.
    /// </summary>
    public async Task JoinAsync(PlayerId player, Guid armyListId, OpenSearch search, CancellationToken cancellationToken = default)
    {
        if (search.Ticket.ReservedFor is { } reservedFor && reservedFor != player)
            throw new InvalidOperationException("Cette partie est proposée à un autre joueur.");
        var author = search.Ticket.ReservedFor is null ? (PlayerId?)null : search.Ticket.Player;
        var list = await lists.FindAsync(armyListId, cancellationToken);
        if (list is null || !list.IsCompatibleWith(search.Ticket.Format))
            throw new InvalidOperationException($"Choisissez une liste au format {GameFormats.LabelOf(search.Ticket.Format)}.");

        if (search.PlannedFor is not { } plannedFor)
        {
            await SearchNowAsync(player, armyListId, search.Ticket.Ranked, author, cancellationToken);
            return;
        }

        if (plannedFor < time.GetUtcNow() + Match.ScheduledAcceptanceNotice)
            throw new InvalidOperationException("Cette partie commence dans moins d'une heure : il est trop tard pour la rejoindre.");
        await scheduled.PlanAsync(player, armyListId, search.Ticket.Ranked, plannedFor, author, cancellationToken);
    }

    /// <summary>Refuse une partie proposée à ce joueur : la recherche de son auteur s'arrête.</summary>
    public async Task DeclineAsync(PlayerId player, OpenSearch search, CancellationToken cancellationToken = default)
    {
        if (!search.Ticket.IsReservedFor(player))
            throw new InvalidOperationException("Cette proposition ne vous concerne pas.");

        if (search.PlannedSearchId is { } id)
            await scheduled.DeclineReservedSearchAsync(player, id, cancellationToken);
        else
            coordinator.DeclineReservedSearch(player, search.Ticket.Player);
    }

    private static bool IsVisibleTo(OpenSearch search, PlayerId? viewer) =>
        search.Ticket.ReservedFor is not { } reservedFor || viewer == reservedFor || viewer == search.Ticket.Player;
}
