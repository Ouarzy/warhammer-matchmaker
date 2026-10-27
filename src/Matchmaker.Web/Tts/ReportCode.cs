using System.Security.Cryptography;

namespace Matchmaker.Web.Tts;

/// <summary>
/// Code secret d'une partie, à coller dans l'objet TTS : seule preuve qu'un score envoyé
/// depuis TTS vient bien de cette partie (les Steam ID, eux, sont publics).
/// </summary>
public sealed class ReportCode(Guid matchId, string code)
{
    public const int Length = 8;

    // Sans 0/O ni 1/I, faciles à confondre en recopiant.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public Guid MatchId { get; } = matchId;
    public string Code { get; } = code;

    public static string Generate() => new(RandomNumberGenerator.GetItems<char>(Alphabet, Length));
}
