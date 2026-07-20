namespace TransportationService.Api.Modules.Tenancy.Dtos;

/// <summary>Full company/tenant settings, returned to the settings screen.</summary>
public record CompanySettingsDto(
    // Company profile
    string? CompanyLegalName,
    string? TradingName,
    string? CompanyNumber,
    string? VatNumber,
    // Registered address
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    // Operational address
    string? OperationalStreet,
    string? OperationalHouseNumber,
    string? OperationalPostalCode,
    string? OperationalCity,
    string? OperationalCountryCode,
    // Contact
    string? Email,
    string? PhoneNumber,
    string? Website,
    // Localisation
    string DefaultLanguage,
    string Timezone,
    string DefaultCurrency,
    string DateFormat,
    string DecimalSeparator,
    string DefaultWeightUnit,
    string DefaultDistanceUnit,
    // Invoicing / financial
    string? Iban,
    string? InvoiceEmail,
    int PaymentTermDays,
    decimal DefaultVatRatePercent,
    // Transport defaults
    int DefaultLoadingMinutes,
    int DefaultUnloadingMinutes,
    // Schedule-conflict severities (Information | Warning | Blocking)
    string TrainingConflictSeverity,
    string ShiftOverlapConflictSeverity,
    string CapacityConflictSeverity,
    // Qualifications
    int QualificationExpiryWarningDays,
    // Numbering
    string? EmployeeNumberPrefix, int EmployeeNumberNextValue,
    string? CustomerNumberPrefix, int CustomerNumberNextValue,
    string? DriverNumberPrefix, int DriverNumberNextValue,
    string? OrderNumberPrefix, int OrderNumberNextValue,
    string? TripNumberPrefix, int TripNumberNextValue,
    string? InvoiceNumberPrefix, int InvoiceNumberNextValue,
    string? VehicleNumberPrefix, int VehicleNumberNextValue,
    string? TrailerNumberPrefix, int TrailerNumberNextValue,
    // Presentation
    int DefaultPageSize,
    string? LogoReference);

/// <summary>Editable company/tenant settings. Every field is replaced on save.</summary>
public record UpdateCompanySettingsRequest(
    string? CompanyLegalName,
    string? TradingName,
    string? CompanyNumber,
    string? VatNumber,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? OperationalStreet,
    string? OperationalHouseNumber,
    string? OperationalPostalCode,
    string? OperationalCity,
    string? OperationalCountryCode,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string DefaultLanguage,
    string Timezone,
    string DefaultCurrency,
    string DateFormat,
    string DecimalSeparator,
    string DefaultWeightUnit,
    string DefaultDistanceUnit,
    string? Iban,
    string? InvoiceEmail,
    int PaymentTermDays,
    decimal DefaultVatRatePercent,
    int DefaultLoadingMinutes,
    int DefaultUnloadingMinutes,
    string TrainingConflictSeverity,
    string ShiftOverlapConflictSeverity,
    int QualificationExpiryWarningDays,
    string? EmployeeNumberPrefix, int EmployeeNumberNextValue,
    string? CustomerNumberPrefix, int CustomerNumberNextValue,
    string? DriverNumberPrefix, int DriverNumberNextValue,
    string? OrderNumberPrefix, int OrderNumberNextValue,
    string? TripNumberPrefix, int TripNumberNextValue,
    string? InvoiceNumberPrefix, int InvoiceNumberNextValue,
    string? VehicleNumberPrefix, int VehicleNumberNextValue,
    string? TrailerNumberPrefix, int TrailerNumberNextValue,
    int DefaultPageSize,
    string? LogoReference,
    // Trailing default: existing positional callers keep compiling; the JSON binder maps by name.
    string CapacityConflictSeverity = "Warning");
