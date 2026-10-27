using Matchmaker.Domain.ArmyLists;
using Matchmaker.Web.ArmyLists;
using Matchmaker.Web.ArmyLists.YellowScribe;

namespace Matchmaker.Web.Tests.Infrastructure;

internal static class Samples
{
    public const string CustodesCode = "0c6cfa93";

    // Liste réelle récupérée sur YellowScribe le 2026-10-27 (Adeptus Custodes, 11e édition).
    public static readonly string CustodesJson = File.ReadAllText(Path.Combine("Samples", "yellowscribe-0c6cfa93.json"));
    public static readonly Roster Custodes = YellowScribeRosterParser.Parse(CustodesJson);
}

/// <summary>Faux YellowScribe : seul le code de la liste Custodes existe.</summary>
internal sealed class StubRosterProvider : IRosterProvider
{
    public Task<Roster> FetchAsync(string code, CancellationToken cancellationToken = default) =>
        code == Samples.CustodesCode
            ? Task.FromResult(Samples.Custodes)
            : throw new RosterImportException(RosterImportError.NotFound, "Code YellowScribe inconnu ou expiré.");
}
