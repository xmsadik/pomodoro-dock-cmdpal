namespace PomodoroDock.Tests;

/// <summary>Settable local time over a fixed UTC+3 zone.</summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");

    public FakeTimeProvider(DateTime localNow) => LocalNow = localNow;

    public DateTime LocalNow { get; set; }

    public override TimeZoneInfo LocalTimeZone => Zone;

    public override DateTimeOffset GetUtcNow() =>
        new(DateTime.SpecifyKind(LocalNow - TimeSpan.FromHours(3), DateTimeKind.Utc));

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);
}
