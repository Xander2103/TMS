namespace TransportationService.Api.Common.Scheduling;

/// <summary>
/// Functional grouping of an operational conflict, orthogonal to <see cref="ConflictSeverity"/>.
/// <see cref="Data"/> marks checks that could not run (completely) because input data is missing.
/// </summary>
public enum ConflictCategory
{
    Resource,
    Availability,
    Qualification,
    Capacity,
    Timing,
    Equipment,
    Document,
    Data,
}
