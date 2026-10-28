using Matchmaker.Domain.Matches;

namespace Matchmaker.Web.Push;

/// <summary>
/// Contenu d'une notification. <paramref name="At"/> est mis dans l'heure locale du joueur par le service
/// worker, à la place de « {at} » dans le texte : le serveur ne connaît pas le fuseau de chacun.
/// </summary>
public sealed record PushMessage(string Title, string Body, string Url, string Tag, DateTimeOffset? At = null);

/// <summary>Les notifications envoyées par le site.</summary>
public static class PushMessages
{
    public static PushMessage ScheduledProposal(Match match) => new(
        "Adversaire trouvé", "Partie planifiée le {at} : acceptez-la sur le site.", "/play", $"proposal-{match.Id}", match.ScheduledFor);

    public static PushMessage ScheduledConfirmed(Match match) => new(
        "Partie confirmée", "Rendez-vous le {at} sur Tabletop Simulator.", $"/matches/{match.Id}", $"proposal-{match.Id}", match.ScheduledFor);

    public static PushMessage SearchGoesOn(DateTimeOffset plannedFor) => new(
        "Proposition annulée", "Votre adversaire n'a pas confirmé la partie du {at} : la recherche continue.", "/play", "search", plannedFor);

    public static PushMessage SearchStopped(DateTimeOffset plannedFor) => new(
        "Recherche arrêtée", "Pas d'adversaire pour la partie du {at} : il est trop tard pour en trouver un autre.", "/play", "search", plannedFor);

    public static PushMessage ResultToConfirm(Match match) => new(
        "Résultat à confirmer", "Votre adversaire a déclaré le score de votre partie.", $"/matches/{match.Id}", $"result-{match.Id}");

    public static PushMessage ResultContested(Match match) => new(
        "Score contesté", "Votre adversaire conteste le score et en propose un autre : acceptez-le ou contestez-le.", $"/matches/{match.Id}", $"result-{match.Id}");

    public static PushMessage OpponentAbandoned(Match match) => new(
        "Victoire par abandon", "Votre adversaire a abandonné la partie.", $"/matches/{match.Id}", $"result-{match.Id}");

    public static PushMessage Test() => new(
        "Notifications activées", "Vous serez prévenu ici des propositions de partie et des résultats à confirmer.", "/profile", "test");
}
