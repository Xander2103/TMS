using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Drivers.Dtos;

namespace TransportationService.Api.Modules.Drivers.Services;

public interface IDriverService
{
    Task<PagedResult<DriverListItemDto>> SearchAsync(
        string? search, bool? isActive, bool? isBlocked, Guid? categoryId,
        Entities.DriverAvailabilityStatus? availabilityStatus, Guid? qualificationTypeId,
        int? expiringWithinDays, bool hasExpired, bool eligibleOnly,
        PageRequest page, CancellationToken cancellationToken);

    Task<DriverDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<DriverOperationResult> CreateAsync(CreateDriverRequest request, CancellationToken cancellationToken);

    Task<DriverOperationResult> UpdateAsync(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken);

    Task<DriverOperationResult> SetBlockedAsync(Guid id, SetDriverBlockedRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
