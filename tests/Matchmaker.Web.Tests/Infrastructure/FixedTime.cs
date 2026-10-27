namespace Matchmaker.Web.Tests.Infrastructure;

/// <summary>Horloge réglable à la main.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
