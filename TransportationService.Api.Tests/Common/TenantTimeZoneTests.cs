using TransportationService.Api.Common;

namespace TransportationService.Api.Tests.Common;

/// <summary>
/// C-03, wall clock → UTC direction. A date filter typed by a human ("van 20/07 tot 24/07") must
/// become UTC instants of the tenant's local midnights before it is compared with an instant
/// column: Npgsql refuses a <c>Kind=Unspecified</c> DateTime outright (B07: the KPI activity
/// report and the storage billing derivation returned 500 on every call).
/// </summary>
public class TenantTimeZoneTests
{
    private static readonly TimeZoneInfo Amsterdam = TenantTimeZone.Resolve("Europe/Amsterdam");

    [Fact]
    public void ToUtc_ReturnsTheUtcInstant_OfATenantWallClockTime()
    {
        var summer = TenantTimeZone.ToUtc(new DateOnly(2026, 7, 20), new TimeOnly(0, 0), Amsterdam);
        var winter = TenantTimeZone.ToUtc(new DateOnly(2026, 1, 20), new TimeOnly(8, 30), Amsterdam);

        Assert.Equal(DateTimeKind.Utc, summer.Kind);
        Assert.Equal(new DateTime(2026, 7, 19, 22, 0, 0), summer);
        Assert.Equal(DateTimeKind.Utc, winter.Kind);
        Assert.Equal(new DateTime(2026, 1, 20, 7, 30, 0), winter);
    }

    [Fact]
    public void ToUtc_IsDstSafe_GapShiftsForward_AmbiguousTakesTheFirstOccurrence()
    {
        // 29/03/2026 02:30 does not exist in Amsterdam (clocks jump 02:00 → 03:00).
        var gap = TenantTimeZone.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), Amsterdam);
        // 25/10/2026 02:30 happens twice (clocks fall back 03:00 → 02:00); the first is still CEST.
        var ambiguous = TenantTimeZone.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), Amsterdam);

        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc), gap);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), ambiguous);
    }

    [Fact]
    public void LocalDayRangeUtc_CoversTheInclusiveLocalDays_AsAHalfOpenUtcWindow()
    {
        var (start, end) = TenantTimeZone.LocalDayRangeUtc(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 24), Amsterdam);

        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
        Assert.Equal(new DateTime(2026, 7, 19, 22, 0, 0), start);
        Assert.Equal(new DateTime(2026, 7, 24, 22, 0, 0), end);
        // Round trip: both edges are local midnight again.
        Assert.Equal(new DateTime(2026, 7, 20, 0, 0, 0), TenantTimeZone.ToWallClock(start, Amsterdam));
        Assert.Equal(new DateTime(2026, 7, 25, 0, 0, 0), TenantTimeZone.ToWallClock(end, Amsterdam));
    }
}
