using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IMaintenanceService
{
    Task<IReadOnlyList<MaintenanceRecordDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaintenanceRecordDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken);

    /// <summary>Open (planned/in-progress) jobs that are overdue or due within the window, most urgent first.</summary>
    Task<IReadOnlyList<DueMaintenanceDto>> ListDueAsync(int withinDays, CancellationToken cancellationToken);

    Task<MaintenanceOperationResult> CreateForVehicleAsync(Guid vehicleId, CreateMaintenanceRequest request, CancellationToken cancellationToken);

    Task<MaintenanceOperationResult> CreateForTrailerAsync(Guid trailerId, CreateMaintenanceRequest request, CancellationToken cancellationToken);

    Task<MaintenanceOperationResult> UpdateAsync(Guid id, UpdateMaintenanceRequest request, CancellationToken cancellationToken);

    /// <summary>Marks a job completed; when it carries an interval, plans and returns the follow-up job.</summary>
    Task<MaintenanceOperationResult> CompleteAsync(Guid id, CompleteMaintenanceRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
