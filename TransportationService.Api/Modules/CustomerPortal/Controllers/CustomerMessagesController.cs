using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.CustomerPortal.Controllers;

/// <summary>
/// Internal (staff) side of the customer/portal messages thread — the counterpart of the
/// portal's own <c>api/customer-portal/messages</c> endpoints. The customer id is explicit
/// (staff address any customer); each call still validates it exists in the current tenant.
/// </summary>
[ApiController]
[Route("api/customers/{customerId:guid}/messages")]
public class CustomerMessagesController : ControllerBase
{
    private readonly ICustomerMessageService _service;

    public CustomerMessagesController(ICustomerMessageService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.CustomerMessagesView)]
    public async Task<ActionResult<IReadOnlyList<CustomerMessageDto>>> List(
        Guid customerId, [FromQuery] Guid? orderId, CancellationToken cancellationToken)
    {
        var messages = await _service.ListForCustomerAsync(customerId, orderId, cancellationToken);
        return messages is null ? NotFound() : Ok(messages);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.CustomerMessagesSend)]
    public async Task<ActionResult<CustomerMessageDto>> Send(
        Guid customerId, SendCustomerMessageRequest request, CancellationToken cancellationToken)
    {
        var sent = await _service.SendToCustomerAsync(customerId, request, cancellationToken);
        return sent is null ? NotFound() : Ok(sent);
    }

    [HttpPost("read")]
    [RequirePermission(PermissionCodes.CustomerMessagesView)]
    public async Task<IActionResult> MarkRead(
        Guid customerId, MarkMessagesReadRequest request, CancellationToken cancellationToken)
    {
        return await _service.MarkCustomerReadAsync(customerId, request.OrderId, cancellationToken)
            ? NoContent() : NotFound();
    }

    [HttpGet("unread-count")]
    [RequirePermission(PermissionCodes.CustomerMessagesView)]
    public async Task<ActionResult<PortalUnreadCountDto>> UnreadCount(Guid customerId, CancellationToken cancellationToken)
    {
        var count = await _service.GetCustomerUnreadCountAsync(customerId, cancellationToken);
        return count is null ? NotFound() : Ok(new PortalUnreadCountDto(count.Value));
    }
}
