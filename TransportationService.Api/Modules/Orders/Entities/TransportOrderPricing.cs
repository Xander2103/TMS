using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Orders.Entities;

/// <summary>
/// Manual-editing lifecycle of one pricing line (spec ch. 24-26). Auto/Proposed are engine-owned
/// and get overwritten wholesale on recalculation; AutoAdjusted and Manual are user-owned and
/// survive a recalculation (merge, never delete-all-rewrite). Stored as a string column.
/// </summary>
public enum OrderPriceLineKind
{
    /// <summary>Engine-produced, never edited; replaced wholesale on every recalculation.</summary>
    Auto = 0,

    /// <summary>Started as an engine line, then manually corrected; Original* preserves the engine baseline.</summary>
    AutoAdjusted = 1,

    /// <summary>Free line added by a user (or an orphaned AutoAdjusted line whose engine source disappeared).</summary>
    Manual = 2,

    /// <summary>Unconfirmed engine proposal (spec Phase 6, e.g. extra time); excluded from LinesTotal until confirmed.</summary>
    Proposed = 3,
}

/// <summary>
/// Snapshot of one line of the price calculation at save time. Historical orders keep this
/// breakdown even when master-data tariffs change later; it is only rewritten on an explicit
/// order save.
/// </summary>
public class TransportOrderPricingLine : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }
    public int Sequence { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    /// <summary>Where the amount came from (rule name, "Klantprijs", "Tarievenkaart: ...").</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Informational lines (diesel) are shown but excluded from the stored totals.</summary>
    public bool Informational { get; set; }

    /// <summary>Frozen identity of the tariff rule that produced this line, when applicable.</summary>
    public string? RuleName { get; set; }

    /// <summary>Frozen name of the pricing agreement (rate card) the rule belonged to.</summary>
    public string? AgreementName { get; set; }

    /// <summary>Physically transported quantity for this line.</summary>
    public decimal? ActualQuantity { get; set; }

    /// <summary>Commercially billed quantity (spec ch. 11); differs on oversize contracts.</summary>
    public decimal? BillableQuantity { get; set; }

    /// <summary>
    /// An unconfirmed extra-time charge (spec Phase 6): excluded from AgreedPrice/CalculatedPrice,
    /// shown separately as a proposal until confirmed. DUPLICATES <see cref="Kind"/> == Proposed —
    /// kept only for Phase 6 DTO/consumer compatibility. <see cref="Kind"/> is the single
    /// authoritative source of truth (see <c>OrderPriceLineKind</c>); this flag is always derived
    /// from it and must equal exactly (Kind == Proposed). Every write path that changes Kind uses
    /// TransportOrderService.SetKind (or otherwise updates this flag in the same assignment) so the
    /// two never drift — never set this independently of Kind.
    /// </summary>
    public bool Proposed { get; set; }

    /// <summary>Manual-editing lifecycle (spec ch. 24-26); see <see cref="OrderPriceLineKind"/>.</summary>
    public OrderPriceLineKind Kind { get; set; } = OrderPriceLineKind.Auto;

    /// <summary>Current quantity, when the line is a simple qty × unit-price line (editable).</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Current unit price, when derivable/editable (never invented for bracket/base-amount lines).</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>Managed unit code for the line's Quantity (e.g. "COLLI", "EUROPALLET"), editable on manual lines.</summary>
    public string? Unit { get; set; }

    /// <summary>Quantity as last produced/refreshed by the engine, preserved once a line is adjusted.</summary>
    public decimal? OriginalQuantity { get; set; }

    /// <summary>Unit price as last produced/refreshed by the engine, preserved once a line is adjusted.</summary>
    public decimal? OriginalUnitPrice { get; set; }

    /// <summary>Amount as last produced/refreshed by the engine, preserved once a line is adjusted.</summary>
    public decimal? OriginalAmount { get; set; }

    /// <summary>Mandatory reason for a manual adjustment/removal (audit trail, spec §24).</summary>
    public string? AdjustReason { get; set; }

    public Guid? AdjustedByUserId { get; set; }
    public DateTime? AdjustedAtUtc { get; set; }

    /// <summary>Frozen identity of the tariff rule, for merge-matching (see LineKey too).</summary>
    public Guid? RuleId { get; set; }

    /// <summary>Frozen identity of the service option, for merge-matching (see LineKey too).</summary>
    public Guid? ServiceOptionId { get; set; }

    /// <summary>
    /// Stable merge key stamped by the engine (or "manual:{guid}" for free lines): the single
    /// source of truth used to match an existing adjusted/manual line against a fresh
    /// recalculation instead of delete-all-rewrite (spec ch. 24-26).
    /// </summary>
    public string? LineKey { get; set; }
}

