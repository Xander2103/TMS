using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Services;

public enum OpeningHoursStatus
{
    /// <summary>No structured opening hours exist at all — nothing can be concluded.</summary>
    NoData,

    /// <summary>The time falls inside one of the day's opening intervals.</summary>
    Inside,

    /// <summary>The time is before the start of a later interval on that day (including gaps between intervals).</summary>
    BeforeOpening,

    /// <summary>The time is after the day's last interval has closed.</summary>
    AfterClosing,

    /// <summary>Structured hours exist, but this day carries no intervals — closed (or unknown for this day).</summary>
    ClosedDay,
}

/// <summary>The verdict plus the day's intervals (sorted by start time) for display/warning text.</summary>
public record OpeningHoursCheck(OpeningHoursStatus Status, IReadOnlyList<LocationOpeningInterval> DayIntervals);

/// <summary>
/// Pure, reusable opening-hours check (order warnings, planning, dock appointments). Stateless —
/// registered as a singleton.
/// </summary>
public interface IOpeningHoursEvaluator
{
    OpeningHoursCheck Check(IReadOnlyList<LocationOpeningInterval> intervals, DayOfWeek day, TimeOnly time);
}

public sealed class OpeningHoursEvaluator : IOpeningHoursEvaluator
{
    public OpeningHoursCheck Check(IReadOnlyList<LocationOpeningInterval> intervals, DayOfWeek day, TimeOnly time)
    {
        if (intervals.Count == 0)
        {
            return new OpeningHoursCheck(OpeningHoursStatus.NoData, []);
        }

        // Entity rows use ISO weekdays (1 = maandag .. 7 = zondag); System.DayOfWeek starts at Sunday = 0.
        var isoDay = day == DayOfWeek.Sunday ? 7 : (int)day;
        var dayIntervals = intervals
            .Where(i => i.DayOfWeek == isoDay)
            .OrderBy(i => i.FromTime)
            .ToList();
        if (dayIntervals.Count == 0)
        {
            return new OpeningHoursCheck(OpeningHoursStatus.ClosedDay, dayIntervals);
        }

        // Inside an interval: start inclusive, end exclusive (at 17:00 a 07:00–17:00 site is closing).
        if (dayIntervals.Any(i => time >= i.FromTime && time < i.ToTime))
        {
            return new OpeningHoursCheck(OpeningHoursStatus.Inside, dayIntervals);
        }

        // Not inside: if any interval still opens later, the caller is early for it — this also
        // makes a time in the gap between two intervals report BeforeOpening of the NEXT one,
        // never AfterClosing of the previous.
        var status = dayIntervals.Any(i => i.FromTime > time)
            ? OpeningHoursStatus.BeforeOpening
            : OpeningHoursStatus.AfterClosing;
        return new OpeningHoursCheck(status, dayIntervals);
    }
}
