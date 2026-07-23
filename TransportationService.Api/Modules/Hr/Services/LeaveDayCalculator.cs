using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.Hr.Services;

/// <summary>
/// Counts leave days for a single calendar year, matching the existing absence day-counting
/// (inclusive calendar days, half-day on a single-day Morning/Afternoon request), clipped to
/// the year so a range spanning a boundary only counts its portion in the given year.
/// </summary>
public static class LeaveDayCalculator
{
    public static decimal CountDaysInYear(DateOnly start, DateOnly end, AbsencePartDay part, int year)
    {
        // Half-days only apply to a genuine single-day request.
        if (start == end)
        {
            if (start.Year != year) return 0m;
            return part == AbsencePartDay.FullDay ? 1m : 0.5m;
        }

        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);
        var clippedStart = start < yearStart ? yearStart : start;
        var clippedEnd = end > yearEnd ? yearEnd : end;
        if (clippedEnd < clippedStart) return 0m;
        return clippedEnd.DayNumber - clippedStart.DayNumber + 1;
    }
}
