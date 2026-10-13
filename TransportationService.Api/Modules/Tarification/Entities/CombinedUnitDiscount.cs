using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>How the order's cargo is grouped before computing a combined-unit discount's equivalent quantity.</summary>
public enum DegressionScope
{
    /// <summary>All units on the order combine into a single evaluation.</summary>
    Order = 0,

    /// <summary>Units combine per distinct delivery address (stops sharing the same address merge).</summary>
    DeliveryAddress = 1,

    /// <summary>Units combine per individual unloading stop — never merged, even at the same address.</summary>
    Stop = 2,
}

/// <summary>
/// Spec §29-31: a volume discount that combines DIFFERENT unit types into one weighted "equivalent"
/// count (e.g. "1 europallet + 1 blokpallet + 2 colli, each weight 1 = 4 eenheden → -8%"), evaluated
/// per <see cref="Scope"/> group so quantities of different delivery addresses are never mixed. Most
/// specific configuration wins (customer+agreement &gt; customer &gt; agreement &gt; company-wide);
/// an exact tie at the same specificity blocks pricing (see PricingEngine).
/// </summary>
public class CombinedUnitDiscount : AuditableTenantEntity
{
    /// <summary>Null = company-wide.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Optional: this discount only fires when this agreement is actually engaged by the order.</summary>
    public Guid? AgreementId { get; set; }

    public string Name { get; set; } = string.Empty;
    public DegressionScope Scope { get; set; } = DegressionScope.DeliveryAddress;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;

    public List<CombinedUnitDiscountUnit> Units { get; set; } = new();
    public List<CombinedUnitDiscountTier> Tiers { get; set; } = new();
}

/// <summary>One unit type counted toward the discount's equivalent quantity, weighted by <see cref="EquivalentFactor"/>.</summary>
public class CombinedUnitDiscountUnit : AuditableTenantEntity
{
    public Guid DiscountId { get; set; }
    public Guid UnitTypeId { get; set; }

    /// <summary>Weight of one physical unit toward the equivalent count (e.g. europallet = 1, colli = 0.25).</summary>
    public decimal EquivalentFactor { get; set; } = 1m;
}

/// <summary>One staffel step: an equivalent-quantity range mapped to a discount percentage.</summary>
public class CombinedUnitDiscountTier : AuditableTenantEntity
{
    public Guid DiscountId { get; set; }
    public decimal FromCount { get; set; }

    /// <summary>Null = open-ended (no upper bound).</summary>
    public decimal? ToCount { get; set; }

    /// <summary>Positive percentage, applied as a discount (subtracted from the eligible base).</summary>
    public decimal Percent { get; set; }
}
