using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Entities;

namespace TransportationService.Api.Modules.TripCosting.Services;

public interface ITripCostingService
{
    Task<TripCostingDto?> GetAsync(Guid tripId, bool includeProfitability, CancellationToken cancellationToken);

    /// <summary>Estimated: only while Draft/Planned. Actual: only from InProgress onwards. Refused when finalized.</summary>
    Task<CostingOperationResult> RecalculateAsync(Guid tripId, TripCostPhase phase, CancellationToken cancellationToken);

    Task<CostingOperationResult> AddManualLineAsync(Guid tripId, AddCostLineRequest request, CancellationToken cancellationToken);

    Task<CostingOperationResult> OverrideLineAsync(
        Guid tripId, Guid lineId, OverrideCostLineRequest request, CancellationToken cancellationToken);

    Task<CostingOperationResult> DeleteLineAsync(Guid tripId, Guid lineId, CancellationToken cancellationToken);

    Task<CostingOperationResult> UpdateActualsAsync(
        Guid tripId, UpdateTripActualsRequest request, CancellationToken cancellationToken);

    Task<CostingOperationResult> FinalizeAsync(Guid tripId, CancellationToken cancellationToken);

    Task<CostingOperationResult> ReopenAsync(Guid tripId, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a recalculation on the shared DbContext without saving — used by trip lifecycle
    /// hooks so costing rides in the same transaction. Silently skips when the phase does not
    /// apply to the trip's status or the costing is finalized.
    /// </summary>
    Task StageRecalculationAsync(Trip trip, TripCostPhase phase, CancellationToken cancellationToken);
}
