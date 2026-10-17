using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface ITrailerService
{
    Task<PagedResult<TrailerListItemDto>> SearchAsync(
        string? search, TrailerOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken, string? sort = null, string? dir = null);

    Task<IReadOnlyList<TrailerOptionDto>> GetOptionsAsync(CancellationToken cancellationToken);

    Task<TrailerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TrailerOperationResult> CreateAsync(CreateTrailerRequest request, CancellationToken cancellationToken);

    Task<TrailerOperationResult> UpdateAsync(Guid id, UpdateTrailerRequest request, CancellationToken cancellationToken);

    Task<bool> SetActiveAsync(Guid id, SetTrailerActiveRequest request, CancellationToken cancellationToken);

    /// <summary>FL-B1: refused (with message + references) when anything still points at the trailer.</summary>
    Task<FleetDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>FL-B1/M8: what references the trailer (same shape as vehicles); null when unknown.</summary>
    Task<VehicleReferencesDto?> GetReferencesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>FL-M7: the trailer's most recent trips (newest first) for the damage trip picker.</summary>
    Task<IReadOnlyList<TripReferenceDto>?> ListRecentTripsAsync(Guid id, int limit, CancellationToken cancellationToken);
}
