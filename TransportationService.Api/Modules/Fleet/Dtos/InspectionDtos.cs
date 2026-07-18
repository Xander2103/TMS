using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public enum InspectionUrgency
{
    Ok,
    DueSoon,
    Overdue,
    Completed,
}

public record InspectionDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    InspectionType InspectionType,
    string? CustomTypeName,
    DateOnly DueDate,
    DateOnly? CompletedDate,
    InspectionResult? Result,
    int? IntervalMonths,
    int? WarningDays,
    InspectionUrgency Urgency,
    bool HasAttachment,
    string? Notes);

/// <summary>Row for the inspections-due overview (fleet dashboard / warnings).</summary>
public record DueInspectionDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    string OwnerNumber,
    string OwnerLicensePlate,
    InspectionType InspectionType,
    string? CustomTypeName,
    DateOnly DueDate,
    InspectionUrgency Urgency);

public record CreateInspectionRequest(
    InspectionType InspectionType,
    string? CustomTypeName,
    DateOnly DueDate,
    int? IntervalMonths,
    int? WarningDays,
    string? Notes);

public record UpdateInspectionRequest(
    InspectionType InspectionType,
    string? CustomTypeName,
    DateOnly DueDate,
    int? IntervalMonths,
    int? WarningDays,
    string? Notes);

public record CompleteInspectionRequest(
    DateOnly CompletedDate,
    InspectionResult Result,
    string? Notes);

public enum InspectionOperationOutcome
{
    Success,
    NotFound,
    OwnerNotFound,
    ValidationFailed,
    AlreadyCompleted,
}

public record InspectionOperationResult(
    InspectionOperationOutcome Outcome,
    InspectionDto? Inspection,
    InspectionDto? FollowUp = null,
    string? Error = null)
{
    public static InspectionOperationResult Success(InspectionDto inspection, InspectionDto? followUp = null) =>
        new(InspectionOperationOutcome.Success, inspection, followUp);
    public static readonly InspectionOperationResult NotFound = new(InspectionOperationOutcome.NotFound, null);
    public static readonly InspectionOperationResult OwnerNotFound = new(InspectionOperationOutcome.OwnerNotFound, null);
    public static readonly InspectionOperationResult AlreadyCompleted = new(InspectionOperationOutcome.AlreadyCompleted, null);
    public static InspectionOperationResult Invalid(string error) => new(InspectionOperationOutcome.ValidationFailed, null, null, error);
}
