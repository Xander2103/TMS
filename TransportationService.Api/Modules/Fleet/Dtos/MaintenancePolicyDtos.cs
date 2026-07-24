using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public record MaintenancePolicyDto(
    Guid Id,
    MaintenancePolicyKind Kind,
    FleetAssetKind AssetKind,
    Guid? CategoryId,
    string? CategoryName,
    Guid? VehicleId,
    string? VehicleNumber,
    Guid? TrailerId,
    string? TrailerNumber,
    int? IntervalMonths,
    int? IntervalKm,
    int WarningDays,
    string? Description,
    bool IsActive);

public record SaveMaintenancePolicyRequest(
    MaintenancePolicyKind Kind,
    FleetAssetKind AssetKind,
    Guid? CategoryId,
    Guid? VehicleId,
    Guid? TrailerId,
    int? IntervalMonths,
    int? IntervalKm,
    int WarningDays,
    string? Description,
    bool IsActive = true);

/// <summary>Resolution result: the applicable intervals + which level supplied them.</summary>
public record ResolvedMaintenancePolicy(
    Guid PolicyId,
    int? IntervalMonths,
    int? IntervalKm,
    int WarningDays,
    MaintenancePolicyLevel Level);

public enum MaintenancePolicyLevel
{
    Asset,
    Category,
    CompanyDefault,
}

/// <summary>Effective rule for one kind, with a human-readable source ("waar komt dit vandaan").</summary>
public record EffectivePolicyDto(
    Guid PolicyId,
    MaintenancePolicyLevel Level,
    string SourceLabel,
    int? IntervalMonths,
    int? IntervalKm,
    int WarningDays,
    string? Description);

/// <summary>Both effective rules of one asset; null = no applicable rule configured.</summary>
public record EffectivePoliciesDto(EffectivePolicyDto? Maintenance, EffectivePolicyDto? Inspection);
