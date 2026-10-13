using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.CustomerPortal.Controllers;

/// <summary>
/// Staff side of portal messages: authoring, delivery status, withdrawal. Multi-customer
/// fan-out additionally requires portal_messages.send_bulk (service-side gate).
/// </summary>
[ApiController]
[Route("api/portal-messages")]
public class PortalMessagesController : ControllerBase
{
    private readonly IPortalMessageService _service;

    public PortalMessagesController(IPortalMessageService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.PortalMessagesSend)]
    public async Task<ActionResult<PortalMessageAdminDto>> Send(SendPortalMessageRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.SendAsync(request, cancellationToken));
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.PortalMessagesView, PermissionCodes.PortalMessagesSend)]
    public async Task<ActionResult<IReadOnlyList<PortalMessageAdminDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(cancellationToken));
    }

    [HttpGet("{id:guid}/delivery-status")]
    [RequirePermission(PermissionCodes.PortalMessagesView, PermissionCodes.PortalMessagesSend)]
    public async Task<ActionResult<PortalMessageDeliveryStatusDto>> DeliveryStatus(Guid id, CancellationToken cancellationToken)
    {
        var status = await _service.GetDeliveryStatusAsync(id, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.PortalMessagesSend, PermissionCodes.PortalMessagesCancel)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        return await _service.CancelAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}

/// <summary>
/// Portal side: the caller's own message feed. Everything is scoped to the caller's
/// customer link; a foreign message id is indistinguishable from a missing one (404).
/// </summary>
[ApiController]
[Route("api/customer-portal/portal-messages")]
[RequirePermission(PermissionCodes.CustomerPortalView)]
public class CustomerPortalMessagesFeedController : ControllerBase
{
    private readonly IPortalMessageService _service;

    public CustomerPortalMessagesFeedController(IPortalMessageService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortalMessageFeedItemDto>>> List(CancellationToken cancellationToken)
    {
        var feed = await _service.ListFeedAsync(cancellationToken);
        return feed is null ? Forbid() : Ok(feed);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken cancellationToken)
    {
        var count = await _service.FeedUnreadCountAsync(cancellationToken);
        return count is null ? Forbid() : Ok(new { count });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        return await _service.MarkFeedReadAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        return await _service.AcknowledgeFeedAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
