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
    string? Notes,
    /// <summary>FL-M7: the trip during which the damage occurred, when known.</summary>
    Guid? TripId = null,
    string? TripNumber = null)
{
    /// <summary>EX-03: the incident/afwijking this report came from, described for the fleet
    /// (type, severity, trip, order, route) — no customer, cost or HR data.</summary>
    public Guid? SourceIncidentId { get; init; }
    public Guid? SourceExceptionId { get; init; }
    public string? SourceLabel { get; init; }
}

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
    string? Notes,
    /// <summary>FL-M7: optional trip and the costs known at reporting time.</summary>
    Guid? TripId = null,
    decimal? RepairCost = null,
    int? DowntimeDays = null);

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
    string? Notes,
    Guid? TripId = null);

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
