using TransportationService.Api.Modules.Attendance.Entities;

namespace TransportationService.Api.Modules.Attendance.Services;

/// <summary>
/// Centrale werktijdberekening — de ENIGE plek waar bruto/pauze/netto wordt berekend,
/// zodat dashboard, self-service, HR-overzicht en rapportering nooit uiteenlopen.
///
/// Definities:
///   GrossMinutes = uitpunt (of "nu" voor een lopende sessie) − inpunt;
///   BreakMinutes = som van alle pauzes (een open pauze telt tot "nu");
///   NetMinutes   = Gross − Break, nooit negatief.
///
/// Opslag is UTC; kalenderdag-attributie (rapportering, "vandaag gewerkt") gebeurt in
/// de tenant-tijdzone. Sessies en pauzes mogen over middernacht en DST-overgangen
/// lopen: duraties worden altijd uit UTC-instanten berekend (dus een shift over de
/// zomer-/wintertijdwissel is exact zo lang als hij werkelijk duurde) en per-dag-
/// splitsing gebruikt de lokale dagvensters. Negatieve duraties bestaan niet.
/// </summary>
public static class AttendanceCalculator
{
    public sealed record DaySlice(DateOnly Day, int GrossMinutes, int BreakMinutes)
    {
        public int NetMinutes => Math.Max(0, GrossMinutes - BreakMinutes);
    }

    public static int GrossMinutes(DateTime clockInUtc, DateTime? clockOutUtc, DateTime nowUtc)
    {
        var end = clockOutUtc ?? nowUtc;
        return end <= clockInUtc ? 0 : (int)(end - clockInUtc).TotalMinutes;
    }

    public static int BreakMinutes(IEnumerable<AttendanceBreak> breaks, DateTime nowUtc)
    {
        var total = TimeSpan.Zero;
        foreach (var b in breaks)
        {
            var end = b.EndedAt ?? nowUtc;
            if (end > b.StartedAt)
            {
                total += end - b.StartedAt;
            }
        }

        return (int)total.TotalMinutes;
    }

    public static int NetMinutes(int grossMinutes, int breakMinutes) => Math.Max(0, grossMinutes - breakMinutes);

    /// <summary>
    /// Tenant-tijdzone (IANA, bv. "Europe/Amsterdam") naar <see cref="TimeZoneInfo"/>;
    /// onbekende of lege waarden vallen terug op Europe/Amsterdam (tenant-default).
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? ianaTimeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(ianaTimeZoneId) ? "Europe/Amsterdam" : ianaTimeZoneId;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        }
    }

    /// <summary>
    /// Splitst een sessie (en zijn pauzes) per kalenderdag in de gegeven tijdzone.
    /// Een nachtshift 22:00–06:00 levert dus twee slices op; de sessie zelf blijft één
    /// registratie (er wordt nooit om 00:00 "geknipt" in de data, alleen in rapportage).
    /// </summary>
    public static IReadOnlyList<DaySlice> SplitByCalendarDay(
        DateTime clockInUtc,
        DateTime? clockOutUtc,
        IReadOnlyList<AttendanceBreak> breaks,
        TimeZoneInfo timeZone,
        DateTime nowUtc)
    {
        var endUtc = clockOutUtc ?? nowUtc;
        if (endUtc <= clockInUtc)
        {
            return [];
        }

        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clockInUtc, timeZone));
        var lastDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endUtc, timeZone));

        var slices = new List<DaySlice>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var dayStartUtc = LocalDayStartUtc(day, timeZone);
            var dayEndUtc = LocalDayStartUtc(day.AddDays(1), timeZone);

            var gross = Overlap(clockInUtc, endUtc, dayStartUtc, dayEndUtc);
            if (gross <= TimeSpan.Zero)
            {
                continue;
            }

            var breakTotal = TimeSpan.Zero;
            foreach (var b in breaks)
            {
                var breakEnd = b.EndedAt ?? nowUtc;
                var overlap = Overlap(b.StartedAt, breakEnd, dayStartUtc, dayEndUtc);
                if (overlap > TimeSpan.Zero)
                {
                    breakTotal += overlap;
                }
            }

            slices.Add(new DaySlice(day, (int)gross.TotalMinutes, (int)breakTotal.TotalMinutes));
        }

        return slices;
    }

    /// <summary>UTC-venstergrenzen van één lokale kalenderdag in de tenant-tijdzone.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) LocalDayWindowUtc(DateOnly day, TimeZoneInfo timeZone) =>
        (LocalDayStartUtc(day, timeZone), LocalDayStartUtc(day.AddDays(1), timeZone));

    private static DateTime LocalDayStartUtc(DateOnly day, TimeZoneInfo timeZone)
    {
        var localMidnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(localMidnight))
        {
            // Tijdzones die om middernacht verspringen (niet in de EU): schuif voorbij het gat.
            localMidnight = localMidnight.AddHours(1);
        }
        else if (timeZone.IsAmbiguousTime(localMidnight))
        {
            // Neem de eerste (zomertijd-)instantie van de dubbele middernacht.
            var offsets = timeZone.GetAmbiguousTimeOffsets(localMidnight);
            return DateTime.SpecifyKind(localMidnight - offsets.Max(), DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone);
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime windowStart, DateTime windowEnd)
    {
        var s = start > windowStart ? start : windowStart;
        var e = end < windowEnd ? end : windowEnd;
        return e > s ? e - s : TimeSpan.Zero;
    }
}
