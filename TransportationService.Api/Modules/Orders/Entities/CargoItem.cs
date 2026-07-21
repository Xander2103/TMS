using TransportationService.Api.Common.Abstractions;
using TransportationService.Api.Modules.Packages.Entities;

namespace TransportationService.Api.Modules.Orders.Entities;

/// <summary>
/// One structured cargo line of an order: typed packaging, quantities, weights, dimensions
/// (with derived-or-manual volume, same rule as the fleet), ADR/stackable flags and the
/// loading/unloading stop it travels between. The barcode is unique within the order when
/// present (an ambiguous scan would be unresolvable); across orders the same barcode may
/// recur (EAN/SSCC reuse) — scans resolve within the trip context. This model feeds package
/// generation, scanning, capacity checks, POD and pricing.
/// </summary>
public class CargoItem : AuditableTenantEntity
{
    public Guid TransportOrderId { get; set; }

    public int Sequence { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public decimal ExpectedQuantity { get; set; } = 1;

    public string? QuantityUnit { get; set; }

    /// <summary>Managed unit-type code (from the UnitType lookup); QuantityUnit is the legacy free-text fallback.</summary>
    public string? QuantityUnitCode { get; set; }

    public string? Notes { get; set; }

    /// <summary>Packaging type; shares the package module's classification so generated packages inherit it 1:1.</summary>
    public PackageUnitType? UnitType { get; set; }

    /// <summary>Free-text packaging label when <see cref="UnitType"/> is Other.</summary>
    public string? UnitTypeLabel { get; set; }

    public decimal? TotalWeightKg { get; set; }
    public decimal? WeightPerUnitKg { get; set; }

    // Dimensions are PER UNIT; volume follows the shared derived-or-manual rule.
    public decimal? LengthMeters { get; set; }
    public decimal? WidthMeters { get; set; }
    public decimal? HeightMeters { get; set; }
    public decimal? VolumeM3 { get; set; }
    public bool VolumeIsManual { get; set; }

    public bool AdrRequired { get; set; }
    public string? AdrDetails { get; set; }

    public bool Stackable { get; set; } = true;

    /// <summary>Line-level reference (SSCC range, customer line number, ...).</summary>
    public string? Reference { get; set; }

    /// <summary>The stops this line travels between; resolved from the request's stop list on every save.</summary>
    public Guid? LoadingStopId { get; set; }
    public Guid? UnloadingStopId { get; set; }
}
