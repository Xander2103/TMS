using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IInspectionService
{
    Task<IReadOnlyList<InspectionDto>?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InspectionDto>?> ListForTrailerAsync(Guid trailerId, CancellationToken cancellationToken);

    /// <summary>Open inspections that are overdue or due within the window, most urgent first.</summary>
    Task<IReadOnlyList<DueInspectionDto>> ListDueAsync(int withinDays, CancellationToken cancellationToken);

    Task<InspectionOperationResult> CreateForVehicleAsync(Guid vehicleId, CreateInspectionRequest request, CancellationToken cancellationToken);

    Task<InspectionOperationResult> CreateForTrailerAsync(Guid trailerId, CreateInspectionRequest request, CancellationToken cancellationToken);

    Task<InspectionOperationResult> UpdateAsync(Guid id, UpdateInspectionRequest request, CancellationToken cancellationToken);

    /// <summary>Records the result; when the inspection carries an interval, plans and returns the next one.</summary>
    Task<InspectionOperationResult> CompleteAsync(Guid id, CompleteInspectionRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
