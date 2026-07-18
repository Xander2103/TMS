namespace TransportationService.Api.Modules.Tenancy.Entities;

public class TenantSettings
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Timezone { get; set; } = "Europe/Amsterdam";
    public string DefaultLanguage { get; set; } = "nl";
    public int QualificationExpiryWarningDays { get; set; } = 30;
    public int DefaultPageSize { get; set; } = 25;

    // Document / record numbering
    public string? EmployeeNumberPrefix { get; set; }
    public int EmployeeNumberNextValue { get; set; } = 1;
    public string? CustomerNumberPrefix { get; set; } = "KL-";
    public int CustomerNumberNextValue { get; set; } = 1;
    public string? DriverNumberPrefix { get; set; } = "CH-";
    public int DriverNumberNextValue { get; set; } = 1;

    // Company profile (shown on documents, used for invoicing)
    public string? CompanyLegalName { get; set; }
    public string? VatNumber { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }
    public string? Iban { get; set; }
    public string DefaultCurrency { get; set; } = "EUR";

    /// <summary>JSON-serialized <see cref="TenantModuleFlags"/>. Never read/written raw - always via a typed accessor.</summary>
    public string EnabledModulesJson { get; set; } = "{}";
}

public record TenantModuleFlags(
    bool Employees = true,
    bool Qualifications = true,
    bool Eligibility = true,
    bool Overrides = true,
    bool AuditLog = true);
