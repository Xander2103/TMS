namespace TransportationService.Api.Modules.Tenancy.Entities;

public class TenantSettings
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Timezone { get; set; } = "Europe/Amsterdam";
    public string DefaultLanguage { get; set; } = "nl";
    public int QualificationExpiryWarningDays { get; set; } = 30;
    public int DefaultPageSize { get; set; } = 25;
    public string? EmployeeNumberPrefix { get; set; }
    public int EmployeeNumberNextValue { get; set; } = 1;

    /// <summary>JSON-serialized <see cref="TenantModuleFlags"/>. Never read/written raw - always via a typed accessor.</summary>
    public string EnabledModulesJson { get; set; } = "{}";
}

public record TenantModuleFlags(
    bool Employees = true,
    bool Qualifications = true,
    bool Eligibility = true,
    bool Overrides = true,
    bool AuditLog = true);
