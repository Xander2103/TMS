using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Packages.Entities;

public enum PackageEventType
{
    Created,
    Labelled,
    LabelReprinted,
    Relabelled,
    StatusChanged,
    LoadScan,
    LoadMissing,
    WrongPackageScan,
    UnloadScan,
    Delivered,
    Refused,
    PartialDelivery,
    DamageReported,
    MarkedMissing,
    ExceptionResolved,
    GroupAssigned,
    GroupRemoved,
    GroupBroken,
    MovedToTrip,
    Cancelled,
    DispositionSet,
    ReturnLoaded,
    ReturnedToDepot,
    ReturnedToSender,
    RedeliveryLoaded,
    Quarantined,
    DepartureOverride,
    CompletionOverride,
    PodFinalized,

    // Wave 4 §3 — standalone warehouse scans (append-only: stored values must never shift).
    /// <summary>Arrival registration at the warehouse (trip-less Received scan).</summary>
    Received,
    /// <summary>Location move inside the warehouse (trip-less Moved scan).</summary>
    MovedLocation,
    /// <summary>Staged for outbound (trip-less Staged scan; operational marker, no status change).</summary>
    Staged,
}

/// <summary>
/// One immutable chain-of-custody event. APPEND-ONLY by design: there is no update or
/// delete API anywhere — corrections append new events, history is never rewritten.
/// </summary>
public class PackageEvent : AuditableTenantEntity
{
    public Guid PackageId { get; set; }
    public PackageEventType EventType { get; set; }

    public PackageLifecycleStatus? OldStatus { get; set; }
    public PackageLifecycleStatus? NewStatus { get; set; }

    public Guid? TripId { get; set; }
    public Guid? TransportOrderStopId { get; set; }
    public Guid? TransportOrderId { get; set; }

    /// <summary>Wave 4 §2: warehouse location involved in this event (FK-less snapshot id).</summary>
    public Guid? WarehouseLocationId { get; set; }

    public Guid? UserId { get; set; }
    public Guid? DriverId { get; set; }
    public string? DeviceInfo { get; set; }

    /// <summary>The barcode value that produced this event, when a scan was involved.</summary>
    public string? BarcodeUsed { get; set; }
    public string? Result { get; set; }
    public Guid? ScanEventId { get; set; }
    public Guid? ExceptionId { get; set; }

    public bool IsOverride { get; set; }
    public string? Notes { get; set; }

    // Location architecture: populated by GPS-capable clients later; nullable by design.
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    /// <summary>Client idempotency key for offline-queue replays.</summary>
    public Guid? ClientEventId { get; set; }

    public DateTime OccurredAt { get; set; }
}
