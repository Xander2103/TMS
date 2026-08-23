using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Dtos;

/// <summary>One Peppol scheme (EAS) option for the grouped Peppol control.</summary>
public sealed record PeppolSchemeDto(string Code, string Label, string? CountryCode);

public record CustomerListItemDto(
    Guid Id,
    string CustomerNumber,
    string Name,
    string? City,
    string? CountryCode,
    string? CategoryName,
    bool IsActive,
    bool IsBlocked);

public record CustomerContactDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Role,
    string? Email,
    string? PhoneNumber,
    bool IsPrimary,
    string? Notes,
    string? DisplayName = null,
    string? Nickname = null,
    string? MobilePhone = null,
    Guid? DepartmentId = null,
    string? PreferredLanguageCode = null,
    bool IsActive = true,
    string ContactType = "Algemeen");

public record CustomerDetailDto(
    Guid Id,
    string CustomerNumber,
    string Name,
    string? LegalName,
    string? VatNumber,
    Guid? CategoryId,
    string? CategoryName,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? InvoiceEmail,
    int PaymentTermDays,
    string? DefaultLanguageCode,
    string? Notes,
    bool IsActive,
    bool IsBlocked,
    string? BlockReason,
    VatTreatment VatTreatment,
    decimal? DefaultVatRatePercent,
    string? VatCountryCode,
    string? VatNotes,
    string? PeppolId,
    string? PeppolScheme,
    string? InvoiceLanguageCode,
    bool PurchaseOrderRequired,
    bool SignedDeliveryNoteRequired,
    bool CustomerReferenceRequired,
    IReadOnlyList<CustomerContactDto> Contacts,
    string? Nickname = null,
    string? CompanyNumber = null,
    string CurrencyCode = "EUR",
    string? Iban = null,
    string? Bic = null,
    string? BankName = null,
    string? BankAccountNumber = null,
    Guid? DefaultLegalEntityId = null,
    bool PeppolEnabled = false,
    string PeppolDeliveryPreference = "Peppol",
    string? BuyerReference = null,
    string PeppolValidationStatus = "Unknown",
    DateTime? PeppolValidatedAt = null,
    string? PeppolValidationReference = null,
    /// <summary>Wave 2: allowed issuing entities; empty = every active entity is allowed.</summary>
    IReadOnlyList<Guid>? AllowedLegalEntityIds = null,
    /// <summary>Wave 2: PerDossier | Weekly | Monthly | ByReference | Manual (default).</summary>
    string InvoiceGrouping = "Manual",
    /// <summary>P1: GenerateOwn | CustomerDocument | PerOrder — who supplies the transport document.</summary>
    string DocumentStrategy = "GenerateOwn");

public record CreateCustomerRequest(
    string Name,
    string? LegalName,
    string? VatNumber,
    Guid? CategoryId,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? InvoiceEmail,
    int PaymentTermDays,
    string? DefaultLanguageCode,
    string? Notes,
    VatTreatment VatTreatment = VatTreatment.DomesticVat,
    decimal? DefaultVatRatePercent = null,
    string? VatCountryCode = null,
    string? VatNotes = null,
    string? PeppolId = null,
    string? PeppolScheme = null,
    string? InvoiceLanguageCode = null,
    bool PurchaseOrderRequired = false,
    bool SignedDeliveryNoteRequired = false,
    bool CustomerReferenceRequired = false,
    CreateCustomerContactRequest? InitialContact = null,
    string? CustomerNumber = null,
    string? Nickname = null,
    string? CompanyNumber = null,
    string? CurrencyCode = null,
    string? Iban = null,
    string? Bic = null,
    string? BankName = null,
    string? BankAccountNumber = null,
    Guid? DefaultLegalEntityId = null,
    bool PeppolEnabled = false,
    string? PeppolDeliveryPreference = null,
    string? BuyerReference = null,
    IReadOnlyList<CreateCustomerContactRequest>? Contacts = null,
    /// <summary>Wave 2: allowed issuing entities; null = leave as-is, empty = clear (all allowed).</summary>
    IReadOnlyList<Guid>? AllowedLegalEntityIds = null,
    /// <summary>Wave 2: invoice grouping preference; null = leave as-is (Manual on create).</summary>
    string? InvoiceGrouping = null,
    /// <summary>P1: document strategy; null = leave as-is (GenerateOwn on create).</summary>
    string? DocumentStrategy = null);

