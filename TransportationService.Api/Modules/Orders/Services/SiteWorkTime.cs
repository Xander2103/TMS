using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Master sprint 2026-09-21 (D2) — the ONE place that derives the planned end of on-site work:
/// for a <see cref="StopType.Site"/> stop whose end is not manual, end = start + the activity's
/// planned duration (<c>DossierActivity.DurationHours</c>, decimal hours: 1.5 = 90 minutes).
/// Plain <see cref="DateTime"/> arithmetic, so work that runs past midnight simply lands on the
/// next day (22:00 + 4h = 02:00). Pure on purpose: no clock, no database, no time zone — a fixed
/// duration added to an instant is zone-independent.
///
/// Deliberately never applied to loading/unloading stops, and it only ever produces a
/// <c>PlannedTo</c>: time requirements, requested/confirmed windows and the hard bounds are
/// promises to the customer and are not derived from a work duration.
/// </summary>
public static class SiteWorkTime
{
    /// <summary>
    /// The <c>PlannedTo</c> a stop must carry. Returns <paramref name="plannedTo"/> unchanged when
    /// the rule does not apply: not a site stop, a manual end, no start, or no usable duration
    /// (unknown or negative — a missing duration is never read as "zero hours").
    /// </summary>
    public static DateTime? ResolvePlannedTo(
        StopType stopType, DateTime? plannedFrom, DateTime? plannedTo, bool plannedToIsManual, decimal? durationHours)
    {
        if (stopType != StopType.Site || plannedToIsManual
            || plannedFrom is not { } start || durationHours is not { } hours || hours < 0)
        {
            return plannedTo;
        }

        // Whole minutes: the planning UI works in minutes and 1.5 h must be exactly 90 of them.
        var minutes = (double)decimal.Round(hours * 60m, 0, MidpointRounding.AwayFromZero);
        return start.AddMinutes(minutes);
    }

    /// <summary>
    /// Request-side variant: the stop inputs with every non-manual site stop's end resolved, so
    /// validation and persistence both see the value that will actually be stored. Returns the
    /// SAME list instance when nothing changes. The manual flag is meaningless on a
    /// loading/unloading stop and is cleared there.
    /// </summary>
    public static IReadOnlyList<TransportOrderStopInput> Normalize(
        IReadOnlyList<TransportOrderStopInput> stops, decimal? durationHours)
    {
        List<TransportOrderStopInput>? normalized = null;
        for (var i = 0; i < stops.Count; i++)
        {
            var stop = stops[i];
            var isManual = stop.StopType == StopType.Site && stop.PlannedToIsManual;
            var plannedTo = ResolvePlannedTo(stop.StopType, stop.PlannedFrom, stop.PlannedTo, isManual, durationHours);
            if (plannedTo == stop.PlannedTo && isManual == stop.PlannedToIsManual)
            {
                continue;
            }

            normalized ??= [.. stops];
            normalized[i] = stop with { PlannedTo = plannedTo, PlannedToIsManual = isManual };
        }

        return normalized ?? stops;
    }

    /// <summary>
    /// Entity-side variant for a duration change outside the order PUT (dossier activity update):
    /// recomputes the end of every live, non-manual site stop. Returns true when a stop changed.
    /// </summary>
    public static bool Recompute(IEnumerable<TransportOrderStop> stops, decimal? durationHours)
    {
        var changed = false;
        foreach (var stop in stops.Where(s => !s.IsDeleted))
        {
            var plannedTo = ResolvePlannedTo(stop.StopType, stop.PlannedFrom, stop.PlannedTo, stop.PlannedToIsManual, durationHours);
            if (plannedTo != stop.PlannedTo)
            {
                stop.PlannedTo = plannedTo;
                changed = true;
            }
        }

        return changed;
    }
}
