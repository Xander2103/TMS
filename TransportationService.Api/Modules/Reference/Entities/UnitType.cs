using TransportationService.Api.Common.Lookups;

namespace TransportationService.Api.Modules.Reference.Entities;

/// <summary>Broad classification of a unit, used for grouping/filtering in master data.</summary>
public enum UnitCategory
{
    Other = 0,
    Packaging = 1,
    Weight = 2,
    Volume = 3,
    Capacity = 4,
    Time = 5,
    Distance = 6,
    Commercial = 7,
}

/// <summary>How the unit's default dimensions behave during order entry.</summary>
public enum UnitDimensionBehavior
{
    /// <summary>No defaults are proposed; dimensions are entered per order line when needed.</summary>
    Variable = 0,

    /// <summary>Defaults are pre-filled but the planner may override them per order line.</summary>
    DefaultButOverridable = 1,

    /// <summary>Dimensions are fixed by the unit definition and not editable per order line.</summary>
    Fixed = 2,
}

/// <summary>
/// A managed unit of measure for transport-order quantities (Colli, Europallet, Laadmeter,
/// Kilogram, ...). Tenant-editable lookup with a stable Code and a translated display Name,
/// so historical free-text values can be mapped onto a code without breaking.
/// Physical defaults (dimensions, weight, volume, loading meters, pallet places) live here —
/// business logic must read them from this record, never hardcode them per unit name.
/// The Code is user-editable and is never regenerated when the name changes.
/// </summary>
public class UnitType : LookupEntity
{
    /// <summary>Selectable as "Eenheid" during order entry.</summary>
    public bool AllowForOrderEntry { get; set; } = true;

    /// <summary>Usable as the unit of a price rule / customer price agreement.</summary>
    public bool AllowForPricing { get; set; } = true;

    public UnitCategory Category { get; set; } = UnitCategory.Other;

    /// <summary>Number of decimals allowed for order-entry quantities (0 = whole units).</summary>
    public int Decimals { get; set; }

    /// <summary>Optional display symbol/abbreviation (e.g. "kg", "ldm"); purely presentational.</summary>
    public string? Symbol { get; set; }

    public UnitDimensionBehavior DimensionBehavior { get; set; } = UnitDimensionBehavior.Variable;

    public decimal? DefaultLengthCm { get; set; }
    public decimal? DefaultWidthCm { get; set; }
    public decimal? DefaultHeightCm { get; set; }
    public decimal? DefaultWeightKg { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public decimal? DefaultVolumeM3 { get; set; }
    public decimal? DefaultLoadingMeters { get; set; }
    public decimal? DefaultPalletPlaces { get; set; }
}
