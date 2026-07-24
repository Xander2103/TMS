using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

public enum PriceRuleBasis
{
    /// <summary>Price = UnitPrice × quantity.</summary>
    PerUnit,

    /// <summary>Price from the bracket containing the quantity (1 pallet = €50, 2 = €85, ...).</summary>
    QuantityBracket,

    /// <summary>Price from the bracket containing the order weight (kg).</summary>
    WeightBracket,

    /// <summary>Price = UnitPrice × hours (the line quantity is interpreted as hours).</summary>
    Hourly,

    /// <summary>Flat amount per order, independent of quantity.</summary>
    Fixed,
}

/// <summary>
/// A parameterized price agreement: for a customer (or company-wide when CustomerId is null),
/// a unit, an optional delivery zone and an effective window. The order pricing engine picks
/// the most specific active rule (customer+zone → customer → company+zone → company).
/// </summary>
public class PriceRule : AuditableTenantEntity
{
    /// <summary>Null = company-wide default rule.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Null allowed only for Fixed rules (order-level flat price).</summary>
    public Guid? UnitTypeId { get; set; }

    public PriceRuleBasis Basis { get; set; }

    /// <summary>Optional zone dimension; null = applies to every destination.</summary>
    public Guid? ZoneId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>PerUnit rate / Hourly rate / Fixed amount; unused for bracket bases.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal? MinimumAmount { get; set; }

    public List<PriceRuleBracket> Brackets { get; set; } = new();
}

/// <summary>One bracket of a Quantity/Weight rule. ToQuantity null = open-ended.</summary>
public class PriceRuleBracket : AuditableTenantEntity
{
    public Guid PriceRuleId { get; set; }
    public decimal FromQuantity { get; set; }
    public decimal? ToQuantity { get; set; }

    /// <summary>Bracket price for quantities in [From, To].</summary>
    public decimal Price { get; set; }

    /// <summary>Open-ended brackets: extra amount per unit above FromQuantity.</summary>
    public decimal? PricePerExtraUnit { get; set; }
}
