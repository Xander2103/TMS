namespace TransportationService.Api.Modules.Peppol.Dtos;

public record PeppolSettingsDto(
    Guid LegalEntityId,
    string LegalEntityName,
    bool Enabled,
    string Environment,
    string ProviderKey,
    string? InvoiceEmailFallback,
    string? DefaultInvoiceNote);

public record SavePeppolSettingsRequest(
    bool Enabled,
    string Environment,
    string ProviderKey,
    string? InvoiceEmailFallback,
    string? DefaultInvoiceNote);

/// <summary>One legal entity's Peppol configuration-completeness checklist row.</summary>
public record PeppolChecklistItemDto(
    Guid LegalEntityId,
    string LegalEntityName,
    bool HasPeppolIdentity,
    bool HasVatNumber,
    bool HasIban,
    bool Enabled,
    string Environment,
    string ProviderKey,
    bool IsComplete);

public record PeppolCountsDto(int Queued, int Delivered, int Failed, int ReceivedIncoming);

public record PeppolOverviewDto(
    IReadOnlyList<PeppolChecklistItemDto> LegalEntities,
    PeppolCountsDto Counts,
    /// <summary>Customers with Peppol enabled but missing a Peppol id/scheme.</summary>
    int CustomersEnabledWithoutPeppolId,
    /// <summary>Active customers with no Peppol id/scheme at all (informational, Peppol not necessarily wanted).</summary>
    int ActiveCustomersMissingPeppolData);

public record PeppolTestConnectionResultDto(
    bool Found,
    IReadOnlyList<string> SupportedDocumentTypes,
    string? ProviderReference,
    string? Error);

public record CustomerPeppolVerifyResultDto(
    bool Found,
    IReadOnlyList<string> SupportedDocumentTypes,
    DateTime LastCheckedAt,
    string? Reference);

/// <summary>One actionable Dutch readiness issue blocking Peppol sending of an invoice.</summary>
public record PeppolValidationIssueDto(string Code, string Message);

public record PeppolInvoiceValidationResultDto(bool IsValid, IReadOnlyList<PeppolValidationIssueDto> Issues);

public record PeppolPreviewPartyDto(string Name, string? VatNumber, string? Participant);

public record PeppolPreviewLineDto(
    int Sequence, string Description, decimal Quantity, string UnitCode,
    decimal UnitPrice, decimal LineTotal, string VatCategoryCode, decimal VatRatePercent);

public record PeppolPreviewVatGroupDto(string VatCategoryCode, decimal VatRatePercent, decimal TaxableAmount, decimal VatAmount);

/// <summary>Structured summary of what the UBL document will contain (no XML parsing in the UI).</summary>
public record PeppolInvoicePreviewDto(
    string InvoiceNumber,
    string Kind,
    DateOnly InvoiceDate,
    string Currency,
    PeppolPreviewPartyDto Seller,
    PeppolPreviewPartyDto Buyer,
    IReadOnlyList<PeppolPreviewLineDto> Lines,
    IReadOnlyList<PeppolPreviewVatGroupDto> VatGroups,
    decimal Subtotal,
    decimal VatAmount,
    decimal Total,
    string? BuyerReference,
    string? PurchaseOrderNumber,
    string? CreditedInvoiceNumber);
