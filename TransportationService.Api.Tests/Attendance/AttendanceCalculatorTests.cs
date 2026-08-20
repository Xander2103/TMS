using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Centrale werktijdberekening: bruto/pauze/netto, meerdere pauzes, nachtshiften over
/// middernacht, DST-overgangen en kalenderdag-splitsing in de tenant-tijdzone. Negatieve
/// duraties bestaan niet.
/// </summary>
public class AttendanceCalculatorTests
{
    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    private static AttendanceBreak Break(DateTime start, DateTime? end) =>
        new() { Id = Guid.NewGuid(), StartedAt = start, EndedAt = end };

    [Fact]
    public void GrossBreakNet_WithMultipleBreaks_MatchSpecExample()
    {
        // 07:54 → 16:13 = 8u19 bruto; pauzes 10:02–10:12 + 12:03–12:31 + 15:14–15:21 = 45m.
        var clockIn = new DateTime(2026, 8, 20, 5, 54, 0, DateTimeKind.Utc);   // 07:54 lokale tijd
        var clockOut = new DateTime(2026, 8, 20, 14, 13, 0, DateTimeKind.Utc); // 16:13 lokale tijd
        var breaks = new[]
        {
            Break(new DateTime(2026, 8, 20, 8, 2, 0, DateTimeKind.Utc), new DateTime(2026, 8, 20, 8, 12, 0, DateTimeKind.Utc)),
            Break(new DateTime(2026, 8, 20, 10, 3, 0, DateTimeKind.Utc), new DateTime(2026, 8, 20, 10, 31, 0, DateTimeKind.Utc)),
            Break(new DateTime(2026, 8, 20, 13, 14, 0, DateTimeKind.Utc), new DateTime(2026, 8, 20, 13, 21, 0, DateTimeKind.Utc)),
        };

        var gross = AttendanceCalculator.GrossMinutes(clockIn, clockOut, clockOut);
        var breakMinutes = AttendanceCalculator.BreakMinutes(breaks, clockOut);

        Assert.Equal(8 * 60 + 19, gross);
        Assert.Equal(45, breakMinutes);
        Assert.Equal(8 * 60 + 19 - 45, AttendanceCalculator.NetMinutes(gross, breakMinutes));
    }

    [Fact]
    public void OpenSessionAndOpenBreak_CountUntilNow()
    {
        var clockIn = new DateTime(2026, 8, 20, 6, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 8, 20, 10, 30, 0, DateTimeKind.Utc);
        var breaks = new[] { Break(new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc), null) };

