using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Portal.Services;
using TransportationService.Api.Modules.Qualifications.Dtos;

namespace TransportationService.Api.Modules.Portal.Controllers;

/// <summary>
/// The employee portal surface. Deliberately no permission codes: every endpoint is
/// self-scoped through the user's employee link, so authentication is the only gate and
/// nobody can reach anyone else's data here.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly IPortalService _service;

    public MeController(IPortalService service)
    {
        _service = service;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<MyProfileDto>> Profile(CancellationToken cancellationToken)
    {
        var profile = await _service.GetMyProfileAsync(cancellationToken);
        return profile is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(profile);
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<MyDashboardDto>> Dashboard(CancellationToken cancellationToken)
    {
        var dashboard = await _service.GetMyDashboardAsync(cancellationToken);
        return dashboard is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(dashboard);
    }

    [HttpGet("absences")]
    public async Task<ActionResult<IReadOnlyList<AbsenceDto>>> Absences(CancellationToken cancellationToken)
    {
        var absences = await _service.ListMyAbsencesAsync(cancellationToken);
        return absences is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(absences);
    }

    [HttpPost("absences")]
    public async Task<ActionResult<AbsenceDto>> CreateAbsence(
        CreateAbsenceRequest request, CancellationToken cancellationToken)
    {
        return HandleAbsence(await _service.CreateMyAbsenceAsync(request, cancellationToken));
    }

    /// <summary>Own leave balance (read-only, self-scoped). Requires the view-own permission.</summary>
    [HttpGet("leave-balance")]
    [RequirePermission(PermissionCodes.LeaveBalancesViewOwn)]
    public async Task<ActionResult<EmployeeLeaveBalanceDto>> LeaveBalance([FromQuery] int? year, CancellationToken cancellationToken)
    {
        var resolved = year ?? DateTime.UtcNow.Year;
        var balance = await _service.GetMyLeaveBalanceAsync(resolved, cancellationToken);
        return balance is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(balance);
    }

    [HttpPost("absences/{id:guid}/cancel")]
    public async Task<ActionResult<AbsenceDto>> CancelAbsence(Guid id, CancellationToken cancellationToken)
    {
        return HandleAbsence(await _service.CancelMyAbsenceAsync(id, cancellationToken));
    }

    private const long MaxAttachmentBytes = 10 * 1024 * 1024;

    [HttpPost("absences/{id:guid}/attachment")]
    [RequestSizeLimit(MaxAttachmentBytes)]
    public async Task<ActionResult<AbsenceDto>> AttachAbsenceDocument(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxAttachmentBytes)
        {
            return BadRequest(new { message = "De bijlage moet tussen 1 byte en 10 MB groot zijn." });
        }

        await using var stream = file.OpenReadStream();
        return HandleAbsence(await _service.AttachMyAbsenceDocumentAsync(id, file.FileName, stream, cancellationToken));
    }

    [HttpGet("absences/{id:guid}/attachment")]
    public async Task<IActionResult> DownloadAbsenceDocument(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.OpenMyAbsenceDocumentAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(document.Value.FileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        return File(document.Value.Content, contentType, document.Value.FileName);
    }

    [HttpGet("planning")]
    public async Task<IActionResult> Planning(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            return BadRequest(new { message = "Kies een geldige periode van maximaal 62 dagen." });
        }

        var days = await _service.GetMyPlanningAsync(from, to, cancellationToken);
        return days is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(days);
    }

    // --- Personal calendar notes (self-service; strictly own-employee scoped) ---

    [HttpGet("calendar-notes")]
    public async Task<IActionResult> ListCalendarNotes(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var notes = await _service.ListMyCalendarNotesAsync(from, to, cancellationToken);
        return notes is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(notes);
    }

    [HttpPost("calendar-notes")]
    public async Task<IActionResult> CreateCalendarNote(
        Dtos.SavePersonalCalendarNoteRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateMyCalendarNoteAsync(request, cancellationToken);
        return created is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(created);
    }

    [HttpPut("calendar-notes/{id:guid}")]
    public async Task<IActionResult> UpdateCalendarNote(
        Guid id, Dtos.SavePersonalCalendarNoteRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateMyCalendarNoteAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("calendar-notes/{id:guid}")]
    public async Task<IActionResult> DeleteCalendarNote(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteMyCalendarNoteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>Own planning as iCalendar — import/subscribe from any calendar client.</summary>
    [HttpGet("planning/ics")]
    public async Task<IActionResult> PlanningIcs(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62)
        {
            return BadRequest(new { message = "Kies een geldige periode van maximaal 62 dagen." });
        }

        var days = await _service.GetMyPlanningAsync(from, to, cancellationToken);
        if (days is null)
        {
            return NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." });
        }

        var ics = Services.PlanningIcsBuilder.Build(days);
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar", "planning.ics");
    }

    [HttpGet("qualifications")]
    public async Task<ActionResult<IReadOnlyList<EmployeeQualificationDto>>> Qualifications(CancellationToken cancellationToken)
    {
        var qualifications = await _service.ListMyQualificationsAsync(cancellationToken);
        return qualifications is null
            ? NotFound(new { message = "Er is geen personeelsdossier gekoppeld aan dit account." })
            : Ok(qualifications);
    }

    [HttpGet("qualifications/{id:guid}/document")]
    public async Task<IActionResult> QualificationDocument(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.OpenMyQualificationDocumentAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var extension = Path.GetExtension(document.Value.FileName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        return File(document.Value.Content, contentType, document.Value.FileName);
    }

    [HttpPost("password")]
    [TransportationService.Api.Modules.Identity.Authorization.PermitWhenPasswordChangeRequired]
    public async Task<IActionResult> ChangePassword(
        ChangeMyPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ChangeMyPasswordAsync(request, cancellationToken);
        return result.Outcome switch
        {
            PortalOutcome.Success => NoContent(),
            PortalOutcome.NotFound => NotFound(),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    private ActionResult<AbsenceDto> HandleAbsence(PortalAbsenceResult result) => result.Outcome switch
    {
        PortalOutcome.Success => Ok(result.Absence),
        PortalOutcome.NotFound => NotFound(),
        PortalOutcome.NoEmployeeLink => NotFound(new { message = result.Error }),
        PortalOutcome.InvalidState => BadRequest(new { message = result.Error }),
        _ => BadRequest(new { message = result.Error }),
    };
}
