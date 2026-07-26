using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Tarification.Entities;

/// <summary>
/// A customer rate card: the agreed pricing structure valid within an effective-date window.
/// Quotes are computed from the card that is effective on the requested date; overlapping
/// windows per customer are refused so that lookup is always unambiguous.
/// </summary>
public class RateCard : AuditableTenantEntity
{
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "EUR";

    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Inclusive end date; null = open-ended.</summary>
    public DateOnly? EffectiveUntil { get; set; }

    // Price components; only configured components contribute to a quote.
    public decimal BaseAmount { get; set; }
    public decimal? PerKmRate { get; set; }
    public decimal? PerPalletRate { get; set; }
    public decimal? PerTonRate { get; set; }

    /// <summary>Floor applied after surcharges; the explanation shows when it kicks in.</summary>
    public decimal? MinimumAmount { get; set; }

    public string? Notes { get; set; }

    public List<RateSurcharge> Surcharges { get; set; } = [];
}

public enum SurchargeKind
{
    /// <summary>Percentage of the applicable base subtotal.</summary>
    Percent,

    /// <summary>Flat amount per order.</summary>
    Fixed,

    /// <summary>Amount × entered hours (e.g. Wachttijd €45/uur). Service options only.</summary>
    PerHour,

    /// <summary>Amount × number of (extra) stops (e.g. Extra stop €20). Service options only.</summary>
    PerStop,

    /// <summary>Amount × quantity of a specific managed unit on the order (e.g. Picking €1,25/colli,
    /// PAL UIT €4,50/pallet). Service options only; requires ServiceOption.UnitTypeId.</summary>
    PerUnit = 4,

    /// <summary>Amount × number of (non-deleted) cargo/order lines (e.g. Administratie €3/orderlijn). Service options only.</summary>
    PerOrderLine = 5,

    /// <summary>Amount × the order's total weight in kg. Service options only.</summary>
    PerKg = 6,

    /// <summary>Amount × the order's total volume in m³. Service options only.</summary>
    PerM3 = 7,

    /// <summary>Amount × the order's loading meters. Service options only.</summary>
    PerLdm = 8,

    /// <summary>Amount × an entered number of days (e.g. opslag per dag). Service options only.</summary>
    PerDay = 9,

    /// <summary>Amount × an entered number of pallet-days (e.g. palletopslag). Service options only.</summary>
    PerPalletDay = 10,
}

/// <summary>A named surcharge on a rate card (e.g. fuel %, ADR fixed fee).</summary>
public class RateSurcharge : AuditableTenantEntity
{
    public Guid RateCardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public SurchargeKind Kind { get; set; }
    /// <summary>Percent (of the subtotal) for Percent, amount for Fixed.</summary>
    public decimal Value { get; set; }
}
