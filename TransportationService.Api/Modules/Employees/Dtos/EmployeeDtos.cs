using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Qualifications.Dtos;

namespace TransportationService.Api.Modules.Employees.Dtos;

/// <summary>One missing dossier-completeness requirement (HR maturity wave §2.1): a stable code,
/// a Dutch label for display, and the dossier section it deep-links to.</summary>
public record CompletenessItemDto(string Code, string Label, string Section);

/// <summary>Dossier-completeness snapshot for one employee. <c>Percentage</c> is
/// <c>100 * satisfied / totalApplicable</c>, rounded away from zero; <c>IsComplete</c> is
/// equivalent to <c>MissingItems.Count == 0</c>.</summary>
public record EmployeeCompletenessDto(int Percentage, bool IsComplete, IReadOnlyList<CompletenessItemDto> MissingItems);

public record EmployeeListItemDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    IReadOnlyList<string> FunctionNames,
    string? DepartmentName,
    EmploymentStatus EmploymentStatus,
    bool IsActive,
    bool IsDriver,
    /// <summary>Populated only for the personnel "Chauffeurs" view (rows with a driver profile).</summary>
    bool? DriverIsBlocked = null,
    DriverAvailabilityStatus? DriverAvailability = null,
    /// <summary>Dossier-completeness percentage (HR maturity wave §2.1), filled via one batched
    /// query in <c>SearchAsync</c>.</summary>
    int CompletenessPercentage = 0);

/// <summary>
/// Full employee profile. NationalRegisterNumber, Iban and Bic are null when the caller
/// lacks the employees.view_confidential permission.
/// </summary>
public record EmployeeDetailDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? NationalityCode,
    string? PreferredLanguageCode,
    string? Email,
    string? PhoneNumber,
    string? MobilePhone,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    DateOnly? EmploymentStartDate,
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
    string? Bic,
    CivilStatus? CivilStatus = null,
    int? DependentChildren = null,
    string? DimonaNumber = null,
    /// <summary>Confidential: null without employees.view_confidential.</summary>
    string? IdentityCardNumber = null,
    IReadOnlyList<EmployeeEmergencyContactDto>? EmergencyContacts = null,
    /// <summary>Dossier-completeness snapshot (HR maturity wave §2.1).</summary>
    EmployeeCompletenessDto? Completeness = null);

public record EmployeeEmergencyContactDto(
    Guid Id, string Name, string? Relationship, string? Phone, string? MobilePhone, string? Notes, int Priority);

/// <summary>Existing contacts keep their id; new ones come without one (wholesale sync).</summary>
public record EmployeeEmergencyContactInput(
    Guid? Id, string Name, string? Relationship, string? Phone, string? MobilePhone, string? Notes, int Priority);

/// <summary>
/// Optional driver profile created atomically with the employee — one workflow, one
/// transaction, no re-entering personal data on a separate screen.
/// </summary>
public record CreateEmployeeDriverProfile(Guid? DriverCategoryId, string? Notes, IReadOnlyList<Guid>? DriverCategoryIds = null);

/// <summary>
/// Only FirstName and LastName are required (spec §13); everything else is optional and can
/// be completed later. A supplied Email is format-checked; end date must not precede start.
/// </summary>
public record CreateEmployeeRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? PhoneNumber,
    string? Email,
    DateOnly? EmploymentStartDate,
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
    /// <summary>Accepted but ignored: EmployeeNote records are the source of truth; the
    /// legacy Employee.Notes column is read-only historical data and is never written.</summary>
    string? Notes = null,
    CreateEmployeeDriverProfile? DriverProfile = null,
    IReadOnlyList<CreateEmployeeQualificationRequest>? Qualifications = null,
    CivilStatus? CivilStatus = null,
    int? DependentChildren = null,
    string? DimonaNumber = null,
    string? IdentityCardNumber = null,
    DateOnly? EmploymentEndDate = null,
    IReadOnlyList<EmployeeEmergencyContactInput>? EmergencyContacts = null);

/// <summary>Same contract as create: only FirstName and LastName are required.</summary>
public record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? PhoneNumber,
    string? Email,
    DateOnly? EmploymentStartDate,
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
    /// <summary>Accepted but ignored: EmployeeNote records are the source of truth; the
    /// stored legacy Employee.Notes value is preserved untouched.</summary>
    string? Notes = null,
    CivilStatus? CivilStatus = null,
    int? DependentChildren = null,
    string? DimonaNumber = null,
    string? IdentityCardNumber = null,
    IReadOnlyList<EmployeeEmergencyContactInput>? EmergencyContacts = null);

// --- Personnel history (corrections wave §4) ---

/// <summary>One field-level change: readable Dutch label with the old and new value.</summary>
public record EmployeeHistoryChangeDto(string Field, string? Before, string? After);

/// <summary>
/// One audited save: all field changes of that save grouped together, with actor, timestamp,
/// action and the section of the personnel dossier they belong to.
/// </summary>
public record EmployeeHistoryEntryDto(
    Guid Id, DateTime Timestamp, string? UserName, string Action, string ActionLabel,
    string Category, IReadOnlyList<EmployeeHistoryChangeDto> Changes, string Summary);

public record EmployeeHistoryPageDto(
    IReadOnlyList<EmployeeHistoryEntryDto> Items, int TotalCount, int Page, int PageSize);
