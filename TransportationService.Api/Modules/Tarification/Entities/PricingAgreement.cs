using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// A named commercial rate card grouping the price rules a customer (or the company default,
/// when CustomerId is null) agreed on for a period — e.g. "Distributie België 2026-Q4".
/// Versioning happens at rule level via effective windows; the agreement gives the rules a
/// commercial identity, an optional order-level minimum and automatic surcharges.
/// </summary>
public class PricingAgreement : AuditableTenantEntity
{
    /// <summary>Null = company-wide default agreement.</summary>
    public Guid? CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Minimum applied to the subtotal of this agreement's rule amounts per order.</summary>
    public decimal? MinimumAmount { get; set; }

    /// <summary>Optional internal commercial background (e.g. why this customer prices lower).</summary>
    public string? Notes { get; set; }

    /// <summary>Set when this agreement was converted from a legacy rate card (idempotency marker).</summary>
    public Guid? LegacyRateCardId { get; set; }

    public List<PricingAgreementSurcharge> Surcharges { get; set; } = new();
}

/// <summary>Automatic surcharge on the agreement subtotal (Percent) or per order (Fixed).</summary>
public class PricingAgreementSurcharge : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SurchargeKind Kind { get; set; }
    public decimal Value { get; set; }
}
