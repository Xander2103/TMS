using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public record DamageReportDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    Guid? DriverId,
    string? DriverName,
    DateOnly IncidentDate,
    string? Location,
    string Description,
    DamageSeverity Severity,
    DamageStatus Status,
    string? InsuranceReference,
    decimal? RepairCost,
    int? DowntimeDays,
    bool HasAttachment,
    string? Notes);

/// <summary>Row for the recent-damage overview (fleet dashboard).</summary>
public record RecentDamageDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    string OwnerNumber,
    string OwnerLicensePlate,
    DateOnly IncidentDate,
    DamageSeverity Severity,
    DamageStatus Status,
    string Description);

public record CreateDamageReportRequest(
    Guid? DriverId,
    DateOnly IncidentDate,
    string? Location,
    string Description,
    DamageSeverity Severity,
    string? InsuranceReference,
    string? Notes);

public record UpdateDamageReportRequest(
    Guid? DriverId,
    DateOnly IncidentDate,
    string? Location,
    string Description,
    DamageSeverity Severity,
    DamageStatus Status,
    string? InsuranceReference,
    decimal? RepairCost,
    int? DowntimeDays,
    string? Notes);

public enum DamageOperationOutcome
{
    Success,
    NotFound,
    OwnerNotFound,
    InvalidReference,
    ValidationFailed,
}

public record DamageOperationResult(DamageOperationOutcome Outcome, DamageReportDto? Report, string? Error = null)
{
    public static DamageOperationResult Success(DamageReportDto report) => new(DamageOperationOutcome.Success, report);
    public static readonly DamageOperationResult NotFound = new(DamageOperationOutcome.NotFound, null);
    public static readonly DamageOperationResult OwnerNotFound = new(DamageOperationOutcome.OwnerNotFound, null);
    public static readonly DamageOperationResult InvalidReference = new(DamageOperationOutcome.InvalidReference, null);
    public static DamageOperationResult Invalid(string error) => new(DamageOperationOutcome.ValidationFailed, null, error);
}
