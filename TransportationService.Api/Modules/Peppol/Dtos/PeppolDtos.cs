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

public record PeppolTransmissionEventDto(string Status, DateTime Timestamp, string? Detail);

public record PeppolTransmissionDto(
    Guid Id,
    Guid InvoiceId,
    string DocumentKind,
    string Status,
    string Environment,
    string ProviderKey,
    string? ProviderMessageId,
    int PayloadVersion,
    string? PayloadHash,
    string? ErrorDetail,
    int RetryCount,
    DateTime CreatedAt,
    IReadOnlyList<PeppolTransmissionEventDto> Events);

public record PeppolTransmissionListItemDto(
    Guid Id,
    Guid InvoiceId,
    string InvoiceNumber,
    string DocumentKind,
    string CustomerName,
    decimal Total,
    string Currency,
    DateOnly InvoiceDate,
    string Status,
    string Environment,
    string? ProviderMessageId,
    string? ErrorDetail,
    int RetryCount,
    int PayloadVersion,
    DateTime CreatedAt);

public record PeppolIncomingDocumentDto(
    Guid Id,
    string DocumentKind,
    string SupplierParticipant,
    string? SupplierName,
    string DocumentNumber,
    DateOnly? DocumentDate,
    decimal? Amount,
    string? Currency,
    string Status,
    string? ReviewNote,
    DateTime ReceivedAt);

public record PeppolIncomingDecisionRequest(string? Note);

/// <summary>One provider callback: a transmission status update or an inbound document.</summary>
public record PeppolWebhookRequest(
    string ProviderMessageId,
    string Kind,
    string? Status = null,
    string? Detail = null,
    PeppolWebhookIncomingDocument? Document = null);

public record PeppolWebhookIncomingDocument(
    string ReceiverParticipant,
    string? SupplierParticipant = null,
    string? SupplierName = null,
    string? DocumentKind = null,
    string? DocumentNumber = null,
    DateOnly? DocumentDate = null,
    decimal? Amount = null,
    string? Currency = null,
    string? PayloadXml = null);

public record PeppolWebhookOutcomeDto(bool Accepted, string? Reason);

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
