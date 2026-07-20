using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Warehousing.Entities;

/// <summary>
/// A warehouse/depot the tenant operates. The physical address is NOT duplicated here — it
/// lives on the linked master <c>Location</c>; the warehouse adds the operational layer
/// (opening hours, docks, contact).
/// </summary>
public class Warehouse : AuditableTenantEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Master location carrying the address/coordinates.</summary>
    public Guid LocationId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Daily operating window; appointments outside it conflict.</summary>
    public TimeOnly? OpensAt { get; set; }
    public TimeOnly? ClosesAt { get; set; }

    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }

    public string? Notes { get; set; }

    public List<Dock> Docks { get; set; } = [];
}

/// <summary>A loading/unloading position within a warehouse, with its capabilities.</summary>
public class Dock : AuditableTenantEntity
{
    public Guid WarehouseId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }

    public bool AllowsLoading { get; set; } = true;
    public bool AllowsUnloading { get; set; } = true;
    public bool AllowsAdr { get; set; }
    public bool Refrigerated { get; set; }

    public decimal? MaxVehicleLengthM { get; set; }
    public decimal? MaxVehicleHeightM { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}

public enum DockOperationType
{
    Loading,
    Unloading,
}

public enum DockAppointmentStatus
{
    Planned,
    Expected,
    Arrived,
    Waiting,
    AssignedToDock,
    InProgress,
    Completed,
    Cancelled,
    NoShow,
}

/// <summary>
/// A scheduled slot on (or awaiting) a dock. Links reuse the existing trip/order/fleet
/// aggregates; scan progress derives from the existing package lifecycle — this entity
/// never becomes a second package system.
/// </summary>
public class DockAppointment : AuditableTenantEntity
{
    public Guid WarehouseId { get; set; }

    /// <summary>Null while the appointment waits in the queue for a dock.</summary>
    public Guid? DockId { get; set; }

    public Guid? TripId { get; set; }
    public Guid? TransportOrderId { get; set; }
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }
    public Guid? DriverId { get; set; }

    public DockOperationType OperationType { get; set; }

    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }

    public DateTime? ArrivedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DockAppointmentStatus Status { get; set; } = DockAppointmentStatus.Planned;

    /// <summary>Reuses the order priority scale for queue ordering.</summary>
    public Orders.Entities.OrderPriority Priority { get; set; } = Orders.Entities.OrderPriority.Normal;

    public string? Reference { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Optimistic-concurrency token, same pattern as Trip.Version.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}
