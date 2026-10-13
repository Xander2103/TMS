using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// A customer-specific price for ONE bracket row of a shared/company-wide bracket rule
/// ("Klantafwijking"). The override targets a row by its exact identity — FromQuantity,
/// ToQuantity and the optional dimension caps — on the rule it belongs to. During calculation
/// the engine first resolves the winning shared rule and its matching bracket row as usual;
/// only then, when that rule is not customer-private, a date-valid override for the order's
/// customer replaces that single row's price. Every non-overridden row keeps falling back to
/// the shared bracket, so a customer never needs a full copy of the table for one deviating row.
/// Row identity is by VALUE, not by bracket id: rule edits and Excel imports replace bracket
/// rows wholesale, so ids don't survive — an override whose row no longer exists simply stops
/// matching (and the admin UI shows it as orphaned).
/// </summary>
public class PriceRuleBracketOverride : AuditableTenantEntity
{
    public Guid PriceRuleId { get; set; }
    public PriceRule? PriceRule { get; set; }

    public Guid CustomerId { get; set; }

    // --- Row identity (must equal the shared bracket row's values exactly) ---
    public decimal FromQuantity { get; set; }
    public decimal? ToQuantity { get; set; }
    public decimal? WeightToKg { get; set; }
    public decimal? VolumeToM3 { get; set; }
    public decimal? LoadingMetersTo { get; set; }

    /// <summary>The customer's price for this row (replaces the shared row's Price).</summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Optional replacement for the open-ended row's per-extra-unit price. Null keeps the
    /// shared row's PricePerExtraUnit — most overrides only change the base row price.
    /// </summary>
    public decimal? PricePerExtraUnit { get; set; }

    /// <summary>Optional validity window; null bounds follow the rule's own window.</summary>
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }

    public string? Notes { get; set; }
}
