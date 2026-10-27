using Matchmaker.Domain.Matches;
using Matchmaker.Domain.Players;
using Matchmaker.Web.Data;
using Matchmaker.Web.Matches;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Tts;

/// <summary>Score lu sur la feuille de score LCT et envoyé par l'objet « Warhammer Matchmaker ».</summary>
public sealed record TtsReport(int Version, string? Code, string? HostSteamId, TtsSide? Red, TtsSide? Blue, int RoundsPlayed);

public sealed record TtsSide(string? SteamId, int Vp);

public enum TtsReportStatus { Accepted, Invalid, UnknownCode, NotTheirMatch, AlreadyReported }

public sealed record TtsReportOutcome(TtsReportStatus Status, string Message, Match? Match = null);

/// <summary>Scores envoyés depuis Tabletop Simulator par l'objet « Warhammer Matchmaker ».</summary>
public sealed class TtsReporting(MatchmakerDbContext db, MatchService matches)
{
    public const int SupportedVersion = 1;
    public const int MaxVictoryPoints = 100;

    public async Task<string?> CodeOfAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.ReportCodes.Where(c => c.MatchId == matchId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Déclare le score au nom de l'hôte de la partie TTS, comme s'il l'avait saisi sur le site :
    /// son adversaire doit encore le confirmer.
    /// </summary>
    public async Task<TtsReportOutcome> ReportAsync(TtsReport report, CancellationToken cancellationToken = default)
    {
        if (Invalid(report) is { } problem)
            return new(TtsReportStatus.Invalid, problem);

        var code = report.Code!.Trim().ToUpperInvariant();
        var matchId = await db.ReportCodes.Where(c => c.Code == code).Select(c => (Guid?)c.MatchId).FirstOrDefaultAsync(cancellationToken);
        if (matchId is not { } id || await matches.FindAsync(id, cancellationToken) is not { } match)
            return new(TtsReportStatus.UnknownCode, "Code de partie inconnu : recopiez-le depuis la page de la partie sur le site.");

        var players = new[] { match.First.Player, match.Second.Player };
        var (red, blue, host) = (new PlayerId(report.Red!.SteamId!), new PlayerId(report.Blue!.SteamId!), new PlayerId(report.HostSteamId!));
        if (!players.Contains(red) || !players.Contains(blue) || red == blue || !players.Contains(host))
            return new(TtsReportStatus.NotTheirMatch, "Les joueurs assis à la table ne sont pas ceux de cette partie.");

        if (match.Status != MatchStatus.Accepted)
            return new(TtsReportStatus.AlreadyReported, "Un résultat a déjà été déclaré pour cette partie.");

        var (hostVp, opponentVp) = host == red ? (report.Red.Vp, report.Blue.Vp) : (report.Blue.Vp, report.Red.Vp);
        var declared = await matches.DeclareResultAsync(host, id, hostVp, opponentVp, cancellationToken);
        return new(TtsReportStatus.Accepted, $"Score {hostVp} - {opponentVp} envoyé, en attente de confirmation par l'adversaire.", declared);
    }

    private static string? Invalid(TtsReport report)
    {
        if (report.Version != SupportedVersion)
            return "Version de l'objet non prise en charge : téléchargez la dernière version sur le site.";
        if (string.IsNullOrWhiteSpace(report.Code) || string.IsNullOrWhiteSpace(report.HostSteamId)
            || string.IsNullOrWhiteSpace(report.Red?.SteamId) || string.IsNullOrWhiteSpace(report.Blue?.SteamId))
            return "Il manque le code de partie ou un joueur : les deux joueurs doivent être assis (rouge et bleu).";
        if (report.Red.Vp is < 0 or > MaxVictoryPoints || report.Blue.Vp is < 0 or > MaxVictoryPoints)
            return $"Les points de victoire doivent être compris entre 0 et {MaxVictoryPoints}.";
        return null;
    }
}
