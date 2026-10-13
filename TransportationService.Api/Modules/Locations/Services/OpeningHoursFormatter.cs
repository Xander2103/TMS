using System.Globalization;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Services;

/// <summary>
/// Formats structured opening intervals into the compact Dutch weekly summary used by the
/// location audit trail AND the order-stop location snapshot (master-data wave 2026-08-05):
/// "Ma 07:00–12:00, 13:00–17:00; Di 07:00–17:00". Pure/static so it can be shared without DI.
/// </summary>
public static class OpeningHoursFormatter
{
    public static readonly string[] DutchDayAbbreviations = ["Ma", "Di", "Wo", "Do", "Vr", "Za", "Zo"];

    /// <summary>Compact weekly summary; null when there are no structured intervals.</summary>
    public static string? Summarize(IReadOnlyCollection<LocationOpeningInterval> intervals)
    {
        if (intervals.Count == 0)
        {
            return null;
        }

        return string.Join("; ", intervals
            .GroupBy(i => i.DayOfWeek)
            .OrderBy(g => g.Key)
            .Select(g => DutchDayAbbreviations[g.Key - 1] + " " + string.Join(", ", g
                .OrderBy(i => i.FromTime)
                .Select(i => $"{FormatTime(i.FromTime)}–{FormatTime(i.ToTime)}"))));
    }

    /// <summary>The intervals of one day as display text, e.g. "07:00–12:00, 13:00–17:00".</summary>
    public static string FormatDayIntervals(IEnumerable<LocationOpeningInterval> dayIntervals) =>
        string.Join(", ", dayIntervals
            .OrderBy(i => i.FromTime)
            .Select(i => $"{FormatTime(i.FromTime)}–{FormatTime(i.ToTime)}"));

    public static string FormatTime(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
