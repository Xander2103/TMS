namespace TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;

public record QualificationTypeDto(Guid Id, string Code, string Name, string? Description, string Category, bool RequiresExpiryDate, bool IsActive);

public record EmployeeQualificationDto(
    Guid Id, Guid EmployeeId, Guid QualificationTypeId, string QualificationTypeCode, string QualificationTypeName,
    string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, QualificationStatus StoredStatus, QualificationStatus EffectiveStatus,
    string? DocumentPath, string? Notes, DateTime? VerifiedAt, Guid? VerifiedByUserId);

public record CreateEmployeeQualificationRequest(Guid QualificationTypeId, string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, string? Notes);
public record UpdateEmployeeQualificationRequest(string? DocumentNumber, DateOnly ObtainedDate, DateOnly? ExpiryDate, string? Notes);
