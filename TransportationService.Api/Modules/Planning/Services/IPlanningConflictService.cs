using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;

namespace TransportationService.Api.Modules.Planning.Services;

/// <summary>
/// The planning conflict engine: evaluates a trip's resource assignment against absences,
/// driver readiness, fleet state, double bookings and order requirements.
/// </summary>
public interface IPlanningConflictService
{
    Task<IReadOnlyList<PlanningConflictDto>> EvaluateAsync(Trip trip, CancellationToken cancellationToken);

    /// <summary>
    /// Same evaluation for work that is not (only) described by the trip's orders: a standalone
    /// dossier activity probes with a transient trip and states its own crane requirement here.
    /// </summary>
    Task<IReadOnlyList<PlanningConflictDto>> EvaluateAsync(Trip trip, bool requiresCraneOperator, CancellationToken cancellationToken);

    /// <summary>
    /// Set-based evaluation for boards and lists (PERF-BLOCKER 3): the facts of ALL trips are
    /// loaded in a fixed number of statements and every trip is judged in memory with the same
    /// rules as <see cref="EvaluateAsync(Trip, CancellationToken)"/>. The result holds one entry
    /// per distinct trip id, in the same order and with the same conflicts the single-trip call
    /// would produce.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<PlanningConflictDto>>> EvaluateManyAsync(
        IReadOnlyList<Trip> trips, CancellationToken cancellationToken);
}