public record UpdateCustomerRequest(
    string Name,
    string? LegalName,
    string? VatNumber,
    Guid? CategoryId,
    string? Email,
    string? PhoneNumber,
    string? Website,
    string? Street,
    string? HouseNumber,
    string? PostalCode,
    string? City,
    string? CountryCode,
    string? InvoiceEmail,
    int PaymentTermDays,
    string? DefaultLanguageCode,
    string? Notes,
    bool IsActive,
    VatTreatment VatTreatment = VatTreatment.DomesticVat,
    decimal? DefaultVatRatePercent = null,
    string? VatCountryCode = null,
    string? VatNotes = null,
    string? PeppolId = null,
    string? PeppolScheme = null,
    string? InvoiceLanguageCode = null,
    bool PurchaseOrderRequired = false,
    bool SignedDeliveryNoteRequired = false,
    bool CustomerReferenceRequired = false,
    string? Nickname = null,
    string? CompanyNumber = null,
    string? CurrencyCode = null,
    string? Iban = null,
    string? Bic = null,
    string? BankName = null,
    string? BankAccountNumber = null,
    Guid? DefaultLegalEntityId = null,
    bool PeppolEnabled = false,
    string? PeppolDeliveryPreference = null,
    string? BuyerReference = null,
    /// <summary>Wave 2: allowed issuing entities; null = leave as-is, empty = clear (all allowed).</summary>
    IReadOnlyList<Guid>? AllowedLegalEntityIds = null,
    /// <summary>Wave 2: invoice grouping preference; null = leave as-is.</summary>
    string? InvoiceGrouping = null,
    /// <summary>P1: document strategy; null = leave as-is.</summary>
    string? DocumentStrategy = null);

public record SetCustomerBlockedRequest(bool IsBlocked, string? Reason);

/// <summary>One readable field change (labels and values resolved server-side, Dutch).</summary>
public record CustomerHistoryChangeDto(string Field, string? Before, string? After);

public record CustomerHistoryEntryDto(
    Guid Id,
    DateTime Timestamp,
    string? UserName,
    string Action,
    string ActionLabel,
    /// <summary>LEGACY Nederlands categorielabel; logica hoort op CategoryCode (i18n-wave).</summary>
    string Category,
    IReadOnlyList<CustomerHistoryChangeDto> Changes,
    string Summary,
    /// <summary>Stabiele categoriecode: customer | contacts | locations | billing | communication.</summary>
    string CategoryCode = "customer");

public record CustomerHistoryPageDto(
    IReadOnlyList<CustomerHistoryEntryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>Company-registry lookup request (VAT or enterprise number).</summary>
public record CompanyRegistryLookupRequest(string Number);

/// <summary>Manual customer-number change; requires customers.override_number + reason (audited).</summary>
public record ChangeCustomerNumberRequest(string CustomerNumber, string Reason);

public record SetCustomerActiveRequest(bool IsActive);

public record CreateCustomerContactRequest(
    string FirstName,
    string LastName,
    string? Role,
    string? Email,
    string? PhoneNumber,
    bool IsPrimary,
    string? Notes,
    string? DisplayName = null,
    string? Nickname = null,
    string? MobilePhone = null,
    Guid? DepartmentId = null,
    string? PreferredLanguageCode = null,
    bool IsActive = true,
    string? ContactType = null);

public record UpdateCustomerContactRequest(
    string FirstName,
    string LastName,
    string? Role,
    string? Email,
    string? PhoneNumber,
    bool IsPrimary,
    string? Notes,
    string? DisplayName = null,
    string? Nickname = null,
    string? MobilePhone = null,
    Guid? DepartmentId = null,
    string? PreferredLanguageCode = null,
    bool IsActive = true,
    string? ContactType = null);
