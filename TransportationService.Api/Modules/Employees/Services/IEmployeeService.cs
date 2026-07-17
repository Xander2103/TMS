namespace TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Employees.Dtos;

public interface IEmployeeService
{
    Task<EmployeePagedResult> SearchAsync(string? searchText, bool? isActive, int page, int pageSize, CancellationToken cancellationToken);
    Task<EmployeeDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<EmployeeDetailDto> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken);
    Task<EmployeeDetailDto?> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
