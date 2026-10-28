using Matchmaker.Domain.ArmyLists;

namespace Matchmaker.Web.ArmyLists;

/// <summary>Récupère le contenu d'une liste d'armée depuis un outil externe.</summary>
public interface IRosterProvider
{
    /// <exception cref="RosterImportException">Code invalide, liste introuvable ou service indisponible.</exception>
    Task<Roster> FetchAsync(string code, CancellationToken cancellationToken = default);
}

public enum RosterImportError
{
    InvalidCode,
    NotFound,
    Unavailable,
    UnreadableContent,
    LimitReached,
}

public sealed class RosterImportException(RosterImportError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public RosterImportError Error { get; } = error;
}
