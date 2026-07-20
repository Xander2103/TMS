using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.CustomerPortal.Controllers;

/// <summary>
/// Customer-portal endpoints. The customer context comes exclusively from the authenticated
/// user's linked customer; a client-supplied customer id is never accepted anywhere.
/// </summary>
[ApiController]
[Route("api/customer-portal")]
public class CustomerPortalController : ControllerBase
{
    private readonly ICustomerPortalService _service;

    public CustomerPortalController(ICustomerPortalService service)
    {
        _service = service;
    }

    [HttpGet("context")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalContextDto>> Context(CancellationToken cancellationToken) =>
        Handle(await _service.GetContextAsync(cancellationToken));

    [HttpGet("orders")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<IReadOnlyList<PortalOrderListItemDto>>> Orders(CancellationToken cancellationToken) =>
        Handle(await _service.ListMyOrdersAsync(cancellationToken));

    [HttpGet("orders/{id:guid}")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<PortalOrderDetailDto>> Order(Guid id, CancellationToken cancellationToken) =>
        Handle(await _service.GetMyOrderAsync(id, cancellationToken));

    [HttpPost("orders")]
    [RequirePermission(PermissionCodes.CustomerPortalSubmitOrders)]
    public async Task<ActionResult<PortalOrderDetailDto>> SubmitOrder(
        PortalCreateOrderRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.SubmitOrderAsync(request, cancellationToken));

    [HttpGet("locations")]
    [RequirePermission(PermissionCodes.CustomerPortalView)]
    public async Task<ActionResult<IReadOnlyList<PortalLocationDto>>> Locations(CancellationToken cancellationToken) =>
        Handle(await _service.ListMyLocationsAsync(cancellationToken));

    [HttpPost("locations")]
    [RequirePermission(PermissionCodes.CustomerPortalManageLocations)]
    public async Task<ActionResult<PortalLocationDto>> CreateLocation(
        PortalCreateLocationRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.CreateMyLocationAsync(request, cancellationToken));

    private ActionResult<T> Handle<T>(PortalResult<T> result) where T : class => result.Outcome switch
    {
        PortalOutcomeKind.Success => Ok(result.Value),
        PortalOutcomeKind.NoCustomerLink => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        PortalOutcomeKind.NotFound => NotFound(),
        _ => BadRequest(new { message = result.Error }),
    };
}
