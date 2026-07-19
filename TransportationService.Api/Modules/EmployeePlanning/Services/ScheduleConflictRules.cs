using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Hr.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Services;

/// <summary>
/// The single rule table for pairwise schedule conflicts, shared by the trip-side
/// conflict engine and the employee-side schedule/shift gate so both surfaces
/// always agree on severity.
/// </summary>
public static class ScheduleConflictRules
{
    /// <summary>Trips on the same day for the same employee always block.</summary>
    public const ConflictSeverity TripVsTrip = ConflictSeverity.Blocking;

    /// <summary>Tenant-configured severities parse leniently: unknown values fall back to Warning.</summary>
    public static ConflictSeverity Parse(string? value) =>
        Enum.TryParse<ConflictSeverity>(value, ignoreCase: true, out var parsed) ? parsed : ConflictSeverity.Warning;

    /// <summary>
    /// Approved training is configurable; every other approved absence blocks — the same rule
    /// whether a trip or a manual shift collides with the absence.
    /// </summary>
    public static ConflictSeverity TripVsAbsence(AbsenceType type, ConflictSeverity trainingSeverity) =>
        type == AbsenceType.Training ? trainingSeverity : ConflictSeverity.Blocking;

    /// <summary>Training shifts follow the training severity; other shift overlaps the shift-overlap severity.</summary>
    public static ConflictSeverity TripVsShift(ShiftType type, ConflictSeverity shiftOverlapSeverity, ConflictSeverity trainingSeverity) =>
        type == ShiftType.Training ? trainingSeverity : shiftOverlapSeverity;

    /// <summary>Overlap check where either side may lack times: a missing window counts as all-day.</summary>
    public static bool WindowsOverlap(TimeOnly? aStart, TimeOnly? aEnd, TimeOnly? bStart, TimeOnly? bEnd)
    {
        if (aStart is null || aEnd is null || bStart is null || bEnd is null)
        {
            return true;
        }

        return aStart < bEnd && bStart < aEnd;
    }
}
