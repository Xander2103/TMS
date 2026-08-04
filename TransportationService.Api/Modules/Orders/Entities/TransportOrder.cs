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
/// Where an order's price agreement comes from: the customer's contract (rules/agreements) or a
/// one-off price agreement carried on the order itself (spec Phase 6). Stored as string.
/// </summary>
public enum OrderPricingSource
{
    Contract,
    OneOff,
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

    /// <summary>
    /// Selling own company. Inherits the customer's default at creation; may be overridden
    /// per order. The invoice snapshot freezes the final entity data at invoicing time.
    /// </summary>
    public Guid? LegalEntityId { get; set; }

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

    /// <summary>Managed unit-type code (from the UnitType lookup); QuantityUnit is the legacy free-text fallback.</summary>
    public string? QuantityUnitCode { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public int? PalletCount { get; set; }
    public bool AdrRequired { get; set; }
    public bool CraneRequired { get; set; }

    /// <summary>
    /// The effective order price used by invoicing. Equal to <see cref="CalculatedPrice"/>
    /// unless a manual override was applied (<see cref="PriceIsManual"/>) or no pricing
    /// configuration exists (legacy manual entry).
    /// </summary>
    public decimal? AgreedPrice { get; set; }

    /// <summary>Engine result at last save; null when nothing could be calculated.</summary>
    public decimal? CalculatedPrice { get; set; }

    /// <summary>True when AgreedPrice was explicitly overridden (orders.override_price).</summary>
    public bool PriceIsManual { get; set; }

    public string? PriceOverrideReason { get; set; }

    /// <summary>
    /// Contract (default): priced by the customer's rules/agreements. OneOff: no contract is
    /// consulted — the order carries its own fixed price + included-time agreement below.
    /// Choosing OneOff and entering its fields needs NO special permission: it is the order's own
    /// price agreement, not an override of a calculated price. <c>orders.override_price</c>
    /// (<see cref="PriceIsManual"/>) is a SEPARATE, narrower gate that only applies when someone
    /// overwrites whatever the engine calculated (contract OR one-off) with a different number.
    /// </summary>
    public OrderPricingSource PricingSource { get; set; } = OrderPricingSource.Contract;

    /// <summary>OneOff only: the agreed fixed price for this single order.</summary>
    public decimal? OneOffFixedAmount { get; set; }

    /// <summary>OneOff only: included loading/unloading minutes, per activity. Mutually exclusive with the combined variant.</summary>
    public int? OneOffIncludedLoadingMinutes { get; set; }
    public int? OneOffIncludedUnloadingMinutes { get; set; }

    /// <summary>OneOff only: included loading+unloading minutes combined. Mutually exclusive with the per-activity fields.</summary>
    public int? OneOffIncludedCombinedMinutes { get; set; }

    /// <summary>OneOff only: hourly rate charged for time beyond the included allowance (proposal until confirmed).</summary>
    public decimal? OneOffExtraHourlyRate { get; set; }

    /// <summary>OneOff only: free-text context for how the one-off price was agreed (e.g. "Afgesproken via telefoon").</summary>
    public string? OneOffNotes { get; set; }

    /// <summary>
    /// Task 10: order-level overrides of the ENGAGED CONTRACT AGREEMENT's included loading/
    /// unloading time and extra-time rate/rounding/minimum. Contract pricing only — rejected in
    /// combination with <see cref="OrderPricingSource.OneOff"/> (which has its own included-time
    /// fields above). Resolution rule (order override wins over the agreement value, per activity;
    /// switching a combined-mode agreement to per-activity when only one side is overridden) lives
    /// in <c>PricingEngine.ComputeExtraTimeLines</c>. Stop-level overrides are a deferred limitation.
    /// </summary>
    public int? IncludedLoadingMinutesOverride { get; set; }
    public int? IncludedUnloadingMinutesOverride { get; set; }
    public decimal? ExtraTimeHourlyRateOverride { get; set; }
    public int? ExtraTimeRoundingStepMinutes { get; set; }
    public int? ExtraTimeMinimumBillableMinutes { get; set; }

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

/// <summary>Wave 2026-08-04 §15: simple per-stop time requirement kinds.</summary>
public enum StopTimeRequirementKind
{
    /// <summary>Geen specifieke eis.</summary>
    None = 0,

    /// <summary>Vóór een uur (leveren/laden vóór TimeRequirementTo).</summary>
    Before = 1,

    /// <summary>Na een uur (niet vóór TimeRequirementFrom).</summary>
    After = 2,

    /// <summary>Exact tijdvenster (TimeRequirementFrom – TimeRequirementTo).</summary>
    Window = 3,
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

    /// <summary>
    /// Wave 2026-08-04 §15: the user-friendly time requirement of this stop ("Leveren vóór
    /// 10:00"). Distinct from the four raw window pairs above: this is the COMMERCIAL promise
    /// that also drives time-based surcharges (§16). Before uses <see cref="TimeRequirementTo"/>,
    /// After uses <see cref="TimeRequirementFrom"/>, Window uses both. "Afspraak verplicht"
    /// remains the existing <see cref="AppointmentRequired"/> flag.
    /// </summary>
    public StopTimeRequirementKind TimeRequirement { get; set; } = StopTimeRequirementKind.None;

    /// <summary>Not before this time (After / start of Window).</summary>
    public TimeOnly? TimeRequirementFrom { get; set; }

    /// <summary>Before this time (Before / end of Window).</summary>
    public TimeOnly? TimeRequirementTo { get; set; }

    /// <summary>
    /// Wave 2026-08-04 §18: stop-level override of the included loading/unloading minutes for
    /// THIS stop's activity. Resolution: stop override → order override → contract value. When
    /// any stop of an activity carries an override, the activity's effective included minutes
    /// are the sum of the overriding stops' values (a stop without one contributes 0).
    /// </summary>
    public int? IncludedTimeMinutesOverride { get; set; }

    /// <summary>Stop-level reference (dossier, container number, booking slot, ...).</summary>
    public string? Reference { get; set; }

    public string? Instructions { get; set; }

    // Stop-level overrides; when empty the master location's instructions apply.
    public string? AccessInstructions { get; set; }
    public string? LoadingInstructions { get; set; }
    public string? UnloadingInstructions { get; set; }
}
