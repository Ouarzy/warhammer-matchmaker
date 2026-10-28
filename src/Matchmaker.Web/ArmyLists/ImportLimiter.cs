using System.Collections.Concurrent;
using Matchmaker.Domain.Players;

namespace Matchmaker.Web.ArmyLists;

/// <summary>Nombre d'imports de listes par joueur sur une fenêtre glissante, en mémoire (une seule instance du site).</summary>
public sealed class ImportLimiter(TimeProvider time)
{
    public const int MaxImports = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<PlayerId, Queue<DateTimeOffset>> _imports = new();

    /// <returns>false si le joueur a déjà importé <see cref="MaxImports"/> listes dans la fenêtre.</returns>
    public bool TryAcquire(PlayerId player)
    {
        var now = time.GetUtcNow();
        var imports = _imports.GetOrAdd(player, _ => new Queue<DateTimeOffset>());
        lock (imports)
        {
            while (imports.TryPeek(out var oldest) && oldest <= now - Window)
                imports.Dequeue();
            if (imports.Count >= MaxImports) return false;
            imports.Enqueue(now);
            return true;
        }
    }
}
