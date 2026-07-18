using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface ITankCardService
{
    Task<PagedResult<TankCardDto>> SearchAsync(
        string? search, TankCardStatus? status, PageRequest page, CancellationToken cancellationToken);

    Task<TankCardDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TankCardOperationResult> CreateAsync(CreateTankCardRequest request, CancellationToken cancellationToken);

    Task<TankCardOperationResult> UpdateAsync(Guid id, UpdateTankCardRequest request, CancellationToken cancellationToken);

    Task<TankCardOperationResult> SetBlockedAsync(Guid id, SetTankCardBlockedRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
