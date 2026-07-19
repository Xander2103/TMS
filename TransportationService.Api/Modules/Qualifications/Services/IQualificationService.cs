namespace TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Qualifications.Dtos;

public interface IQualificationService
{
    Task<IReadOnlyList<EmployeeQualificationDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<EmployeeQualificationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<EmployeeQualificationDto> CreateAsync(Guid employeeId, CreateEmployeeQualificationRequest request, CancellationToken cancellationToken);
    Task<EmployeeQualificationDto?> UpdateAsync(Guid id, UpdateEmployeeQualificationRequest request, CancellationToken cancellationToken);
    Task<EmployeeQualificationDto?> VerifyAsync(Guid id, Guid verifyingUserId, CancellationToken cancellationToken);
    Task<EmployeeQualificationDto?> SuspendAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpiringQualificationDto>> ListExpiringWithinDaysAsync(int days, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExpiringQualificationDto>> ListExpiredAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<QualificationTypeDto>> ListQualificationTypesAsync(CancellationToken cancellationToken);

    /// <summary>Stores an uploaded document for the qualification and returns the updated DTO.</summary>
    Task<EmployeeQualificationDto?> AttachDocumentAsync(Guid employeeId, Guid id, string fileName, Stream content, CancellationToken cancellationToken);

    /// <summary>Opens the stored document; null when the qualification or document is missing.</summary>
    Task<(Stream Content, string FileName)?> OpenDocumentAsync(Guid employeeId, Guid id, CancellationToken cancellationToken);

    Task<bool> RemoveDocumentAsync(Guid employeeId, Guid id, CancellationToken cancellationToken);
}
