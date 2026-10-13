using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Documents for vehicles and trailers. List/create are nested under the owning resource;
/// item operations and the expiring-documents overview are flat.
/// </summary>
[ApiController]
public class FleetDocumentsController : ControllerBase
{
    private readonly IFleetDocumentService _service;

    public FleetDocumentsController(IFleetDocumentService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<FleetDocumentDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpGet("api/trailers/{trailerId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<FleetDocumentDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListForTrailerAsync(trailerId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    [HttpGet("api/fleet-documents/expiring")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<ActionResult<IReadOnlyList<ExpiringFleetDocumentDto>>> ListExpiring(
        [FromQuery] int withinDays = 60, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListExpiringAsync(withinDays, cancellationToken));
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsCreate)]
    public async Task<ActionResult<FleetDocumentDto>> CreateForVehicle(
        Guid vehicleId, CreateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken), created: true);
    }

    [HttpPost("api/trailers/{trailerId:guid}/documents")]
    [RequirePermission(PermissionCodes.FleetDocumentsCreate)]
    public async Task<ActionResult<FleetDocumentDto>> CreateForTrailer(
        Guid trailerId, CreateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForTrailerAsync(trailerId, request, cancellationToken), created: true);
    }

    [HttpPut("api/fleet-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.FleetDocumentsEdit)]
    public async Task<ActionResult<FleetDocumentDto>> Update(
        Guid id, UpdateFleetDocumentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken), created: false);
    }

    [HttpDelete("api/fleet-documents/{id:guid}")]
    [RequirePermission(PermissionCodes.FleetDocumentsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private const long MaxDocumentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    [HttpPost("api/fleet-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.FleetDocumentsEdit)]
    [RequestSizeLimit(MaxDocumentBytes + 1024)]
    public async Task<ActionResult<FleetDocumentDto>> UploadDocument(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxDocumentBytes)
        {
            return BadRequest(new { message = "Het document moet tussen 1 byte en 10 MB groot zijn." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan." });
        }

        if (Modules.Security.UploadValidation.SignatureError(file) is { } signatureError)
        {
            return BadRequest(new { message = signatureError });
        }

        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };

        await using var stream = file.OpenReadStream();
        return Handle(await _service.AttachFileAsync(id, file.FileName, contentType, stream, cancellationToken), created: false);
    }

    [HttpGet("api/fleet-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.FleetDocumentsView)]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken cancellationToken)
    {
        var file = await _service.OpenFileAsync(id, cancellationToken);
        return file is { } found ? File(found.Content, found.ContentType, found.FileName) : NotFound();
    }

    [HttpDelete("api/fleet-documents/{id:guid}/document")]
    [RequirePermission(PermissionCodes.FleetDocumentsEdit)]
    public async Task<ActionResult<FleetDocumentDto>> RemoveDocument(Guid id, CancellationToken cancellationToken)
    {
        return Handle(await _service.RemoveFileAsync(id, cancellationToken), created: false);
    }

    private ActionResult<FleetDocumentDto> Handle(FleetDocumentOperationResult result, bool created) => result.Outcome switch
    {
        FleetDocumentOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Document),
        FleetDocumentOperationOutcome.Success => Ok(result.Document),
        FleetDocumentOperationOutcome.NotFound => NotFound(),
        FleetDocumentOperationOutcome.OwnerNotFound => NotFound(),
        FleetDocumentOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
