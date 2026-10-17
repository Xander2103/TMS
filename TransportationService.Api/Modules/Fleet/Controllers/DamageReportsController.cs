using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Damage/incident reports for vehicles and trailers. List/create nested under the owning
/// resource; item operations and the recent overview are flat.
/// </summary>
[ApiController]
public class DamageReportsController : ControllerBase
{
    private readonly IDamageReportService _service;

    public DamageReportsController(IDamageReportService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<ActionResult<IReadOnlyList<DamageReportDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var reports = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return reports is null ? NotFound() : Ok(reports);
    }

    [HttpGet("api/trailers/{trailerId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<ActionResult<IReadOnlyList<DamageReportDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var reports = await _service.ListForTrailerAsync(trailerId, cancellationToken);
        return reports is null ? NotFound() : Ok(reports);
    }

    [HttpGet("api/damage-reports/recent")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<ActionResult<IReadOnlyList<RecentDamageDto>>> ListRecent(
        [FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListRecentAsync(take, cancellationToken));
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsCreate)]
    public async Task<ActionResult<DamageReportDto>> CreateForVehicle(
        Guid vehicleId, CreateDamageReportRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken), created: true);
    }

    [HttpPost("api/trailers/{trailerId:guid}/damage-reports")]
    [RequirePermission(PermissionCodes.DamageReportsCreate)]
    public async Task<ActionResult<DamageReportDto>> CreateForTrailer(
        Guid trailerId, CreateDamageReportRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.CreateForTrailerAsync(trailerId, request, cancellationToken), created: true);
    }

    [HttpPut("api/damage-reports/{id:guid}")]
    [RequirePermission(PermissionCodes.DamageReportsEdit)]
    public async Task<ActionResult<DamageReportDto>> Update(
        Guid id, UpdateDamageReportRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.UpdateAsync(id, request, cancellationToken), created: false);
    }

    [HttpDelete("api/damage-reports/{id:guid}")]
    [RequirePermission(PermissionCodes.DamageReportsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private const long MaxAttachmentBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    /// <summary>FL-M7: photo or document of the damage (pdf/jpg/png, max 10 MB).</summary>
    [HttpPost("api/damage-reports/{id:guid}/document")]
    [RequirePermission(PermissionCodes.DamageReportsEdit)]
    [RequestSizeLimit(MaxAttachmentBytes + 1024)]
    public async Task<ActionResult<DamageReportDto>> UploadDocument(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxAttachmentBytes)
        {
            return BadRequest(new { message = "Het bestand moet tussen 1 byte en 10 MB groot zijn." });
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

    [HttpGet("api/damage-reports/{id:guid}/document")]
    [RequirePermission(PermissionCodes.DamageReportsView)]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken cancellationToken)
    {
        var file = await _service.OpenFileAsync(id, cancellationToken);
        return file is { } found ? File(found.Content, found.ContentType, found.FileName) : NotFound();
    }

    [HttpDelete("api/damage-reports/{id:guid}/document")]
    [RequirePermission(PermissionCodes.DamageReportsEdit)]
    public async Task<ActionResult<DamageReportDto>> RemoveDocument(Guid id, CancellationToken cancellationToken)
    {
        return Handle(await _service.RemoveFileAsync(id, cancellationToken), created: false);
    }

    private ActionResult<DamageReportDto> Handle(DamageOperationResult result, bool created) => result.Outcome switch
    {
        DamageOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Report),
        DamageOperationOutcome.Success => Ok(result.Report),
        DamageOperationOutcome.NotFound => NotFound(),
        DamageOperationOutcome.OwnerNotFound => NotFound(),
        DamageOperationOutcome.InvalidReference => BadRequest(new { message = "De gekoppelde chauffeur bestaat niet." }),
        DamageOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
