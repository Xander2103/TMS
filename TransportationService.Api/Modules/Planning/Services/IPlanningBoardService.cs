using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Planning.Dtos;

namespace TransportationService.Api.Modules.Planning.Services;

/// <summary>
/// Read models for the dispatcher planning center. Ranges are bounded (max 31 days) and the
/// projections are batched — trip/order/resource data loads in a fixed number of queries.
/// Conflict counts are recomputed live through ONE batched conflict-engine pass per window
/// (PERF-BLOCKER 3; the same correctness-first behavior as the trip list). Lists that hit
/// their cap say so through <c>Truncated</c> / <c>Limit</c> (PERF-HIGH 3).
/// </summary>
public interface IPlanningBoardService
{
    Task<PlanningBoardDto> GetBoardAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    Task<PagedResult<UnplannedOrderDto>> GetUnplannedOrdersAsync(
        UnplannedOrdersQuery query, CancellationToken cancellationToken);

    Task<PlanningResourcesDto> GetResourcesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
