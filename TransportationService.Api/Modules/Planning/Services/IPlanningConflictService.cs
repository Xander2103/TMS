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
}
