using TransportationService.Api.Modules.Partners.Entities;

namespace TransportationService.Api.Modules.Partners.Dtos;

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
    string? Notes);

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
    IReadOnlyList<CustomerContactDto> Contacts);

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
    string? CustomerNumber = null);

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
    bool CustomerReferenceRequired = false);

public record SetCustomerBlockedRequest(bool IsBlocked, string? Reason);

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
    string? Notes);

public record UpdateCustomerContactRequest(
    string FirstName,
    string LastName,
    string? Role,
    string? Email,
    string? PhoneNumber,
    bool IsPrimary,
    string? Notes);
