using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Tarification.Entities;

namespace TransportationService.Api.Modules.Orders.Entities;

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
    /// shown separately as a proposal until confirmed.
    /// </summary>
    public bool Proposed { get; set; }
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

    /// <summary>Entered quantity for per-hour / per-stop services.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Frozen effective invoice description (customer override > global > name).</summary>
    public string? InvoiceDescriptionSnapshot { get; set; }
}
