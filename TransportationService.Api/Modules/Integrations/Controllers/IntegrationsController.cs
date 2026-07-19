using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Integrations.Entities;
using TransportationService.Api.Modules.Integrations.Services;

namespace TransportationService.Api.Modules.Integrations.Controllers;

/// <summary>
/// Admin surface for integration sync queues. Today that is the Outlook/Exchange calendar
/// queue (fake provider in development); real Graph credentials plug into ICalendarProvider.
/// </summary>
[ApiController]
public class IntegrationsController : ControllerBase
{
    private readonly QueueingCalendarSyncService _calendarSync;

    public IntegrationsController(QueueingCalendarSyncService calendarSync)
    {
        _calendarSync = calendarSync;
    }

    [HttpGet("api/integrations/calendar-sync")]
    [RequirePermission(PermissionCodes.IntegrationsManage)]
    public async Task<IActionResult> ListCalendarSync(
        [FromQuery] CalendarSyncStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _calendarSync.ListAsync(status, page, pageSize, cancellationToken));
    }

    [HttpPost("api/integrations/calendar-sync/{id:guid}/retry")]
    [RequirePermission(PermissionCodes.IntegrationsManage)]
    public async Task<IActionResult> RetryCalendarSync(Guid id, CancellationToken cancellationToken)
    {
        var retried = await _calendarSync.RetryAsync(id, cancellationToken);
        return retried
            ? NoContent()
            : BadRequest(new { message = "Alleen mislukte items kunnen opnieuw worden aangeboden." });
    }
}
