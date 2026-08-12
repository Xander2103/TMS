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

    /// <summary>Order-measure rule: BaseAmount + UnitPrice × order distance (km). UnitTypeId null.</summary>
    PerKm,

    /// <summary>Order-measure rule: UnitPrice × order pallet count. UnitTypeId null.</summary>
    PerPallet,

    /// <summary>Order-measure rule: UnitPrice × order weight in tonnes (WeightKg / 1000). UnitTypeId null.</summary>
    PerTon,

    /// <summary>Order-measure rule: BaseAmount + UnitPrice × loading meters. UnitTypeId null.</summary>
    PerLoadingMeter,

    /// <summary>Order-measure rule: BaseAmount + UnitPrice × order volume (m³). UnitTypeId null.</summary>
    PerVolume,

    /// <summary>
    /// Order-measure rule over the number of stops: linear (UnitPrice × stops) or progressive
    /// via brackets (1e stop €65, 2e €40, volgende €30). UnitTypeId null.
    /// </summary>
    PerStop,
}

/// <summary>
/// How a QuantityBracket rule turns brackets into an amount: Absolute takes the single bracket
/// price that contains the quantity (existing behaviour); PerNextUnit sums the price of the
/// bracket containing each unit index 1..qty (progressive per-piece pricing, e.g. "1e stuk €60,
/// 2e €55, 3e €50, 4e en verder €45").
/// </summary>
public enum BracketSelectionMode
{
    Absolute = 0,
    PerNextUnit = 1,
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

    /// <summary>
    /// Sales code for lines this rule produces (Wave 2); null falls back to the agreement's
    /// code, then the system-role defaults at invoicing. Resolution is frozen on the line.
    /// </summary>
    public Guid? SalesCategoryId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>PerUnit rate / Hourly rate / Fixed amount; unused for bracket bases.</summary>
    public decimal? UnitPrice { get; set; }

    public decimal? MinimumAmount { get; set; }

    /// <summary>Caps the computed amount AFTER the MinimumAmount floor is applied.</summary>
    public decimal? MaximumAmount { get; set; }

    /// <summary>QuantityBracket only: how the brackets combine into an amount.</summary>
    public BracketSelectionMode BracketMode { get; set; } = BracketSelectionMode.Absolute;

    /// <summary>Optional grouping into a named commercial agreement (rate card).</summary>
    public Guid? AgreementId { get; set; }
    public PricingAgreement? Agreement { get; set; }

    /// <summary>Explicit tie-breaker between equally specific rules; higher wins. Default 0.</summary>
    public int Priority { get; set; }

    /// <summary>Added on top of the computed amount (e.g. base cost before the per-km price).</summary>
    public decimal? BaseAmount { get; set; }

    /// <summary>Hourly: minimum billable quantity (e.g. minimum 3 uur).</summary>
    public decimal? MinimumQuantity { get; set; }

    /// <summary>Hourly: quantity rounds UP to this step (e.g. 0.25 = per started 15 minutes).</summary>
    public decimal? QuantityRoundingStep { get; set; }

    // Billable-quantity contract (spec ch. 11): an item exceeding a threshold counts as
    // OversizeBillableFactor billable units. The physical order quantity never changes.
    public decimal? OversizeLengthCm { get; set; }
    public decimal? OversizeWidthCm { get; set; }
    public decimal? OversizeBillableFactor { get; set; }

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

    // Multidimensional carrier-table caps (spec: "kg tot / cbm tot / ldm tot / prijs"). A filled
    // cap only matches when the order's own measure is known AND within the cap; null = no
    // constraint on that dimension. See PricingEngine.BracketAmount for the matching/tightness rules.
    public decimal? WeightToKg { get; set; }
    public decimal? VolumeToM3 { get; set; }
    public decimal? LoadingMetersTo { get; set; }
}
