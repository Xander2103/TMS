namespace TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;

public record EmployeeListItemDto(Guid Id, string EmployeeNumber, string FirstName, string LastName, EmployeeFunction PrimaryFunction, EmploymentStatus EmploymentStatus, bool IsActive);

public record EmployeeDetailDto(
    Guid Id, string EmployeeNumber, string FirstName, string LastName,
    string Street, string HouseNumber, string PostalCode, string City, string Country,
    string PhoneNumber, string Email, DateOnly DateOfBirth,
    DateOnly EmploymentStartDate, DateOnly? EmploymentEndDate,
    EmploymentStatus EmploymentStatus, EmployeeFunction PrimaryFunction, bool IsActive,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes);

public record CreateEmployeeRequest(
    string FirstName, string LastName, string Street, string HouseNumber, string PostalCode, string City, string Country,
    string PhoneNumber, string Email, DateOnly DateOfBirth, DateOnly EmploymentStartDate,
    EmploymentStatus EmploymentStatus, EmployeeFunction PrimaryFunction,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes);

public record UpdateEmployeeRequest(
    string FirstName, string LastName, string Street, string HouseNumber, string PostalCode, string City, string Country,
    string PhoneNumber, string Email, DateOnly DateOfBirth, DateOnly? EmploymentEndDate,
    EmploymentStatus EmploymentStatus, EmployeeFunction PrimaryFunction,
    string? EmergencyContactName, string? EmergencyContactPhone, string? Notes);

public record EmployeePagedResult(IReadOnlyList<EmployeeListItemDto> Items, int TotalCount, int Page, int PageSize);
