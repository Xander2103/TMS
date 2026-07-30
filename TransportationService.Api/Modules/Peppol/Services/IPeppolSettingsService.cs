using TransportationService.Api.Modules.Peppol.Dtos;

namespace TransportationService.Api.Modules.Peppol.Services;

public interface IPeppolSettingsService
{
    /// <summary>Reads the settings for one legal entity; defaults (disabled/sandbox) when none saved yet.</summary>
    Task<PeppolSettingsDto> GetSettingsAsync(Guid legalEntityId, CancellationToken cancellationToken);

    /// <summary>Upserts the settings for one legal entity; audited old/new (no secrets to leak).</summary>
    Task<PeppolSettingsDto> SaveSettingsAsync(Guid legalEntityId, SavePeppolSettingsRequest request, CancellationToken cancellationToken);

    /// <summary>Per-legal-entity configuration checklist, transmission counts and customer-data gaps.</summary>
    Task<PeppolOverviewDto> GetOverviewAsync(CancellationToken cancellationToken);

    /// <summary>Resolves the legal entity's provider and validates ITS OWN participant identity.</summary>
    Task<PeppolTestConnectionResultDto> TestConnectionAsync(Guid legalEntityId, CancellationToken cancellationToken);
}
