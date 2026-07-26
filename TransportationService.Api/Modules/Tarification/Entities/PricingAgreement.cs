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

    /// <summary>Cap on the agreement subtotal per order, applied after the minimum top-up.</summary>
    public decimal? MaximumAmount { get; set; }

    /// <summary>
    /// True = a reusable rate table (requires CustomerId == null). Unlike the plain company-wide
    /// default (CustomerId null, IsShared false, applies to everyone automatically), a shared
    /// table prices nothing on its own — it only applies to a customer that has an active
    /// <see cref="PricingAgreementAssignment"/> covering the tariff date.
    /// </summary>
    public bool IsShared { get; set; }

    /// <summary>Optional internal commercial background (e.g. why this customer prices lower).</summary>
    public string? Notes { get; set; }

    /// <summary>Set when this agreement was converted from a legacy rate card (idempotency marker).</summary>
    public Guid? LegacyRateCardId { get; set; }

    /// <summary>
    /// Set => this is a DERIVED table: it has no rules of its own and instead reuses the rules of
    /// its base-chain root (spec §9, "NL = BE +30%"), stacking its own <see cref="Modifiers"/> on
    /// top. Self-referencing FK is Restrict — a base table with dependents cannot be deleted.
    /// </summary>
    public Guid? BaseAgreementId { get; set; }
    public PricingAgreement? BaseAgreement { get; set; }

    public List<PricingAgreementSurcharge> Surcharges { get; set; } = new();
    public List<PricingAgreementAssignment> Assignments { get; set; } = new();
    public List<PricingAgreementModifier> Modifiers { get; set; } = new();
}

/// <summary>Automatic surcharge on the agreement subtotal (Percent) or per order (Fixed).</summary>
public class PricingAgreementSurcharge : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SurchargeKind Kind { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// Links a shared (<see cref="PricingAgreement.IsShared"/>) agreement to a customer for a period,
/// with an optional commercial adjustment on top of the table's own rule amounts. A shared
/// agreement never prices an order for a customer without a matching, date-active assignment.
/// </summary>
public class PricingAgreementAssignment : AuditableTenantEntity
{
    public Guid AgreementId { get; set; }
    public PricingAgreement? Agreement { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>e.g. -5 = 5% korting op de lijnen van deze tabel.</summary>
    public decimal? PercentAdjustment { get; set; }

    /// <summary>Vast bedrag per order bovenop de tabel.</summary>
    public decimal? FixedAdjustment { get; set; }

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// One stacking step of a derived agreement's own markup/discount on top of its base-chain
/// root's rule lines — e.g. "Nederland +30%" (country-conditioned) or "Waddeneilanden +€75"
/// (zone-conditioned). Applied ascending <see cref="Sequence"/> on the RUNNING subtotal (base
/// lines + previously applied modifiers), so modifiers stack.
/// </summary>
public class PricingAgreementModifier : AuditableTenantEntity
{
    /// <summary>The derived agreement this modifier belongs to.</summary>
    public Guid AgreementId { get; set; }

    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional condition: delivery country (2-letter, uppercase). Null = always matches.</summary>
    public string? CountryCode { get; set; }

    /// <summary>Optional condition: resolved delivery zone. Null = always matches.</summary>
    public Guid? ZoneId { get; set; }

    /// <summary>Exactly one of Percent/FixedAmount is set — kept explainable as a single line.</summary>
    public decimal? Percent { get; set; }
    public decimal? FixedAmount { get; set; }
}
