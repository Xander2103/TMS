using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.EmployeePlanning.Entities;

/// <summary>
/// The personnel-planning projection of a trip: one row per trip with an assigned driver,
/// keyed to the driver's employee. The TRIP is the source of truth — this row has no public
/// write API and is only ever mutated by <c>TripPlanningSyncService</c> inside the same
/// SaveChanges as the trip mutation (atomic driver moves, status follows the trip).
/// </summary>
public class TripPlanningEntry : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid DriverId { get; set; }

    /// <summary>Discriminator for planning consumers; always "Trip" today.</summary>
    public string SourceType { get; set; } = "Trip";

    public string TripNumber { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }

    /// <summary>"V-0001 · 1-ABC-123" — display only, refreshed on every sync.</summary>
    public string? VehicleSummary { get; set; }

    /// <summary>"Antwerpen → Rotterdam" — first loading to last unloading city.</summary>
    public string? RouteSummary { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Draft;

    public string? Notes { get; set; }
}
