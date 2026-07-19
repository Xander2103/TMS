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

    public List<TripOrder> Orders { get; set; } = [];
}

/// <summary>Link between a trip and a transport order, ordered by <see cref="Sequence"/>.</summary>
public class TripOrder : AuditableTenantEntity
{
    public Guid TripId { get; set; }
    public Guid TransportOrderId { get; set; }
    public int Sequence { get; set; }
}
