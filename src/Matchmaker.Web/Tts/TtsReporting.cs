using Matchmaker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Matchmaker.Web.Tts;

/// <summary>Scores envoyés depuis Tabletop Simulator par l'objet « Warhammer Matchmaker ».</summary>
public sealed class TtsReporting(MatchmakerDbContext db)
{
    public async Task<string?> CodeOfAsync(Guid matchId, CancellationToken cancellationToken = default) =>
        await db.ReportCodes.Where(c => c.MatchId == matchId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);
}
