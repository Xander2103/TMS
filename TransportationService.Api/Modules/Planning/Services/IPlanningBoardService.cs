using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Planning.Dtos;

namespace TransportationService.Api.Modules.Planning.Services;

/// <summary>
/// Read models for the dispatcher planning center. Ranges are bounded (max 31 days) and the
/// projections are batched — trip/order/resource data loads in a fixed number of queries.
/// Conflict counts are recomputed live per trip through the conflict engine (the same
/// correctness-first behavior as the existing trip list).
/// </summary>
public interface IPlanningBoardService
{
    Task<PlanningBoardDto> GetBoardAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    Task<PagedResult<UnplannedOrderDto>> GetUnplannedOrdersAsync(
        UnplannedOrdersQuery query, CancellationToken cancellationToken);

    Task<PlanningResourcesDto> GetResourcesAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
