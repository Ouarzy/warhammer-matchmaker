namespace Matchmaker.Web.Matches;

/// <summary>Une page de résultats (numérotée à partir de 1).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Number, int Size, int Total)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)Size));
}
