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

/// <summary>
/// Scores en cours envoyés depuis Tabletop Simulator par l'objet « Warhammer Matchmaker ». Rien n'est
/// déclaré depuis TTS : le score final se valide sur le site.
/// </summary>
public sealed class TtsReporting(MatchmakerDbContext db, MatchService matches, LiveScores liveScores, TimeProvider time)
{
    public const int SupportedVersion = 1;
    public const int MaxVictoryPoints = 100;

    public async Task<string?> CodeOfAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.ReportCodes.Where(c => c.MatchId == matchId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Score en cours, affiché en direct sur la page de la partie sans rien déclarer.</summary>
    public async Task<TtsReportOutcome> ShareLiveAsync(TtsReport report, CancellationToken cancellationToken = default)
    {
        var (outcome, seated) = await CheckAsync(report, cancellationToken);
        if (seated is not { } s) return outcome!;

        var (firstVp, secondVp) = s.Red == s.Match.First.Player ? (report.Red!.Vp, report.Blue!.Vp) : (report.Blue!.Vp, report.Red!.Vp);
        liveScores.Update(s.Match.Id, new LiveScore(firstVp, secondVp, report.RoundsPlayed, time.GetUtcNow()));
        return new(TtsReportStatus.Accepted, "Score en cours visible sur le site.", s.Match);
    }

    private sealed record Seated(Match Match, PlayerId Red);

    private async Task<(TtsReportOutcome? Refused, Seated? Seated)> CheckAsync(TtsReport report, CancellationToken cancellationToken)
    {
        if (Invalid(report) is { } problem)
            return (new(TtsReportStatus.Invalid, problem), null);

        var code = report.Code!.Trim().ToUpperInvariant();
        var matchId = await db.ReportCodes.Where(c => c.Code == code).Select(c => (Guid?)c.MatchId).FirstOrDefaultAsync(cancellationToken);
        if (matchId is not { } id || await matches.FindAsync(id, cancellationToken) is not { } match)
            return (new(TtsReportStatus.UnknownCode, "Code de partie inconnu : recopiez-le depuis la page de la partie sur le site."), null);

        var players = new[] { match.First.Player, match.Second.Player };
        var (red, blue, host) = (new PlayerId(report.Red!.SteamId!), new PlayerId(report.Blue!.SteamId!), new PlayerId(report.HostSteamId!));
        if (!players.Contains(red) || !players.Contains(blue) || red == blue || !players.Contains(host))
            return (new(TtsReportStatus.NotTheirMatch, "Les joueurs assis à la table ne sont pas ceux de cette partie."), null);

        if (match.Status == MatchStatus.ResultDeclared)
            return (new(TtsReportStatus.AlreadyReported, "Un score final a été proposé sur le site : le direct reprendra si la proposition est annulée."), null);
        if (match.Status != MatchStatus.Accepted)
            return (new(TtsReportStatus.AlreadyReported, "Cette partie est terminée."), null);

        return (null, new Seated(match, red));
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
