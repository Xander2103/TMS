using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Pod.Dtos;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Pod.Services;

namespace TransportationService.Api.Modules.Pod.Controllers;

/// <summary>
/// Proof of delivery: drivers finalise on their own trips (pod.finalize); staff with
/// pod.correct create new versions; pod.view opens the back-office detail. Files go through
/// permissioned endpoints only — storage keys never leave the server.
/// </summary>
[ApiController]
public class PodController : ControllerBase
{
    private const long MaxPhotoBytes = 10 * 1024 * 1024;

    private readonly IPodService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public PodController(
        IPodService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    private async Task<bool> HasAsync(string permission, CancellationToken cancellationToken) =>
        _currentUserContext.CurrentUserId is { } userId
        && await _permissionService.UserHasPermissionAsync(userId, permission, cancellationToken);

    [HttpPost("api/trips/{tripId:guid}/stops/{stopId:guid}/pod")]
    [RequirePermission(PermissionCodes.PodFinalize)]
    public async Task<ActionResult<PodDetailDto>> Finalize(
        Guid tripId, Guid stopId, FinalizePodRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await HasAsync(PermissionCodes.PodCorrect, cancellationToken);
        return Handle(await _service.FinalizeAsync(tripId, stopId, request, restrict, cancellationToken));
    }

    [HttpGet("api/trips/{tripId:guid}/stops/{stopId:guid}/pod")]
    [RequirePermission(PermissionCodes.PodView, PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<PodDetailDto>> GetForStop(
        Guid tripId, Guid stopId, CancellationToken cancellationToken)
    {
        var restrict = !await HasAsync(PermissionCodes.PodView, cancellationToken);
        var pod = await _service.GetForStopAsync(tripId, stopId, restrict, cancellationToken);
        return pod is null ? NotFound() : Ok(pod);
    }

    [HttpGet("api/pods/{id:guid}")]
    [RequirePermission(PermissionCodes.PodView)]
    public async Task<ActionResult<PodDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var pod = await _service.GetByIdAsync(id, cancellationToken);
        return pod is null ? NotFound() : Ok(pod);
    }

    [HttpPost("api/pods/{id:guid}/corrections")]
    [RequirePermission(PermissionCodes.PodCorrect)]
    public async Task<ActionResult<PodDetailDto>> Correct(
        Guid id, CorrectPodRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CorrectAsync(id, request, cancellationToken));
    }

    [HttpPost("api/pods/{id:guid}/photos")]
    [RequirePermission(PermissionCodes.PodFinalize, PermissionCodes.PodCorrect)]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<ActionResult<PodDetailDto>> AttachPhoto(
        Guid id, [FromQuery] PodPhotoCategory category, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxPhotoBytes)
        {
            return BadRequest(new { message = "De foto moet tussen 1 byte en 10 MB groot zijn." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        var restrict = !await HasAsync(PermissionCodes.PodCorrect, cancellationToken);
        await using var stream = file.OpenReadStream();
        return Handle(await _service.AttachPhotoAsync(
            id, category, file.FileName, file.ContentType, stream, restrict, cancellationToken));
    }

    [HttpGet("api/pods/{id:guid}/photos/{photoId:guid}")]
    [RequirePermission(PermissionCodes.PodView, PermissionCodes.DriverWorkflowView)]
    public async Task<IActionResult> DownloadPhoto(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        // A caller without the back-office pod.view right is a driver: they may only read media of
        // their own trips (404 on anything else, so ids cannot be probed).
        var restrict = !await HasAsync(PermissionCodes.PodView, cancellationToken);
        var photo = await _service.OpenPhotoAsync(id, photoId, restrict, cancellationToken);
        return photo is null ? NotFound() : File(photo.Value.Content, photo.Value.ContentType, photo.Value.FileName);
    }

    [HttpGet("api/pods/{id:guid}/signature")]
    [RequirePermission(PermissionCodes.PodView, PermissionCodes.DriverWorkflowView)]
    public async Task<IActionResult> DownloadSignature(Guid id, CancellationToken cancellationToken)
    {
        var restrict = !await HasAsync(PermissionCodes.PodView, cancellationToken);
        var signature = await _service.OpenSignatureAsync(id, restrict, cancellationToken);
        return signature is null ? NotFound() : File(signature.Value.Content, signature.Value.ContentType);
    }

    private ActionResult<PodDetailDto> Handle(PodOperationResult result) => result.Outcome switch
    {
        PodOutcomeResult.Success => Ok(result.Pod),
        PodOutcomeResult.NotFound => NotFound(),
        PodOutcomeResult.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        PodOutcomeResult.InvalidState => BadRequest(new { message = result.Error }),
        PodOutcomeResult.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
