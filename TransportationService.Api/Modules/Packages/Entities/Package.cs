using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Packages.Entities;

public enum PackageUnitType
{
    Package,
    Parcel,
    Colli,
    Pallet,
    Crate,
    RollContainer,
    Container,
    Document,
    Other,
    // Stored as strings, so appending members is safe.
    EuroPallet,
    BlockPallet,
    Box,
    Drum,
}

public enum PackageLifecycleStatus
{
    Created,
    Labelled,
    AwaitingLoading,
    Loaded,
    InTransit,
    AtStop,
    Delivered,
    PartiallyDelivered,
    DeliveryFailed,
    Refused,
    Missing,
    Damaged,
    Cancelled,
    ReturnPending,
    ReturnLoaded,
    ReturnedToDepot,
    ReturnedToSender,
    RedeliveryPlanned,
    Quarantined,
}

public enum PackageExceptionState
{
    None,
    Open,
    Resolved,
}

/// <summary>
/// An individually tracked cargo unit (colli/pallet/parcel/...) with its own immutable
/// package number, barcode registry and server-controlled lifecycle. Quantity-only cargo
/// stays on the order's CargoItem lines — one order may mix both. The scan pipeline
/// resolves package barcodes FIRST and falls back to cargo lines, so there is exactly one
/// scanning system.
/// </summary>
public class Package : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }

    /// <summary>The cargo line this package was generated from (null for manually created packages).</summary>
    public Guid? CargoItemId { get; set; }

    /// <summary>Best-effort pins; stops are wholesale-replaced on order edits (FK SetNull),
    /// so a null pin falls back to the order's loading/unloading stops at scan time.</summary>
    public Guid? LoadingStopId { get; set; }
    public Guid? DeliveryStopId { get; set; }

    /// <summary>Immutable once claimed — relabelling never changes the number.</summary>
    public string PackageNumber { get; set; } = string.Empty;

    /// <summary>Convenience copy of the ACTIVE internal barcode (authoritative registry: package_barcodes).</summary>
    public string BarcodeValue { get; set; } = string.Empty;
    public PackageBarcodeType BarcodeType { get; set; } = PackageBarcodeType.Code128;

    /// <summary>Customer/carrier-supplied barcode, preserved alongside the internal one.</summary>
    public string? ExternalBarcode { get; set; }
    public string? ExternalPackageReference { get; set; }
    public string? CustomerReference { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1m;
    public PackageUnitType UnitType { get; set; } = PackageUnitType.Colli;
    /// <summary>Free-text unit label when <see cref="UnitType"/> is Other.</summary>
    public string? UnitTypeLabel { get; set; }

    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    /// <summary>The parent IS the pallet/roll container/group; children scan via the parent.</summary>
    public Guid? ParentPackageId { get; set; }

    public bool IsMandatory { get; set; } = true;
    public bool IsFragile { get; set; }
    public bool RequiresTemperatureControl { get; set; }
    public bool RequiresSignature { get; set; }

    public PackageLifecycleStatus CurrentLifecycleStatus { get; set; } = PackageLifecycleStatus.Created;

    /// <summary>
    /// Wave 4 §2: WHERE the package currently sits (projection — the append-only custody
    /// events stay the source of truth). FK-less snapshot id; null = unknown/on the road.
    /// </summary>
    public Guid? CurrentWarehouseLocationId { get; set; }
    public PackageExceptionState CurrentExceptionStatus { get; set; } = PackageExceptionState.None;

    public string? Notes { get; set; }
}
