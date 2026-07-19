using TransportationService.Api.Modules.Employees.Entities;

namespace TransportationService.Api.Modules.Employees.Dtos;

public record EmployeeListItemDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    IReadOnlyList<string> FunctionNames,
    string? DepartmentName,
    EmploymentStatus EmploymentStatus,
    bool IsActive,
    bool IsDriver);

/// <summary>
/// Full employee profile. NationalRegisterNumber, Iban and Bic are null when the caller
/// lacks the employees.view_confidential permission.
/// </summary>
public record EmployeeDetailDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string? PlaceOfBirth,
    string? NationalityCode,
    string? PreferredLanguageCode,
    string Email,
    string PhoneNumber,
    string? MobilePhone,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City,
    string? CountryCode,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    DateOnly EmploymentStartDate,
    DateOnly? EmploymentEndDate,
    EmploymentStatus EmploymentStatus,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? ContractTypeId,
    string? ContractTypeName,
    IReadOnlyList<Guid> JobFunctionIds,
    IReadOnlyList<string> FunctionNames,
    bool IsActive,
    string? Notes,
    /// <summary>Driver profile linked to this employee, when one exists.</summary>
    Guid? DriverId,
    string? NationalRegisterNumber,
    string? Iban,
    string? Bic);

/// <summary>
/// Optional driver profile created atomically with the employee — one workflow, one
/// transaction, no re-entering personal data on a separate screen.
/// </summary>
public record CreateEmployeeDriverProfile(Guid? DriverCategoryId, string? Notes);

public record CreateEmployeeRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City,
    string PhoneNumber,
    string Email,
    DateOnly EmploymentStartDate,
    EmploymentStatus EmploymentStatus,
    string? CountryCode = null,
    string? PlaceOfBirth = null,
    string? NationalityCode = null,
    string? PreferredLanguageCode = null,
    string? MobilePhone = null,
    Guid? DepartmentId = null,
    Guid? ContractTypeId = null,
    IReadOnlyList<Guid>? JobFunctionIds = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? NationalRegisterNumber = null,
    string? Iban = null,
    string? Bic = null,
    string? Notes = null,
    CreateEmployeeDriverProfile? DriverProfile = null);

public record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Street,
    string HouseNumber,
    string PostalCode,
    string City,
    string PhoneNumber,
    string Email,
    DateOnly EmploymentStartDate,
    EmploymentStatus EmploymentStatus,
    DateOnly? EmploymentEndDate = null,
    string? CountryCode = null,
    string? PlaceOfBirth = null,
    string? NationalityCode = null,
    string? PreferredLanguageCode = null,
    string? MobilePhone = null,
    Guid? DepartmentId = null,
    Guid? ContractTypeId = null,
    IReadOnlyList<Guid>? JobFunctionIds = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? NationalRegisterNumber = null,
    string? Iban = null,
    string? Bic = null,
    string? Notes = null);
