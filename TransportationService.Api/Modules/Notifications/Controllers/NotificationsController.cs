using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Notifications.Services;

namespace TransportationService.Api.Modules.Notifications.Controllers;

/// <summary>
/// Personal notifications: any authenticated user reads their own; no permission code needed
/// (the service scopes everything to the current user and tenant).
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationsController(INotificationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] bool unreadOnly = false, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListMineAsync(unreadOnly, take, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken cancellationToken)
    {
        return Ok(new { count = await _service.UnreadCountAsync(cancellationToken) });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        return await _service.MarkReadAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _service.MarkAllReadAsync(cancellationToken);
        return NoContent();
    }
}