/// <summary>
/// Status lifecycle of an order's price (spec ch. 24-26): Draft recalculates freely on every
/// save; Reviewed still recalculates (the frontend warns) but marks the price as checked;
/// Locked/Invoiced refuse recalculation entirely until explicitly unlocked. Stored as a string
/// column. Invoiced is set only by invoice generation, never via the status endpoint.
/// </summary>
public enum OrderPricingStatus
{
    Draft = 0,
    Reviewed = 1,
    Locked = 2,
    Invoiced = 3,
}

/// <summary>
/// One-per-order header of the pricing snapshot (spec ch. 21): the frozen context of the
/// calculation — tariff date, zone, agreements, totals, override audit and a human-readable
/// explanation. Rewritten only on an explicit order save; later tariff changes never touch it.
/// </summary>
public class TransportOrderPricingSnapshot : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }
    public DateOnly TariffDate { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? ZoneCode { get; set; }
    public string? ZoneName { get; set; }

    /// <summary>"; "-joined names of the pricing agreements that contributed.</summary>
    public string? AgreementNames { get; set; }

    /// <summary>E.g. "3 × Europallet (factureerbaar: 4)".</summary>
    public string? UnitSummary { get; set; }

    public decimal? CalculatedTotal { get; set; }
    public decimal? OverrideAmount { get; set; }
    public string? OverrideReason { get; set; }
    public Guid? OverriddenByUserId { get; set; }
    public DateTime? OverriddenAtUtc { get; set; }

    /// <summary>Human-readable multiline calculation explanation.</summary>
    public string? Explanation { get; set; }

    /// <summary>Manual-editing / locking lifecycle (spec ch. 24-26); preserved across recalculations.</summary>
    public OrderPricingStatus Status { get; set; } = OrderPricingStatus.Draft;

    /// <summary>Sum of Auto/AutoAdjusted/Manual non-informational line amounts (drives AgreedPrice).</summary>
    public decimal? LinesTotal { get; set; }

    /// <summary>
    /// Wave 2026-08-04 §7: serialized per-goods-line pricing coverage
    /// (<c>OrderPricingCoverageDto[]</c>, camelCase JSON) frozen with the calculation.
    /// </summary>
    public string? CoverageJson { get; set; }

    /// <summary>Wave 2026-08-04 §8: when/by whom the price was confirmed (visible workflow "Bevestigd").</summary>
    public DateTime? ConfirmedAtUtc { get; set; }
    public Guid? ConfirmedByUserId { get; set; }

    /// <summary>Display-name snapshot of the confirmer ("Bevestigd op … door …").</summary>
    public string? ConfirmedByName { get; set; }

    /// <summary>
    /// Wave 2026-08-04 §10: reason given when the price was confirmed DESPITE unpriced goods
    /// (authorized override). Non-null keeps a visible warning attached to the confirmed price.
    /// </summary>
    public string? ConfirmedWithUnpricedGoodsReason { get; set; }
}

/// <summary>
/// A selected delivery service/supplement snapshotted on the order; becomes a separate
/// invoice line. Name/kind/value are frozen so later option changes never rewrite history.
/// </summary>
public class TransportOrderServiceLine : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }
    public Guid? ServiceOptionId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public SurchargeKind Kind { get; set; }
    public decimal Value { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Entered/derived billable quantity for per-hour / per-stop / per-day / per-pallet-day services.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Per-pallet-day input: pallet count. Quantity = pallets × days unless manually corrected.</summary>
    public decimal? PalletCount { get; set; }

    /// <summary>Per-day / per-pallet-day input: number of days.</summary>
    public decimal? DayCount { get; set; }

    /// <summary>Frozen effective invoice description (customer override > global > name).</summary>
    public string? InvoiceDescriptionSnapshot { get; set; }

    /// <summary>Optional free-text note the user attaches to a manually selected service (e.g. "Afgesproken met klant").</summary>
    public string? Note { get; set; }
}
