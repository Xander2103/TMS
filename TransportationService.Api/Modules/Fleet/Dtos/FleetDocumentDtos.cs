using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Dtos;

public enum FleetDocumentStatus
{
    NoExpiry,
    Valid,
    ExpiringSoon,
    Expired,
}

public record FleetDocumentDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    FleetDocumentType DocumentType,
    string? CustomTypeName,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    int? WarningDays,
    FleetDocumentStatus Status,
    bool HasAttachment,
    string? Notes,
    string? IssuingAuthority = null,
    string? FileName = null);

/// <summary>Row for the expiring-documents overview (fleet dashboard / warnings).</summary>
public record ExpiringFleetDocumentDto(
    Guid Id,
    Guid? VehicleId,
    Guid? TrailerId,
    string OwnerNumber,
    string OwnerLicensePlate,
    FleetDocumentType DocumentType,
    string? CustomTypeName,
    DateOnly ExpiryDate,
    FleetDocumentStatus Status);

public record CreateFleetDocumentRequest(
    FleetDocumentType DocumentType,
    string? CustomTypeName,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    int? WarningDays,
    string? Notes,
    string? IssuingAuthority = null);

public record UpdateFleetDocumentRequest(
    FleetDocumentType DocumentType,
    string? CustomTypeName,
    string? DocumentNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    int? WarningDays,
    string? Notes,
    string? IssuingAuthority = null);

public enum FleetDocumentOperationOutcome
{
    Success,
    NotFound,
    OwnerNotFound,
    ValidationFailed,
}

public record FleetDocumentOperationResult(
    FleetDocumentOperationOutcome Outcome, FleetDocumentDto? Document, string? Error = null)
{
    public static FleetDocumentOperationResult Success(FleetDocumentDto document) => new(FleetDocumentOperationOutcome.Success, document);
    public static readonly FleetDocumentOperationResult NotFound = new(FleetDocumentOperationOutcome.NotFound, null);
    public static readonly FleetDocumentOperationResult OwnerNotFound = new(FleetDocumentOperationOutcome.OwnerNotFound, null);
    public static FleetDocumentOperationResult Invalid(string error) => new(FleetDocumentOperationOutcome.ValidationFailed, null, error);
}
