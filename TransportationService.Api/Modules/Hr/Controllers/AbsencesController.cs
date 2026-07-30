using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Hr.Controllers;

[ApiController]
public class AbsencesController : ControllerBase
{
    private readonly IAbsenceService _service;

    public AbsencesController(IAbsenceService service)
    {
        _service = service;
    }

    /// <summary>Range overview for planning; defaults to the coming 60 days.</summary>
    [HttpGet("api/absences")]
    [RequirePermission(PermissionCodes.AbsencesView)]
    public async Task<ActionResult<IReadOnlyList<AbsenceDto>>> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] AbsenceType? type,
        [FromQuery] AbsenceStatus? status,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(from, to, type, status, cancellationToken));
    }

    [HttpGet("api/employees/{employeeId:guid}/absences")]
    [RequirePermission(PermissionCodes.AbsencesView)]
    public async Task<ActionResult<IReadOnlyList<AbsenceDto>>> ListForEmployee(
        Guid employeeId, CancellationToken cancellationToken)
    {
        var absences = await _service.ListForEmployeeAsync(employeeId, cancellationToken);
        return absences is null ? NotFound() : Ok(absences);
    }

    [HttpPost("api/employees/{employeeId:guid}/absences")]
    [RequirePermission(PermissionCodes.AbsencesCreate)]
    public async Task<ActionResult<AbsenceDto>> Create(
        Guid employeeId, CreateAbsenceRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateForEmployeeAsync(employeeId, request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("api/absences/{id:guid}")]
    [RequirePermission(PermissionCodes.AbsencesEdit)]
    public async Task<ActionResult<AbsenceDto>> Update(
        Guid id, UpdateAbsenceRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("api/absences/{id:guid}/decision")]
    [RequirePermission(PermissionCodes.AbsencesApprove)]
    public async Task<ActionResult<AbsenceDto>> Decide(
        Guid id, DecideAbsenceRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.DecideAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("api/absences/{id:guid}/review")]
    [RequirePermission(PermissionCodes.AbsencesApprove)]
    public async Task<ActionResult<AbsenceDto>> StartReview(Guid id, CancellationToken cancellationToken)
    {
        return Handle(await _service.StartReviewAsync(id, cancellationToken), created: false);
    }

    [HttpPost("api/absences/{id:guid}/request-changes")]
    [RequirePermission(PermissionCodes.AbsencesApprove)]
    public async Task<ActionResult<AbsenceDto>> RequestChanges(
        Guid id, RequestAbsenceChangesRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.RequestChangesAsync(id, request, cancellationToken), created: false);
    }

    [HttpPut("api/absences/{id:guid}/internal-note")]
    [RequirePermission(PermissionCodes.AbsencesApprove)]
    public async Task<ActionResult<AbsenceDto>> SetInternalNote(
        Guid id, SetInternalNoteRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.SetInternalNoteAsync(id, request.Note, cancellationToken), created: false);
    }

    [HttpGet("api/absences/{id:guid}/review-context")]
    [RequirePermission(PermissionCodes.AbsencesApprove, PermissionCodes.AbsencesView)]
    public async Task<ActionResult<AbsenceReviewContextDto>> ReviewContext(Guid id, CancellationToken cancellationToken)
    {
        var context = await _service.GetReviewContextAsync(id, cancellationToken);
        return context is null ? NotFound() : Ok(context);
    }

    [HttpGet("api/absences/{id:guid}/attachment")]
    [RequirePermission(PermissionCodes.AbsencesView, PermissionCodes.AbsencesApprove)]
    public async Task<IActionResult> DownloadAttachment(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.OpenDocumentAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.MedicalRestricted)
        {
            return Forbid();
        }

        var contentType = Path.GetExtension(document.FileName!).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
        return File(document.Content!, contentType, document.FileName!);
    }

    [HttpPost("api/absences/{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.AbsencesEdit)]
    public async Task<ActionResult<AbsenceDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(id, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("api/absences/{id:guid}")]
    [RequirePermission(PermissionCodes.AbsencesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<AbsenceDto> Handle(AbsenceOperationResult result, bool created) => result.Outcome switch
    {
        AbsenceOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Absence),
        AbsenceOperationOutcome.Success => Ok(result.Absence),
        AbsenceOperationOutcome.NotFound => NotFound(),
        AbsenceOperationOutcome.OwnerNotFound => NotFound(new { message = "De medewerker bestaat niet." }),
        AbsenceOperationOutcome.Overlap =>
            Conflict(new { message = "De periode overlapt met een bestaande afwezigheid." }),
        AbsenceOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
        AbsenceOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
