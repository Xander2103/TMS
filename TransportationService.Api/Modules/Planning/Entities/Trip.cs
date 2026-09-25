using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Planning.Entities;

public enum TripStatus
{
    Draft,
    Planned,
    InProgress,
    Completed,
    Cancelled,
}

/// <summary>
/// D1: how the trip's vehicle was chosen. <see cref="Suggested"/> = the server proposed the
/// driver's fixed vehicle (<c>Vehicle.FixedDriverId</c>) and nobody picked one by hand yet, so a
/// driver change may replace it. <see cref="Manual"/> = a planner chose it; never replaced
/// automatically. A null value on <see cref="Trip.VehicleSelectionSource"/> is legacy/unknown and
/// is treated as Manual.
/// </summary>
public enum VehicleSelectionSource
{
    Suggested,
    Manual,
}

/// <summary>
/// A planned journey on one date: driver + vehicle (+ optional trailer) executing one or more
/// transport orders in sequence. Resources stay optional while drafting; promoting a trip to
/// Planned runs the conflict engine and requires a driver and vehicle.
/// </summary>
public class Trip : AuditableTenantEntity
{
    public string TripNumber { get; set; } = string.Empty;

    public DateOnly TripDate { get; set; }

    public Guid? DriverId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }

    /// <summary>
    /// D1: origin of <see cref="VehicleId"/>. Null = legacy row or no vehicle → treated as Manual
    /// (an existing choice is never overwritten). The fixed vehicle itself stays on the vehicle
    /// side (<c>Vehicle.FixedDriverId</c>); this only remembers whether it was merely proposed.
    /// </summary>
    public VehicleSelectionSource? VehicleSelectionSource { get; set; }

    public TripStatus Status { get; set; } = TripStatus.Draft;

    public DateTime? PlannedStart { get; set; }
    public DateTime? PlannedEnd { get; set; }

    // Costing inputs. Planned values are set while drafting; actuals after execution
    // (odometer/manual). PlannedDistanceKm is the TOTAL incl. empty kilometres.
    public decimal? PlannedDistanceKm { get; set; }
    public decimal? PlannedEmptyKm { get; set; }
    public decimal? ActualDistanceKm { get; set; }
    public decimal? ActualEmptyKm { get; set; }

    public string? Notes { get; set; }

    /// <summary>Manual delay applied on top of the internal ETA raming (Wave 10); 0 = none.</summary>
    public int ManualDelayMinutes { get; set; }
    public string? DelayReason { get; set; }

    /// <summary>
    /// Optimistic-concurrency token: bumped by every planning mutation. Clients echo the version
    /// they loaded; a mismatch means another planner changed the trip meanwhile (409).
    /// </summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public List<TripOrder> Orders { get; set; } = [];
}

/// <summary>Link between a trip and a transport order, ordered by <see cref="Sequence"/>.</summary>
public class TripOrder : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid TransportOrderId { get; set; }
    public int Sequence { get; set; }
}
