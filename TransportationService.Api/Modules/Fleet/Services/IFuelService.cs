using TransportationService.Api.Modules.Fleet.Dtos;

namespace TransportationService.Api.Modules.Fleet.Services;

public interface IFuelService
{
    /// <summary>Full fuel history for a vehicle, newest first, with computed consumption, warnings and aggregates. Null when the vehicle is not in the tenant.</summary>
    Task<FuelOverviewDto?> ListForVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);

    /// <summary>Recent transactions with at least one anomaly across the whole fleet, for the dashboard.</summary>
    Task<IReadOnlyList<FuelWarningDto>> ListRecentWarningsAsync(int take, CancellationToken cancellationToken);

    Task<FuelOperationResult> CreateForVehicleAsync(Guid vehicleId, CreateFuelTransactionRequest request, CancellationToken cancellationToken);

    Task<FuelOperationResult> UpdateAsync(Guid id, UpdateFuelTransactionRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
