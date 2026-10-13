using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Services;

namespace TransportationService.Api.Modules.Planning.Controllers;

/// <summary>Read models behind the dispatcher planning center (/planning-center).</summary>
[ApiController]
[Route("api/planning-board")]
public class PlanningBoardController : ControllerBase
{
    private readonly IPlanningBoardService _service;

    public PlanningBoardController(IPlanningBoardService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<PlanningBoardDto>> Board(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetBoardAsync(from, to, cancellationToken));
    }

    [HttpGet("unplanned-orders")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<PagedResult<UnplannedOrderDto>>> UnplannedOrders(
        [FromQuery] string? search,
        [FromQuery] Guid? customerId,
        [FromQuery] TransportOrderStatus? status,
        [FromQuery] OrderPriority? priority,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] bool onlyWithAttention,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new UnplannedOrdersQuery(
            search, customerId, status, priority, fromDate, toDate, onlyWithAttention,
            page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize);
        return Ok(await _service.GetUnplannedOrdersAsync(query, cancellationToken));
    }

    [HttpGet("resources")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<PlanningResourcesDto>> Resources(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        return Ok(await _service.GetResourcesAsync(from, to, cancellationToken));
    }
}
