using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

public enum TransportOrderStatus
{
    Draft,
    Confirmed,
    Planned,
    InProgress,
    Completed,
    Cancelled,
}

public enum StopType
{
    Loading,
    Unloading,
}

/// <summary>
/// A customer transport order: what has to be moved, for whom, via which stops. Planning
/// (trip assignment, Phase 6) and pricing/invoicing (Phase 8) build on top of this entity.
/// The order number is claimed from TenantSettings via the retry-safe numbering helper.
/// </summary>
public class TransportOrder : AuditableTenantEntity
{
    public string OrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    /// <summary>The customer's own reference for this order (PO number, booking id, ...).</summary>
    public string? CustomerReference { get; set; }

    public DateOnly OrderDate { get; set; }

    public TransportOrderStatus Status { get; set; } = TransportOrderStatus.Draft;

    public string GoodsDescription { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? QuantityUnit { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public int? PalletCount { get; set; }
    public bool AdrRequired { get; set; }
    public bool CraneRequired { get; set; }

    /// <summary>Simple agreed price; the real pricing/invoicing engine is Phase 8.</summary>
    public decimal? AgreedPrice { get; set; }

    public string? Notes { get; set; }

    public List<TransportOrderStop> Stops { get; set; } = [];
}

/// <summary>
/// One loading or unloading stop of an order, ordered by <see cref="Sequence"/>. Either a master
/// location is referenced or a free address is entered inline (ad-hoc addresses without master data).
/// </summary>
public class TransportOrderStop : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }

    public int Sequence { get; set; }

    public StopType StopType { get; set; }

    public Guid? LocationId { get; set; }

    // Inline fallback when no master location is linked.
    public string? LocationName { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }

    public DateTime? PlannedFrom { get; set; }
    public DateTime? PlannedTo { get; set; }

    /// <summary>Stop-level reference (dossier, container number, booking slot, ...).</summary>
    public string? Reference { get; set; }

    public string? Instructions { get; set; }
}
