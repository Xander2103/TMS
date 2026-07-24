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
}
