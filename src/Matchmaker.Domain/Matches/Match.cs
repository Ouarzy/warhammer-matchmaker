using Matchmaker.Domain.Matchmaking;
using Matchmaker.Domain.Players;
using Matchmaker.Domain.Results;

namespace Matchmaker.Domain.Matches;

/// <summary>
/// Cycle de vie d'un match : proposé → accepté par les deux joueurs → résultat déclaré → confirmé
/// (par l'adversaire, ou automatiquement après 24 h sans réponse). Une partie en cours n'a pas de
/// limite de durée. Un abandon donne la victoire 20-0 à l'adversaire. Un match refusé, expiré ou
/// contesté n'impacte pas l'Elo.
/// </summary>
public sealed class Match
{
    public static readonly TimeSpan AcceptanceDelay = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ResultConfirmationDelay = TimeSpan.FromHours(24);

    // Un champ par camp (First / Second) : état simple à enregistrer en base.
    private bool _firstAccepted;
    private bool _secondAccepted;
    private int? _firstVictoryPoints;
    private int? _secondVictoryPoints;
    private int? _firstRatingChange;
    private int? _secondRatingChange;
    private int? _firstRatingBefore;
    private int? _secondRatingBefore;

    private Match(QueueTicket first, QueueTicket second, DateTimeOffset proposedAt)
    {
        First = first;
        Second = second;
        ProposedAt = proposedAt;
    }

    // Pour EF Core, qui renseigne les propriétés lui-même.
    private Match()
    {
        First = null!;
        Second = null!;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public QueueTicket First { get; }
    public QueueTicket Second { get; }
    public DateTimeOffset ProposedAt { get; }
    public MatchStatus Status { get; private set; } = MatchStatus.Proposed;
    public PlayerId? Declarer { get; private set; }
    public DateTimeOffset? DeclaredAt { get; private set; }
    public bool ConfirmedAutomatically { get; private set; }
    public PlayerId? AbandonedBy { get; private set; }

    public DateTimeOffset AcceptanceDeadline => ProposedAt + AcceptanceDelay;
    public DateTimeOffset? ResultConfirmationDeadline => DeclaredAt + ResultConfirmationDelay;
    public bool AffectsRating => First.Ranked && Status is MatchStatus.Completed or MatchStatus.Abandoned;

    public static Match Propose(QueueTicket first, QueueTicket second, DateTimeOffset now) => new(first, second, now);

    public QueueTicket OpponentOf(PlayerId player) => IsFirst(player) ? Second : First;

    public bool HasAccepted(PlayerId player) => IsFirst(player) ? _firstAccepted : _secondAccepted;

    public void Accept(PlayerId player, DateTimeOffset now)
    {
        EnsureStatus(MatchStatus.Proposed);
        if (now > AcceptanceDeadline)
            throw new InvalidOperationException("Le délai d'acceptation est dépassé.");

        if (IsFirst(player)) _firstAccepted = true;
        else _secondAccepted = true;

        if (_firstAccepted && _secondAccepted)
            Status = MatchStatus.Accepted;
    }

    /// <returns>Les tickets à remettre dans la file.</returns>
    public IReadOnlyList<QueueTicket> Decline(PlayerId player)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Proposed);

