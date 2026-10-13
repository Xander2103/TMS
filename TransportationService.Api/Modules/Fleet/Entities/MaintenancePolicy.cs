using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Fleet.Entities;

public enum MaintenancePolicyKind
{
    Maintenance,
    Inspection,
}

public enum FleetAssetKind
{
    Vehicle,
    Trailer,
}

/// <summary>
/// Configurable default maintenance/inspection intervals — a rule model instead of loose
/// nullable columns, so future rule types (age-based, country-specific) fit without schema
/// sprawl. Three levels, resolved most-specific-first:
/// 1. asset override (VehicleId/TrailerId set),
/// 2. category rule (CategoryId set),
/// 3. company default (neither set).
/// Belgian legal cadences are entered as tenant configuration, never hardcoded.
/// </summary>
public class MaintenancePolicy : AuditableTenantEntity
{
    public MaintenancePolicyKind Kind { get; set; }
    public FleetAssetKind AssetKind { get; set; }

    /// <summary>Vehicle/trailer category for a type-level rule (matching AssetKind).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Asset override — at most one of these, matching AssetKind.</summary>
    public Guid? VehicleId { get; set; }
    public Guid? TrailerId { get; set; }

    public int? IntervalMonths { get; set; }

    /// <summary>Mileage interval; vehicles only.</summary>
    public int? IntervalKm { get; set; }

    /// <summary>Days before the due date at which a warning starts showing.</summary>
    public int WarningDays { get; set; } = 30;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
