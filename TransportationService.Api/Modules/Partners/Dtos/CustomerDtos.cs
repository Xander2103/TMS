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
    string? Notes);

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
    bool IsActive);

public record SetCustomerBlockedRequest(bool IsBlocked, string? Reason);

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
