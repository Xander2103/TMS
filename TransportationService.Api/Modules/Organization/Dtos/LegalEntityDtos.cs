namespace TransportationService.Api.Modules.Organization.Dtos;

public record LegalEntityDto(
    Guid Id,
    string LegalName,
    string? TradingName,
    string? CompanyNumber,
    string? VatNumber,
    string? PeppolId,
    string? PeppolScheme,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string? Iban,
    string? Bic,
    string? BankName,
    string DefaultCurrency,
    int PaymentTermDays,
    string InvoiceNumberFormat,
    int InvoiceSequencePadding,
    string? InvoicePrefix,
    string? InvoiceFooter,
    bool HasLogo,
    string? LogoFileName,
    bool IsActive,
    bool IsDefault,
    string? CreditNotePrefix = null);

/// <summary>Compact shape for dropdowns (entity pickers, top-bar selector).</summary>
public record LegalEntityOptionDto(Guid Id, string DisplayName, string? VatNumber, bool IsDefault, bool IsActive);

public record SaveLegalEntityRequest(
    string LegalName,
    string? TradingName,
    string? CompanyNumber,
    string? VatNumber,
    string? PeppolId,
    string? PeppolScheme,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string? Iban,
    string? Bic,
    string? BankName,
    string? DefaultCurrency,
    int PaymentTermDays,
    string? InvoiceNumberFormat,
    int InvoiceSequencePadding,
    string? InvoicePrefix,
    string? InvoiceFooter,
    bool IsDefault,
    string? CreditNotePrefix = null);

public record ActiveLegalEntityDto(Guid? LegalEntityId);
