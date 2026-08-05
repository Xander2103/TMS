using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;

namespace TransportationService.Api.Modules.Locations.Services;

public interface ILocationService
{
    Task<PagedResult<LocationListItemDto>> SearchAsync(
        string? search, LocationType? type, bool? isActive, Guid? customerId,
        string? country, string? postalCode,
        string? sort, string? dir, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<LocationOptionDto>> GetOptionsAsync(LocationType? type, Guid? customerId, CancellationToken cancellationToken);

    /// <param name="canViewSensitive">When false, the sensitive AccessCode is returned as null.</param>
    Task<LocationDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken, bool canViewSensitive = false);

    /// <param name="canViewSensitive">When false, the incoming AccessCode is ignored and the field is null in the returned detail.</param>
    Task<LocationOperationResult> CreateAsync(CreateLocationRequest request, CancellationToken cancellationToken, bool canViewSensitive = false);

    /// <param name="canViewSensitive">When false, the stored AccessCode is preserved untouched and returned as null.</param>
    Task<LocationOperationResult> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken cancellationToken, bool canViewSensitive = false);

    Task<bool> SetActiveAsync(Guid id, SetLocationActiveRequest request, CancellationToken cancellationToken);

    Task<LocationOperationResult> SetDefaultsAsync(Guid id, SetLocationDefaultsRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a location's master data and opening intervals into a new location with a
    /// generated code, "&#160;(kopie)" name suffix and cleared default flags.
    /// </summary>
    Task<LocationOperationResult> DuplicateAsync(Guid id, CancellationToken cancellationToken, bool canViewSensitive = false);
}
