using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Services;

public interface ILocationService
{
    Task<PagedResult<LocationListItemDto>> SearchAsync(
        string? search, LocationType? type, bool? isActive, Guid? customerId,
        string? sort, string? dir, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocationOptionDto>> GetOptionsAsync(LocationType? type, CancellationToken cancellationToken);

    Task<LocationDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<LocationOperationResult> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken);

    Task<LocationOperationResult> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
