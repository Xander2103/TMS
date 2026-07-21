using TransportationService.Api.Modules.Organization.Dtos;

namespace TransportationService.Api.Modules.Organization.Services;

public interface ILegalEntityService
{
    Task<IReadOnlyList<LegalEntityDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<IReadOnlyList<LegalEntityOptionDto>> ListOptionsAsync(CancellationToken cancellationToken);
    Task<LegalEntityDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<LegalEntityDto> CreateAsync(SaveLegalEntityRequest request, CancellationToken cancellationToken);
    Task<LegalEntityDto?> UpdateAsync(Guid id, SaveLegalEntityRequest request, CancellationToken cancellationToken);
    Task<LegalEntityDto?> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken);

    Task<LegalEntityDto?> AttachLogoAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName, string ContentType)?> OpenLogoAsync(Guid id, CancellationToken cancellationToken);
    Task<LegalEntityDto?> RemoveLogoAsync(Guid id, CancellationToken cancellationToken);

    Task<ActiveLegalEntityDto> GetActiveSelectionAsync(CancellationToken cancellationToken);
    Task<ActiveLegalEntityDto> SetActiveSelectionAsync(Guid? legalEntityId, CancellationToken cancellationToken);
}
