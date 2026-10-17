using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IVehicleService
{
    Task<PagedResult<VehicleListItemDto>> SearchAsync(
        string? search, VehicleOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken, string? sort = null, string? dir = null);

    Task<IReadOnlyList<VehicleOptionDto>> GetOptionsAsync(CancellationToken cancellationToken);

    Task<VehicleDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<VehicleOperationResult> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken);

    Task<VehicleOperationResult> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken);

    Task<bool> SetActiveAsync(Guid id, SetVehicleActiveRequest request, CancellationToken cancellationToken);

    /// <summary>FL-B1: refused (with message + references) when anything still points at the vehicle.</summary>
    Task<FleetDeleteResult> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>FL-B1/M8: what references the vehicle; null when it does not exist in this tenant.</summary>
    Task<VehicleReferencesDto?> GetReferencesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>FL-M7: the vehicle's most recent trips (newest first) for the damage trip picker.</summary>
    Task<IReadOnlyList<TripReferenceDto>?> ListRecentTripsAsync(Guid id, int limit, CancellationToken cancellationToken);
}
