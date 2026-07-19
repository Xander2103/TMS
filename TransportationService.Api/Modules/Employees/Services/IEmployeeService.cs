using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Services;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeListItemDto>> SearchAsync(
        string? searchText, bool? isActive, Guid? jobFunctionId, Guid? departmentId,
        EmploymentStatus? employmentStatus, PageRequest page, CancellationToken cancellationToken);

    /// <param name="includeConfidential">When false, confidential fields (NRN/IBAN/BIC) are nulled.</param>
    Task<EmployeeDetailDto?> GetByIdAsync(Guid id, bool includeConfidential, CancellationToken cancellationToken);

    /// <param name="canEditConfidential">When false, confidential fields in the request are ignored.</param>
    Task<EmployeeDetailDto> CreateAsync(CreateEmployeeRequest request, bool canEditConfidential, CancellationToken cancellationToken);

    Task<EmployeeDetailDto?> UpdateAsync(Guid id, UpdateEmployeeRequest request, bool canEditConfidential, CancellationToken cancellationToken);

    Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ReactivateAsync(Guid id, CancellationToken cancellationToken);
}
