using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

[ApiController]
[Route("api/transport-orders")]
public class TransportOrdersController : ControllerBase
{
    private readonly ITransportOrderService _service;

    public TransportOrdersController(ITransportOrderService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.OrdersView)]
    public async Task<ActionResult<PagedResult<TransportOrderListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] TransportOrderStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchAsync(
            search, status, customerId, fromDate, toDate, PageRequest.Of(page, pageSize), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersView)]
    public async Task<ActionResult<TransportOrderDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _service.GetByIdAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.OrdersCreate)]
    public async Task<ActionResult<TransportOrderDetailDto>> Create(
        CreateTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersEdit)]
    public async Task<ActionResult<TransportOrderDetailDto>> Update(
        Guid id, UpdateTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.OrdersChangeStatus)]
    public async Task<ActionResult<TransportOrderDetailDto>> ChangeStatus(
        Guid id, ChangeTransportOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ChangeStatusAsync(id, request.Status, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Outcome switch
        {
            TransportOrderOperationOutcome.Success => NoContent(),
            TransportOrderOperationOutcome.NotFound => NotFound(),
            TransportOrderOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
    }

    private ActionResult<TransportOrderDetailDto> Handle(TransportOrderOperationResult result, bool created) =>
        result.Outcome switch
        {
            TransportOrderOperationOutcome.Success when created =>
                CreatedAtAction(nameof(GetById), new { id = result.Order!.Id }, result.Order),
            TransportOrderOperationOutcome.Success => Ok(result.Order),
            TransportOrderOperationOutcome.NotFound => NotFound(),
            TransportOrderOperationOutcome.InvalidReference => BadRequest(new { message = result.Error }),
            TransportOrderOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            TransportOrderOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
}
