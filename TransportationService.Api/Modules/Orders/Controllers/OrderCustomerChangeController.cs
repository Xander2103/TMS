using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>
/// Sprint 6A — moving an order to the customer it really belongs to, once that becomes known.
/// The preview is a separate call so the user sees the consequences before confirming.
/// </summary>
[ApiController]
[Route("api/transport-orders/{id:guid}/customer")]
public class OrderCustomerChangeController : ControllerBase
{
    private readonly IOrderCustomerChangeService _service;

    public OrderCustomerChangeController(IOrderCustomerChangeService service)
    {
        _service = service;
    }

    /// <summary>What the change would do; never writes.</summary>
    [HttpGet("impact")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<CustomerChangeImpactDto>> Impact(
        Guid id, [FromQuery] Guid newCustomerId, CancellationToken cancellationToken)
    {
        var impact = await _service.PreviewAsync(id, newCustomerId, cancellationToken);
        return impact is null ? NotFound() : Ok(impact);
    }

    [HttpPut]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<CustomerChangeImpactDto>> Change(
        Guid id, ChangeOrderCustomerRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ApplyAsync(id, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
