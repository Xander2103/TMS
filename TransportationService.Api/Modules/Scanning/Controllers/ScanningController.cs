using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Services;

namespace TransportationService.Api.Modules.Scanning.Controllers;

/// <summary>
/// Scanning during trip execution. Drivers scan their own trips; users with planning.edit
/// (dispatch) may scan/inspect any trip. Corrections carry their own permission and are
/// never restricted to the assigned driver — a supervisor fixes the record.
/// </summary>
[ApiController]
public class ScanningController : ControllerBase
{
    private readonly IScanService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public ScanningController(
        IScanService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    private async Task<bool> IsDispatcherAsync(CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.PlanningEdit, cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/scans")]
    [RequirePermission(PermissionCodes.ScanningExecute)]
    public async Task<ActionResult<ScanFeedbackDto>> Submit(
        Guid tripId, Guid stopId, SubmitScanRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        return Handle(await _service.SubmitAsync(tripId, stopId, request, restrict, cancellationToken));
    }

    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/scan-corrections")]
    [RequirePermission(PermissionCodes.ScanningCorrect)]
    public async Task<ActionResult<ScanFeedbackDto>> Correct(
        Guid tripId, Guid stopId, ScanCorrectionRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CorrectAsync(tripId, stopId, request, cancellationToken));
    }

    [HttpGet("api/trips/{tripId:guid}/scans")]
    [RequirePermission(PermissionCodes.ScanningView, PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<ScanEventDto>>> List(
        Guid tripId, [FromQuery] Guid? stopId, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        var result = await _service.ListAsync(tripId, stopId, restrict, cancellationToken);
        return result.Outcome switch
        {
            ScanOutcome.Success => Ok(result.Events),
            ScanOutcome.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
            _ => NotFound(),
        };
    }

    [HttpGet("api/trips/{tripId:guid}/stops/{stopId:guid}/scan-summary")]
    [RequirePermission(PermissionCodes.ScanningView, PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<StopScanSummaryDto>> Summary(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var restrict = !await IsDispatcherAsync(cancellationToken);
        var result = await _service.GetStopSummaryAsync(tripId, stopId, restrict, cancellationToken);
        return result.Outcome switch
        {
            ScanOutcome.Success => Ok(result.Summary),
            ScanOutcome.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
            _ => NotFound(),
        };
    }

    private ActionResult<ScanFeedbackDto> Handle(ScanOperationResult result) => result.Outcome switch
    {
        ScanOutcome.Success => Ok(result.Feedback),
        ScanOutcome.NotFound => NotFound(),
        ScanOutcome.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        ScanOutcome.InvalidState => BadRequest(new { message = result.Error }),
        ScanOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
