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
