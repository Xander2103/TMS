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
    Task<IReadOnlyList<EmployeeQualificationDto>> ListExpiringWithinDaysAsync(int days, CancellationToken cancellationToken);
    Task<IReadOnlyList<EmployeeQualificationDto>> ListExpiredAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<QualificationTypeDto>> ListQualificationTypesAsync(CancellationToken cancellationToken);
}
