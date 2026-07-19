namespace TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;

public record QualificationTypeDto(Guid Id, string Code, string Name, string? Description, string Category, bool RequiresExpiryDate, bool IsActive);

public record EmployeeQualificationDto(
    Guid Id, Guid EmployeeId, Guid QualificationTypeId, string QualificationTypeCode, string QualificationTypeName,
    string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, string? IssuingCountryCode,
    QualificationStatus StoredStatus, QualificationStatus EffectiveStatus,
    bool HasDocument, string? Notes, DateTime? VerifiedAt, Guid? VerifiedByUserId);

/// <summary>Row for the tenant-wide expiry overview: qualification + who it belongs to.</summary>
public record ExpiringQualificationDto(
    Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber,
    string QualificationTypeCode, string QualificationTypeName,
    DateOnly? ExpiryDate, QualificationStatus EffectiveStatus, bool EmployeeIsDriver);

public record CreateEmployeeQualificationRequest(
    Guid QualificationTypeId, string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate,
    string? Notes, string? IssuingCountryCode = null);

public record UpdateEmployeeQualificationRequest(
    string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate,
    string? Notes, string? IssuingCountryCode = null);
