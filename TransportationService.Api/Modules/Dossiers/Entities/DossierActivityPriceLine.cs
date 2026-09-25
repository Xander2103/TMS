using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Dossiers.Entities;

/// <summary>
/// D5 (master sprint 2026-09-21): one sales line of a STANDALONE billable activity (hours ×
/// rate, a fixed item, a surcharge as its own line). The activity-side counterpart of a manual
/// order pricing line — there is no engine behind it. <see cref="Amount"/> is always computed
/// by the server as round(<see cref="Quantity"/> × <see cref="UnitPrice"/>, 2); the sum of the
/// lines is stored once on <see cref="DossierActivityPricing.AgreedPrice"/>, which is the only
/// figure the dossier total reads.
/// </summary>
public class DossierActivityPriceLine : AuditableTenantEntity
{
    public Guid DossierActivityPricingId { get; set; }

    public int Sequence { get; set; }

    public string Label { get; set; } = string.Empty;

    /// <summary>Always &gt; 0.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Free unit text or managed unit code (e.g. "uur"); informational, never priced on.</summary>
    public string? Unit { get; set; }

    /// <summary>Always ≥ 0 (a line at € 0 is allowed; a discount is not modelled here).</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>round(Quantity × UnitPrice, 2) — server-computed, a client-sent amount is ignored.</summary>
    public decimal Amount { get; set; }

    /// <summary>Optional sales code (own-tenant, validated with TenantReferenceGuard). No FK, like the order lines.</summary>
    public Guid? SalesCategoryId { get; set; }
}
