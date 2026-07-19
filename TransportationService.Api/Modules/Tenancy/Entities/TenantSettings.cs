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
    public string? OrderNumberPrefix { get; set; } = "ORD-";
    public int OrderNumberNextValue { get; set; } = 1;
    public string? TripNumberPrefix { get; set; } = "RIT-";
    public int TripNumberNextValue { get; set; } = 1;
    public string? InvoiceNumberPrefix { get; set; } = "FAC-";
    public int InvoiceNumberNextValue { get; set; } = 1;
    public string? VehicleNumberPrefix { get; set; } = "VRT-";
    public int VehicleNumberNextValue { get; set; } = 1;
    public string? TrailerNumberPrefix { get; set; } = "OPL-";
    public int TrailerNumberNextValue { get; set; } = 1;

    // Company profile (shown on documents, used for invoicing)
    public string? CompanyLegalName { get; set; }
    public string? TradingName { get; set; }
    public string? CompanyNumber { get; set; }
    public string? VatNumber { get; set; }

    // Registered / legal address
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? CountryCode { get; set; }

    // Operational address (where day-to-day operations run; may differ from registered)
    public string? OperationalStreet { get; set; }
    public string? OperationalHouseNumber { get; set; }
    public string? OperationalPostalCode { get; set; }
    public string? OperationalCity { get; set; }
    public string? OperationalCountryCode { get; set; }

    // Contact
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Website { get; set; }

    // Financial / invoicing defaults
    public string? Iban { get; set; }
    public string DefaultCurrency { get; set; } = "EUR";
    public string? InvoiceEmail { get; set; }
    public int PaymentTermDays { get; set; } = 30;
    public decimal DefaultVatRatePercent { get; set; } = 21m;

    // Localisation / formatting
    public string DateFormat { get; set; } = "dd-MM-yyyy";
    public string DecimalSeparator { get; set; } = ",";
    public string DefaultWeightUnit { get; set; } = "kg";
    public string DefaultDistanceUnit { get; set; } = "km";

    // Transport operation defaults
    public int DefaultLoadingMinutes { get; set; } = 30;
    public int DefaultUnloadingMinutes { get; set; } = 30;

    // Schedule-conflict severities (ConflictSeverity names; unknown values fall back to Warning)
    public string TrainingConflictSeverity { get; set; } = "Warning";
    public string ShiftOverlapConflictSeverity { get; set; } = "Warning";

    // Package (colli) numbering
    public string? PackageNumberPrefix { get; set; } = "PKG-";
    public int PackageNumberNextValue { get; set; } = 1;

    // Branding (reference only; upload/storage is out of scope for this milestone)
    public string? LogoReference { get; set; }

    /// <summary>JSON-serialized <see cref="TenantModuleFlags"/>. Never read/written raw - always via a typed accessor.</summary>
    public string EnabledModulesJson { get; set; } = "{}";
}

public record TenantModuleFlags(
    bool Employees = true,
    bool Qualifications = true,
    bool Eligibility = true,
    bool Overrides = true,
    bool AuditLog = true);
