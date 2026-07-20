using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface ITrailerService
{
    Task<PagedResult<TrailerListItemDto>> SearchAsync(
        string? search, TrailerOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<TrailerOptionDto>> GetOptionsAsync(CancellationToken cancellationToken);

    Task<TrailerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TrailerOperationResult> CreateAsync(CreateTrailerRequest request, CancellationToken cancellationToken);

    Task<TrailerOperationResult> UpdateAsync(Guid id, UpdateTrailerRequest request, CancellationToken cancellationToken);

    Task<bool> SetActiveAsync(Guid id, SetTrailerActiveRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
