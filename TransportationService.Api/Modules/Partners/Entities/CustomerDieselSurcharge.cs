using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Partners.Entities;

/// <summary>What the diesel-surcharge percentage is applied to.</summary>
public enum DieselSurchargeBasis
{
    /// <summary>Per order: the order's (invoiced) freight amount.</summary>
    OrderAmount,

    /// <summary>The invoice subtotal of all order-backed lines.</summary>
    InvoiceSubtotal,
}

/// <summary>How the surcharge appears on the invoice.</summary>
public enum DieselSurchargePresentation
{
    /// <summary>One surcharge line per transport order.</summary>
    PerOrderLine,

    /// <summary>A single aggregated surcharge line (also covers dossier-level invoicing).</summary>
    AggregatedLine,
}

public enum DieselSurchargeRounding
{
    NearestCent,
    RoundUpCent,
}

/// <summary>
/// Customer-level diesel-surcharge configuration (the default; transport orders may
/// override the percentage with a mandatory reason). Calculation is a restricted,
/// documented strategy — percentage over a defined basis — never a free-form formula
/// engine; <see cref="FormulaDescription"/> is explanatory text for the customer's own
/// agreed formula, which maps onto this strategy (or a future one) at configuration time.
/// </summary>
public class CustomerDieselSurcharge : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Surcharge percentage (e.g. 8.50 = 8,5%).</summary>
    public decimal Percent { get; set; }

    public DieselSurchargeBasis Basis { get; set; } = DieselSurchargeBasis.OrderAmount;

    public DieselSurchargePresentation Presentation { get; set; } = DieselSurchargePresentation.PerOrderLine;

    public DieselSurchargeRounding Rounding { get; set; } = DieselSurchargeRounding.NearestCent;

    /// <summary>Human explanation of the agreed formula/index (documentation, not executed).</summary>
    public string? FormulaDescription { get; set; }

    /// <summary>Optional validity window; outside it the surcharge is not applied.</summary>
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
}
