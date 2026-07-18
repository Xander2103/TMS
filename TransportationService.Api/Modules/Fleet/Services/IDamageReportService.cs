using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IDamageReportService
{
    Task<IReadOnlyList<DamageReportDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DamageReportDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken);

    /// <summary>Most recent reports across the fleet (dashboard feed).</summary>
    Task<IReadOnlyList<RecentDamageDto>> ListRecentAsync(int take, CancellationToken cancellationToken);

    Task<DamageOperationResult> CreateForVehicleAsync(Guid vehicleId, CreateDamageReportRequest request, CancellationToken cancellationToken);

    Task<DamageOperationResult> CreateForTrailerAsync(Guid trailerId, CreateDamageReportRequest request, CancellationToken cancellationToken);

    Task<DamageOperationResult> UpdateAsync(Guid id, UpdateDamageReportRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