        Assert.Equal(270, AttendanceCalculator.GrossMinutes(clockIn, null, now));
        Assert.Equal(30, AttendanceCalculator.BreakMinutes(breaks, now));
    }

    [Fact]
    public void Durations_AreNeverNegative()
    {
        var later = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
        var earlier = new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(0, AttendanceCalculator.GrossMinutes(later, earlier, earlier));
        Assert.Equal(0, AttendanceCalculator.BreakMinutes([Break(later, earlier)], earlier));
        Assert.Equal(0, AttendanceCalculator.NetMinutes(10, 45));
    }

    [Fact]
    public void OvernightShift_SplitsAcrossTwoCalendarDays_AndStaysOneSession()
    {
        // 22:00 (di) → 06:00 (wo) lokale zomertijd = 20:00 → 04:00 UTC; pauze 01:30–02:00 lokaal.
        var clockIn = new DateTime(2026, 8, 18, 20, 0, 0, DateTimeKind.Utc);
        var clockOut = new DateTime(2026, 8, 19, 4, 0, 0, DateTimeKind.Utc);
        var breaks = new[]
        {
            Break(new DateTime(2026, 8, 18, 23, 30, 0, DateTimeKind.Utc), new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc)),
        };

        var slices = AttendanceCalculator.SplitByCalendarDay(clockIn, clockOut, breaks, Brussels, clockOut);

        Assert.Equal(2, slices.Count);
        Assert.Equal(new DateOnly(2026, 8, 18), slices[0].Day);
        Assert.Equal(120, slices[0].GrossMinutes);       // 22:00–24:00 lokaal
        Assert.Equal(0, slices[0].BreakMinutes);         // pauze valt na middernacht lokaal
        Assert.Equal(new DateOnly(2026, 8, 19), slices[1].Day);
        Assert.Equal(360, slices[1].GrossMinutes);       // 00:00–06:00 lokaal
        Assert.Equal(30, slices[1].BreakMinutes);
        Assert.Equal(480, slices.Sum(s => s.GrossMinutes));
    }

    [Fact]
    public void BreakSpanningLocalMidnight_IsSplitWithoutNegativeParts()
    {
        var clockIn = new DateTime(2026, 8, 18, 20, 0, 0, DateTimeKind.Utc);
        var clockOut = new DateTime(2026, 8, 19, 4, 0, 0, DateTimeKind.Utc);
        // Pauze 23:45–00:15 lokaal = 21:45–22:15 UTC.
        var breaks = new[]
        {
            Break(new DateTime(2026, 8, 18, 21, 45, 0, DateTimeKind.Utc), new DateTime(2026, 8, 18, 22, 15, 0, DateTimeKind.Utc)),
        };

        var slices = AttendanceCalculator.SplitByCalendarDay(clockIn, clockOut, breaks, Brussels, clockOut);

        Assert.Equal(15, slices[0].BreakMinutes);
        Assert.Equal(15, slices[1].BreakMinutes);
        Assert.All(slices, s => Assert.True(s.NetMinutes >= 0));
    }

    [Fact]
    public void DstFallBack_ShiftDurationIsRealElapsedTime()
    {
        // Nacht van 24 op 25 oktober 2026: klok gaat om 03:00 lokaal terug naar 02:00.
        // 22:00 (za, UTC+2) → 06:00 (zo, UTC+1) is werkelijk 9 uur.
        var clockIn = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);
        var clockOut = new DateTime(2026, 10, 25, 5, 0, 0, DateTimeKind.Utc);

        Assert.Equal(540, AttendanceCalculator.GrossMinutes(clockIn, clockOut, clockOut));

        var slices = AttendanceCalculator.SplitByCalendarDay(clockIn, clockOut, [], Brussels, clockOut);
        Assert.Equal(540, slices.Sum(s => s.GrossMinutes));
        Assert.Equal(2, slices.Count);
        Assert.Equal(120, slices[0].GrossMinutes);   // za 22:00–24:00
        Assert.Equal(420, slices[1].GrossMinutes);   // zo 00:00–06:00 met dubbele 02:00–03:00 = 7 klokuren
    }

    [Fact]
    public void DstSpringForward_LostHourIsNotCounted()
    {
        // Nacht van 28 op 29 maart 2026: 02:00 → 03:00. 22:00 (za) → 06:00 (zo) = 7 echte uren.
        var clockIn = new DateTime(2026, 3, 28, 21, 0, 0, DateTimeKind.Utc);  // 22:00 UTC+1
        var clockOut = new DateTime(2026, 3, 29, 4, 0, 0, DateTimeKind.Utc);  // 06:00 UTC+2

        Assert.Equal(420, AttendanceCalculator.GrossMinutes(clockIn, clockOut, clockOut));
        var slices = AttendanceCalculator.SplitByCalendarDay(clockIn, clockOut, [], Brussels, clockOut);
        Assert.Equal(420, slices.Sum(s => s.GrossMinutes));
    }

    [Fact]
    public void ResolveTimeZone_FallsBackOnUnknownIds()
    {
        Assert.NotNull(AttendanceCalculator.ResolveTimeZone(null));
        Assert.NotNull(AttendanceCalculator.ResolveTimeZone("Not/AZone"));
        Assert.Equal(
            AttendanceCalculator.ResolveTimeZone("Europe/Amsterdam").BaseUtcOffset,
            AttendanceCalculator.ResolveTimeZone("").BaseUtcOffset);
    }
}
