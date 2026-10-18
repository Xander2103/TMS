using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Exceptions.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Exceptions.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Exceptions.Controllers;

/// <summary>
/// Structured execution exceptions. Drivers report on their own trips (exceptions.create);
/// staff with exceptions.resolve work the queue and may act on any trip of the tenant.
/// </summary>
[ApiController]
public class ExceptionsController : ControllerBase
{
    private const long MaxPhotoBytes = 10 * 1024 * 1024;

    private readonly IExecutionExceptionService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly Incidents.Services.IProblemFollowUpService _followUp;

    public ExceptionsController(
        IExecutionExceptionService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext,
        Incidents.Services.IProblemFollowUpService followUp)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
        _followUp = followUp;
    }

    /// <summary>The customer-facing message (the driver's report stays internal).</summary>
    [HttpPut("api/exceptions/{id:guid}/customer-message")]
    [RequirePermission(PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<ExceptionDetailDto>> UpdateCustomerMessage(
        Guid id, UpdateExceptionCustomerMessageRequest request, CancellationToken cancellationToken) =>
        Handle(await _service.UpdateCustomerMessageAsync(id, request.CustomerMessage, cancellationToken));

    /// <summary>Afwijking → Incident: the new incident carries every relation; both stay traceable.</summary>
    [HttpPost("api/exceptions/{id:guid}/escalate")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<Incidents.Dtos.IncidentDetailDto>> Escalate(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _followUp.EscalateExceptionAsync(id, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    /// <summary>EX-03: the linked fleet damage report (vehicle, trip and driver from the afwijking's trip).</summary>
    [HttpPost("api/exceptions/{id:guid}/damage-report")]
    [RequirePermission(PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<ExceptionDetailDto>> CreateDamageReport(
        Guid id, Incidents.Dtos.CreateLinkedDamageReportRequest request, CancellationToken cancellationToken)
    {
        if (await _followUp.CreateDamageReportForExceptionAsync(id, request, cancellationToken) is null)
        {
            return NotFound();
        }

        var detail = await _service.GetByIdAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    /// <summary>Domain history (when / who / what) of the afwijking.</summary>
    [HttpGet("api/exceptions/{id:guid}/history")]
    [RequirePermission(PermissionCodes.ExceptionsView)]
    public async Task<ActionResult<Modules.Auditing.Dtos.AuditLogPagedResult>> History(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var history = await _followUp.HistoryAsync("ExecutionException", id, page, pageSize, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }

    private async Task<bool> IsStaffAsync(CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.ExceptionsResolve, cancellationToken);

    private async Task<bool> HasViewAsync(CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.ExceptionsView, cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/exceptions")]
    [RequirePermission(PermissionCodes.ExceptionsCreate)]
    public async Task<ActionResult<ExceptionDetailDto>> Report(
        Guid tripId, ReportExceptionRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await IsStaffAsync(cancellationToken);
        return Handle(await _service.ReportAsync(tripId, request, restrict, cancellationToken));
    }

    [HttpGet("api/trips/{tripId:guid}/exceptions")]
    [RequirePermission(PermissionCodes.ExceptionsView, PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<ExceptionListItemDto>>> ListForTrip(
        Guid tripId, CancellationToken cancellationToken)
    {
        var restrict = !await HasViewAsync(cancellationToken);
        var result = await _service.ListForTripAsync(tripId, restrict, cancellationToken);
        return result.Outcome switch
        {
            ExceptionOutcome.Success => Ok(result.Exceptions),
            // Security H1: another driver's trip answers exactly like an unknown one.
            ExceptionOutcome.NotYourTrip => NotFound(),
            _ => NotFound(),
        };
    }

    [HttpGet("api/exceptions")]
    [RequirePermission(PermissionCodes.ExceptionsView)]
    public async Task<ActionResult<PagedResult<ExceptionListItemDto>>> Search(
        [FromQuery] ExecutionExceptionStatus? status,
        [FromQuery] ExecutionExceptionType? type,
        [FromQuery] ExceptionSeverity? severity,
        [FromQuery] Guid? tripId,
        [FromQuery] bool? packagesOnly,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] bool? mine,
        [FromQuery] Guid? transportOrderId,
        [FromQuery] bool? openOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        [FromQuery] string? dir,
        CancellationToken cancellationToken)
    {
        var assignee = mine == true ? _currentUserContext.CurrentUserId : assignedToUserId;
        return Ok(await _service.SearchAsync(
            status, type, severity, tripId, packagesOnly, assignee, page, pageSize, cancellationToken, sort, dir,
            transportOrderId, openOnly == true));
    }

    /// <summary>Assign (or with null: unassign) the follow-up owner.</summary>
    [HttpPost("api/exceptions/{id:guid}/assign")]
    [RequirePermission(PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<ExceptionDetailDto>> Assign(
        Guid id, AssignExceptionRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.AssignAsync(id, request.UserId, cancellationToken));
    }

    [HttpGet("api/exceptions/{id:guid}")]
    [RequirePermission(PermissionCodes.ExceptionsView)]
    public async Task<ActionResult<ExceptionDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _service.GetByIdAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("api/exceptions/{id:guid}/status")]
    [RequirePermission(PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<ExceptionDetailDto>> ChangeStatus(
        Guid id, ChangeExceptionStatusRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.ChangeStatusAsync(id, request, cancellationToken));
    }

    [HttpPut("api/exceptions/{id:guid}")]
    [RequirePermission(PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<ExceptionDetailDto>> Update(
        Guid id, UpdateExceptionRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken));
    }

    [HttpPost("api/exceptions/{id:guid}/photos")]
    [RequirePermission(PermissionCodes.ExceptionsCreate, PermissionCodes.ExceptionsResolve)]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<ActionResult<ExceptionDetailDto>> AttachPhoto(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxPhotoBytes)
        {
            return BadRequest(new { message = "De foto moet tussen 1 byte en 10 MB groot zijn." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        var restrict = !await IsStaffAsync(cancellationToken);
        await using var stream = file.OpenReadStream();
        return Handle(await _service.AttachPhotoAsync(
            id, file.FileName, file.ContentType, stream, restrict, cancellationToken));
    }

    [HttpGet("api/exceptions/{id:guid}/photos/{photoId:guid}")]
    [RequirePermission(PermissionCodes.ExceptionsView, PermissionCodes.DriverWorkflowView)]
    public async Task<IActionResult> DownloadPhoto(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        // Non-staff callers reach this through driver-workflow rights and may only read media of
        // their own trips; anything else is 404 so ids cannot be probed.
        var restrict = !await IsStaffAsync(cancellationToken);
        var photo = await _service.OpenPhotoAsync(id, photoId, restrict, cancellationToken);
        return photo is null ? NotFound() : File(photo.Value.Content, photo.Value.ContentType, photo.Value.FileName);
    }

    [HttpDelete("api/exceptions/{id:guid}/photos/{photoId:guid}")]
    [RequirePermission(PermissionCodes.ExceptionsResolve)]
    public async Task<ActionResult<ExceptionDetailDto>> DeletePhoto(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        return Handle(await _service.DeletePhotoAsync(id, photoId, cancellationToken));
    }

    private ActionResult<ExceptionDetailDto> Handle(ExceptionOperationResult result) => result.Outcome switch
    {
        ExceptionOutcome.Success => Ok(result.Exception),
        ExceptionOutcome.NotFound => NotFound(),
        // Security H1: another driver's trip answers exactly like an unknown one.
        ExceptionOutcome.NotYourTrip => NotFound(),
        ExceptionOutcome.InvalidState => BadRequest(new { message = result.Error }),
        ExceptionOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
