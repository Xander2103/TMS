using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Notifications.Services;

namespace TransportationService.Api.Modules.Notifications.Controllers;

/// <summary>
/// Internal in-app messages: sending needs messages.send; reading your own inbox only needs
/// authentication (every user has an inbox).
/// </summary>
[ApiController]
[Route("api/internal-messages")]
public class InternalMessagesController : ControllerBase
{
    private readonly IInternalMessageService _service;

    public InternalMessagesController(IInternalMessageService service)
    {
        _service = service;
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.MessagesSend)]
    public async Task<ActionResult<object>> Send(SendInternalMessageRequest request, CancellationToken cancellationToken)
    {
        var recipients = await _service.SendAsync(request, cancellationToken);
        return Ok(new { recipients });
    }

    [HttpGet("inbox")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<InternalMessageDto>>> Inbox(CancellationToken cancellationToken) =>
        Ok(await _service.ListInboxAsync(cancellationToken));

    [HttpGet("sent")]
    [RequirePermission(PermissionCodes.MessagesSend)]
    public async Task<ActionResult<IReadOnlyList<InternalMessageDto>>> Sent(CancellationToken cancellationToken) =>
        Ok(await _service.ListSentAsync(cancellationToken));

    [HttpGet("unread-count")]
    [Authorize]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken cancellationToken) =>
        Ok(new { count = await _service.UnreadCountAsync(cancellationToken) });

    [HttpPost("{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        await _service.MarkReadAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/acknowledge")]
    [Authorize]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        await _service.AcknowledgeAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("{id:guid}/delivery-status")]
    [RequirePermission(PermissionCodes.MessagesSend, PermissionCodes.MessagesViewDeliveryStatus)]
    public async Task<ActionResult<MessageDeliveryStatusDto>> DeliveryStatus(Guid id, CancellationToken cancellationToken)
    {
        // Service scopes to own messages unless the caller holds messages.view_delivery_status.
        var status = await _service.GetDeliveryStatusAsync(id, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.MessagesSend, PermissionCodes.MessagesCancel)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        await _service.CancelAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("recipients")]
    [RequirePermission(PermissionCodes.MessagesSend)]
    public async Task<ActionResult<IReadOnlyList<MessageRecipientOptionDto>>> Recipients(CancellationToken cancellationToken) =>
        Ok(await _service.ListRecipientOptionsAsync(cancellationToken));
}
