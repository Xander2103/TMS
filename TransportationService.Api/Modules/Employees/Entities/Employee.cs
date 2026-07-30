using TransportationService.Api.Common.Abstractions;

namespace TransportationService.Api.Modules.Employees.Entities;

/// <summary>
/// The person record: every human working for the company is an Employee. Specialised
/// profiles (Driver) link 1:1 to this record and never duplicate personal data.
/// NationalRegisterNumber, Iban and Bic are confidential: served and editable only with
/// the employees.view_confidential permission.
/// </summary>
public class Employee : ITenantOwned, IHasId, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;

    // Person
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string? PlaceOfBirth { get; set; }
    /// <summary>Code of the tenant Nationality lookup (e.g. "BE").</summary>
    public string? NationalityCode { get; set; }
    /// <summary>Belgian national register number. Confidential.</summary>
    public string? NationalRegisterNumber { get; set; }
    /// <summary>Code of the tenant Language lookup (e.g. "nl").</summary>
    public string? PreferredLanguageCode { get; set; }

    // Contact
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? MobilePhone { get; set; }

    // Address
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-2 code validated against the global country reference.</summary>
    public string? CountryCode { get; set; }

    // Emergency contact. Legacy single pair: mirrors the priority-1 EmployeeEmergencyContact
    // row (kept in sync on write) so nothing that reads these columns breaks.
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    /// <summary>All emergency contacts, ordered by Priority (1 = first to call).</summary>
    public List<EmployeeEmergencyContact> EmergencyContacts { get; set; } = [];

    // Civil status / household (HR)
    public CivilStatus? CivilStatus { get; set; }
    public int? DependentChildren { get; set; }

    /// <summary>Identity-card number. Confidential (like NRN/IBAN).</summary>
    public string? IdentityCardNumber { get; set; }

    // Banking (confidential)
    public string? Iban { get; set; }
    public string? Bic { get; set; }

    // Employment
    public DateOnly EmploymentStartDate { get; set; }
    public DateOnly? EmploymentEndDate { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ContractTypeId { get; set; }

    /// <summary>DIMONA declaration reference (Belgian employment registration).</summary>
    public string? DimonaNumber { get; set; }

    /// <summary>HR job functions (multi). These are labels — application permissions are
    /// managed exclusively through user roles.</summary>
    public List<EmployeeJobFunction> JobFunctions { get; set; } = [];

    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