        Status = MatchStatus.Declined;
        return [OpponentOf(player)];
    }

    /// <returns>Les tickets à remettre dans la file : ceux des joueurs qui avaient accepté.</returns>
    public IReadOnlyList<QueueTicket> Expire(DateTimeOffset now)
    {
        EnsureStatus(MatchStatus.Proposed);
        if (now <= AcceptanceDeadline)
            throw new InvalidOperationException("Le délai d'acceptation n'est pas encore dépassé.");

        Status = MatchStatus.Expired;
        return [.. new[] { First, Second }.Where(t => HasAccepted(t.Player))];
    }

    public void DeclareResult(PlayerId declarer, int declarerVp, int opponentVp, DateTimeOffset now)
    {
        EnsureParticipant(declarer);
        EnsureStatus(MatchStatus.Accepted);
        ArgumentOutOfRangeException.ThrowIfNegative(declarerVp);
        ArgumentOutOfRangeException.ThrowIfNegative(opponentVp);

        Declarer = declarer;
        DeclaredAt = now;
        (_firstVictoryPoints, _secondVictoryPoints) = IsFirst(declarer)
            ? (declarerVp, opponentVp)
            : (opponentVp, declarerVp);
        Status = MatchStatus.ResultDeclared;
    }

    public void ConfirmResult(PlayerId player) => RespondToResult(player, MatchStatus.Completed);

    public void DisputeResult(PlayerId player) => RespondToResult(player, MatchStatus.Disputed);

    /// <summary>L'adversaire n'a pas répondu dans les 24 h : le résultat déclaré est confirmé.</summary>
    public void ConfirmAutomatically(DateTimeOffset now)
    {
        EnsureStatus(MatchStatus.ResultDeclared);
        if (now <= ResultConfirmationDeadline)
            throw new InvalidOperationException("Le délai de confirmation du résultat n'est pas encore dépassé.");

        ConfirmedAutomatically = true;
        Status = MatchStatus.Completed;
    }

    /// <summary>Abandon : victoire 20-0 pour l'adversaire, même si un résultat avait été déclaré.</summary>
    public void Abandon(PlayerId player)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Accepted, MatchStatus.ResultDeclared);

        AbandonedBy = player;
        Status = MatchStatus.Abandoned;
    }

    /// <summary>Points de victoire déclarés pour ce joueur (résultat déclaré, confirmé ou contesté).</summary>
    public int VictoryPointsOf(PlayerId player)
    {
        var victoryPoints = IsFirst(player) ? _firstVictoryPoints : _secondVictoryPoints;
        return victoryPoints ?? throw new InvalidOperationException("Aucun résultat n'a encore été déclaré.");
    }

    /// <summary>Score WTC du joueur, une fois le résultat confirmé ou le match abandonné.</summary>
    public WtcScore ScoreOf(PlayerId player)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.Completed, MatchStatus.Abandoned);

        if (Status == MatchStatus.Abandoned)
            return new WtcScore(player == AbandonedBy ? 0 : WtcScore.Max);

        return WtcScore.FromVictoryPoints(VictoryPointsOf(player), VictoryPointsOf(OpponentOf(player).Player));
    }

    /// <summary>Variation d'Elo du joueur, une fois appliquée (null sinon).</summary>
    public int? RatingChangeOf(PlayerId player) => IsFirst(player) ? _firstRatingChange : _secondRatingChange;

    /// <summary>Elo du joueur juste avant la mise à jour par ce match (null tant qu'elle n'a pas eu lieu).</summary>
    public int? RatingBeforeOf(PlayerId player) => IsFirst(player) ? _firstRatingBefore : _secondRatingBefore;

    public bool RatingApplied => _firstRatingChange is not null;

    internal void RecordRatingChange(PlayerId player, int before, int change)
    {
        if (IsFirst(player)) (_firstRatingBefore, _firstRatingChange) = (before, change);
        else (_secondRatingBefore, _secondRatingChange) = (before, change);
    }

    private void RespondToResult(PlayerId player, MatchStatus outcome)
    {
        EnsureParticipant(player);
        EnsureStatus(MatchStatus.ResultDeclared);
        if (player == Declarer)
            throw new InvalidOperationException("Seul l'adversaire du déclarant peut répondre au résultat.");

        Status = outcome;
    }

    private bool IsFirst(PlayerId player)
    {
        if (player == First.Player) return true;
        if (player == Second.Player) return false;
        throw new InvalidOperationException($"Le joueur {player.SteamId} ne participe pas à ce match.");
    }

    private void EnsureParticipant(PlayerId player) => IsFirst(player);

    private void EnsureStatus(params MatchStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvalidOperationException($"Action impossible : le match est au statut {Status}.");
    }
}
