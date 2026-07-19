using TransportationService.Api.Modules.Planning.Dtos;

namespace TransportationService.Api.Modules.Planning.Services;

public interface ITripExecutionService
{
    /// <summary>Trips assigned to the current user's driver profile (empty when the user is no driver).</summary>
    Task<IReadOnlyList<MyTripDto>> ListMyTripsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    /// <summary>Execution view of a trip. <paramref name="restrictToOwnDriver"/> enforces trip ownership for drivers.</summary>
    Task<ExecutionResult> GetExecutionAsync(Guid tripId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>
    /// Controlled status transition through the stop-status machine. Rejects invalid moves,
    /// demands reasons where the model requires them and writes a StopStatusHistory row per step.
    /// </summary>
    Task<ExecutionResult> TransitionAsync(Guid tripId, Guid stopId, TransitionStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Chronological status history of one stop within one trip.</summary>
    Task<StopHistoryResult> GetStopHistoryAsync(Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<ExecutionResult> ArriveAsync(Guid tripId, Guid stopId, bool restrictToOwnDriver, CancellationToken cancellationToken);

    /// <summary>Completes a stop; completing the last open stop auto-completes the trip and its orders.</summary>
    Task<ExecutionResult> CompleteAsync(Guid tripId, Guid stopId, CompleteStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken);

    Task<ExecutionResult> SkipAsync(Guid tripId, Guid stopId, SkipStopRequest request, bool restrictToOwnDriver, CancellationToken cancellationToken);
}
