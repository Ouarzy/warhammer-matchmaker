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
    private readonly ConcurrentDictionary<Guid, LiveScore> _scores = new();

    public void Update(Guid matchId, LiveScore score) => _scores[matchId] = score;

    public LiveScore? Of(Guid matchId) => _scores.GetValueOrDefault(matchId);

    public void Forget(Guid matchId) => _scores.TryRemove(matchId, out _);
}
