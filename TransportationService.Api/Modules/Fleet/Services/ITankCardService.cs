using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface ITankCardService
{
    /// <summary>
    /// <paramref name="available"/> restricts the result to unassigned, unblocked, non-expired
    /// cards (EmployeeId == null) — used by the "link existing card" picker on the employee side.
    /// </summary>
    Task<PagedResult<TankCardDto>> SearchAsync(
        string? search, TankCardStatus? status, bool available, PageRequest page, CancellationToken cancellationToken);

    Task<TankCardDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>All cards currently linked to this employee (tenant-scoped), ordered like SearchAsync.</summary>
    Task<IReadOnlyList<TankCardDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    Task<TankCardOperationResult> CreateAsync(CreateTankCardRequest request, CancellationToken cancellationToken);

    Task<TankCardOperationResult> UpdateAsync(Guid id, UpdateTankCardRequest request, CancellationToken cancellationToken);

    Task<TankCardOperationResult> SetBlockedAsync(Guid id, SetTankCardBlockedRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
