using System.Collections.Concurrent;

namespace Matchmaker.Web.Tts;

/// <summary>Score en cours d'une partie, tel que lu sur la feuille de score LCT.</summary>
public sealed record LiveScore(int FirstVictoryPoints, int SecondVictoryPoints, int RoundsPlayed, DateTimeOffset At);

/// <summary>
/// Derniers scores en cours reçus de TTS. Gardés en mémoire seulement : ils ne servent qu'à suivre la
/// partie en direct, et l'objet TTS les renvoie de toute façon à chaque changement.
/// </summary>
public sealed class LiveScores
{
    /// <summary>Au-delà, un score sans nouvelles (partie abandonnée, objet fermé) est oublié.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

    private readonly ConcurrentDictionary<Guid, LiveScore> _scores = new();

    public void Update(Guid matchId, LiveScore score)
    {
        _scores[matchId] = score;
        foreach (var (id, old) in _scores)
            if (old.At < score.At - KeptFor)
                _scores.TryRemove(id, out _);
    }

    public LiveScore? Of(Guid matchId) => _scores.GetValueOrDefault(matchId);

    public void Forget(Guid matchId) => _scores.TryRemove(matchId, out _);
}
