using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public class EmployeeService : IEmployeeService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public EmployeeService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<EmployeePagedResult> SearchAsync(string? searchText, bool? isActive, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId);

        if (isActive is { } activeFilter)
        {
            query = query.Where(e => e.IsActive == activeFilter);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = $"%{searchText.Trim()}%";
            query = query.Where(e =>
                EF.Functions.Like(e.EmployeeNumber, pattern) ||
                EF.Functions.Like(e.FirstName, pattern) ||
                EF.Functions.Like(e.LastName, pattern) ||
                EF.Functions.Like(e.Email, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeListItemDto(e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.PrimaryFunction, e.EmploymentStatus, e.IsActive))
            .ToListAsync(cancellationToken);

        return new EmployeePagedResult(items, totalCount, page, pageSize);
    }

    public async Task<EmployeeDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.TenantId, cancellationToken);

        return employee is null ? null : MapToDetail(employee);
    }

    public async Task<EmployeeDetailDto> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var employeeNumber = GenerateEmployeeNumber(settings);

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeNumber = employeeNumber,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Street = request.Street.Trim(),
            HouseNumber = request.HouseNumber.Trim(),
            PostalCode = request.PostalCode.Trim(),
            City = request.City.Trim(),
            Country = request.Country.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            Email = request.Email.Trim(),
            DateOfBirth = request.DateOfBirth,
            EmploymentStartDate = request.EmploymentStartDate,
            EmploymentStatus = request.EmploymentStatus,
            PrimaryFunction = request.PrimaryFunction,
            IsActive = true,
            EmergencyContactName = request.EmergencyContactName,
            EmergencyContactPhone = request.EmergencyContactPhone,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.Employees.Add(employee);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetail(employee);
    }

    public async Task<EmployeeDetailDto?> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (employee is null) return null;

        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Street = request.Street.Trim();
        employee.HouseNumber = request.HouseNumber.Trim();
        employee.PostalCode = request.PostalCode.Trim();
        employee.City = request.City.Trim();
        employee.Country = request.Country.Trim();
        employee.PhoneNumber = request.PhoneNumber.Trim();
        employee.Email = request.Email.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.EmploymentEndDate = request.EmploymentEndDate;
        employee.EmploymentStatus = request.EmploymentStatus;
        employee.PrimaryFunction = request.PrimaryFunction;
        employee.EmergencyContactName = request.EmergencyContactName;
        employee.EmergencyContactPhone = request.EmergencyContactPhone;
        employee.Notes = request.Notes;
        employee.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetail(employee);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (employee is null) return false;

        employee.IsActive = false;
        employee.EmploymentEndDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
        employee.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string GenerateEmployeeNumber(Tenancy.Entities.TenantSettings? settings)
    {
        if (settings is null)
        {
            return Guid.NewGuid().ToString("N")[..8];
        }

        var number = $"{settings.EmployeeNumberPrefix}{settings.EmployeeNumberNextValue:D4}";
        settings.EmployeeNumberNextValue++;
        return number;
    }

    private static EmployeeDetailDto MapToDetail(Employee e) => new(
        e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
        e.Street, e.HouseNumber, e.PostalCode, e.City, e.Country,
        e.PhoneNumber, e.Email, e.DateOfBirth,
        e.EmploymentStartDate, e.EmploymentEndDate,
        e.EmploymentStatus, e.PrimaryFunction, e.IsActive,
        e.EmergencyContactName, e.EmergencyContactPhone, e.Notes);
}
