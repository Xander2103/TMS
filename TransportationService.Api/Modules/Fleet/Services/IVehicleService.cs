using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IVehicleService
{
    Task<PagedResult<VehicleListItemDto>> SearchAsync(
        string? search, VehicleOperationalStatus? status, bool? isActive, Guid? categoryId,
        PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<VehicleOptionDto>> GetOptionsAsync(CancellationToken cancellationToken);

    Task<VehicleDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<VehicleOperationResult> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken);

    Task<VehicleOperationResult> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken);

    Task<bool> SetActiveAsync(Guid id, SetVehicleActiveRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
