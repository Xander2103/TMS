using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Scanning.Entities;

public enum ScanType
{
    Load,
    Unload,
    Return,
    Depot,
    Exception,

    // Wave 4 §3 — standalone warehouse scans (append-only: stored values must never shift).
    /// <summary>Arrival registration at the warehouse (trip-less).</summary>
    Received = 5,
    /// <summary>Location move inside the warehouse (trip-less).</summary>
    Moved = 6,
    /// <summary>Staged for outbound (operational marker, trip-less).</summary>
    Staged = 7,
}

/// <summary>
/// Deterministic server-side classification of one scan. Warnings (duplicate, unexpected,
/// wrong item, over-delivery) are always recorded — never silently dropped — so the trail
/// stays complete; only quantities of counting results feed the tallies.
/// </summary>
public enum ScanResult
{
    Expected,
    UnexpectedItem,
    WrongItem,
    DuplicateScan,
    OverDelivery,
    DamagedItem,
    ManualCorrection,
}

/// <summary>
/// One scan (or manual correction) against a stop of a trip. TransportOrderId is the stop's
/// order (the scan context); CargoItemId is the resolved item, which for a WrongItem scan
/// belongs to another order on the same trip and for an UnexpectedItem stays null.
/// The server is the source of truth: totals are always derived from these rows.
/// </summary>
public class ScanEvent : AuditableTenantEntity
{
    /// <summary>Null since Wave 4 §3 for standalone warehouse scans (Received/Moved/Staged
    /// and trip-less Return) — per-trip tallies filter by TripId and never see them.</summary>
    public Guid? TripId { get; set; }
    /// <summary>Null only for a standalone scan of an UNKNOWN barcode (warning row — recorded, never dropped).</summary>
    public Guid? TransportOrderId { get; set; }
    public Guid? TransportOrderStopId { get; set; }
    public Guid? CargoItemId { get; set; }

    /// <summary>Wave 4 §2: warehouse location involved (FK-less snapshot id).</summary>
    public Guid? WarehouseLocationId { get; set; }

    /// <summary>Resolved tracked package, when the barcode belongs to the package registry.</summary>
    public Guid? PackageId { get; set; }

    /// <summary>Precise package outcome (PackageScanOutcome name) alongside the coarse Result.</summary>
    public string? PackageOutcome { get; set; }

    /// <summary>Client idempotency key: replaying the same key returns the original outcome.</summary>
    public Guid? ClientEventId { get; set; }

    public Guid? UserId { get; set; }
    public Guid? DriverId { get; set; }

    public DateTime OccurredAt { get; set; }

    public ScanType ScanType { get; set; }
    public ScanResult Result { get; set; }

    /// <summary>Null for manual corrections entered without a physical scan.</summary>
    public string? Barcode { get; set; }

    /// <summary>Scanned quantity; for corrections the signed delta that makes the tally match.</summary>
    public decimal Quantity { get; set; }

    public bool Damaged { get; set; }
    public string? DamageNote { get; set; }

    public string? CorrectionReason { get; set; }

    /// <summary>Free-form device/source hint supplied by the client (scanner id, app version).</summary>
    public string? DeviceInfo { get; set; }
}
