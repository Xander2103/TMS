using System.ComponentModel.DataAnnotations.Schema;
using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Orders.Entities;

public enum TransportOrderStatus
{
    Draft,
    Confirmed,
    Planned,
    InProgress,
    Completed,
    /// <summary>Set by the invoicing module (Phase 8); not reachable via the manual transition map.</summary>
    Invoiced,
    Cancelled,
    /// <summary>Submitted through the customer portal; awaiting planner review. Stored as string — appending is safe.</summary>
    Submitted,
}

public enum StopType
{
    Loading,
    Unloading,
}

/// <summary>Operational urgency for planning and dock queues. Stored as string — appending is safe.</summary>
public enum OrderPriority
{
    Low,
    Normal,
    High,
    Urgent,
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

    public OrderPriority Priority { get; set; } = OrderPriority.Normal;

    /// <summary>
    /// Transient context for the status-history interceptor: reason and correction marker of
    /// the status change staged in the current SaveChanges. Cleared once recorded.
    /// </summary>
    [NotMapped]
    public string? PendingStatusChangeReason { get; set; }

    [NotMapped]
    public bool PendingStatusChangeIsCorrection { get; set; }

    /// <summary>Optional free-text description of the goods (structured data lives in cargo items).</summary>
    public string? GoodsDescription { get; set; }
    public decimal? Quantity { get; set; }
    public string? QuantityUnit { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public int? PalletCount { get; set; }
    public bool AdrRequired { get; set; }
    public bool CraneRequired { get; set; }

    /// <summary>Simple agreed price; the real pricing/invoicing engine is Phase 8.</summary>
    public decimal? AgreedPrice { get; set; }

    // Diesel-surcharge override (customer config is the default). Overriding requires a
    // reason; the inherited value, override, actor and timestamp are audited.
    public bool DieselSurchargeOverride { get; set; }
    public decimal? DieselSurchargePercentOverride { get; set; }
    public string? DieselSurchargeOverrideReason { get; set; }

    public string? Notes { get; set; }

    /// <summary>Mandatory when the order was cancelled; set only through the cancel action.</summary>
    public string? CancellationReason { get; set; }

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

    // Four distinct time concepts (Wave "stop execution"): what planning scheduled, what the
    // customer asked for, what was confirmed back to the customer, and the hard outer bounds.
    public DateTime? PlannedFrom { get; set; }
    public DateTime? PlannedTo { get; set; }
    public DateTime? RequestedFrom { get; set; }
    public DateTime? RequestedTo { get; set; }
    public DateTime? ConfirmedFrom { get; set; }
    public DateTime? ConfirmedTo { get; set; }
    public DateTime? EarliestAllowed { get; set; }
    public DateTime? LatestAllowed { get; set; }

    public bool AppointmentRequired { get; set; }
    public string? AppointmentReference { get; set; }

    /// <summary>Stop-level reference (dossier, container number, booking slot, ...).</summary>
    public string? Reference { get; set; }

    public string? Instructions { get; set; }

    // Stop-level overrides; when empty the master location's instructions apply.
    public string? AccessInstructions { get; set; }
    public string? LoadingInstructions { get; set; }
    public string? UnloadingInstructions { get; set; }
}
