using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/vehicles/{vehicleId:guid}/tachograph-calibrations")]
public class TachographController : ControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly ITachographService _service;

    public TachographController(ITachographService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.TachographView)]
    public async Task<ActionResult<IReadOnlyList<TachographCalibrationDto>>> List(Guid vehicleId, CancellationToken cancellationToken)
    {
        var rows = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return rows is null ? NotFound() : Ok(rows);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TachographManage)]
    public async Task<ActionResult<TachographCalibrationDto>> Create(Guid vehicleId, SaveTachographCalibrationRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(vehicleId, request, cancellationToken);
        return created is null ? NotFound() : Ok(created);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TachographManage)]
    public async Task<ActionResult<TachographCalibrationDto>> Update(Guid vehicleId, Guid id, SaveTachographCalibrationRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(vehicleId, id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TachographManage)]
    public async Task<IActionResult> Delete(Guid vehicleId, Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(vehicleId, id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/document")]
    [RequirePermission(PermissionCodes.TachographManage)]
    [RequestSizeLimit(MaxBytes + 1024)]
    public async Task<ActionResult<TachographCalibrationDto>> Upload(Guid vehicleId, Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (FleetFileHelpers.Validate(file, MaxBytes, AllowedExtensions) is { } error)
        {
            return BadRequest(new { message = error });
        }

        await using var stream = file.OpenReadStream();
        var updated = await _service.AttachFileAsync(vehicleId, id, file.FileName, FleetFileHelpers.ContentTypeFor(file.FileName), stream, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("{id:guid}/document")]
    [RequirePermission(PermissionCodes.TachographView)]
    public async Task<IActionResult> Download(Guid vehicleId, Guid id, CancellationToken cancellationToken)
    {
        var file = await _service.OpenFileAsync(vehicleId, id, cancellationToken);
        return file is { } found ? File(found.Content, found.ContentType, found.FileName) : NotFound();
    }
}

internal static class FleetFileHelpers
{
    public static string? Validate(IFormFile file, long maxBytes, string[] allowedExtensions)
    {
        if (file.Length == 0 || file.Length > maxBytes)
        {
            return "Het bestand moet tussen 1 byte en 10 MB groot zijn.";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            return "Alleen PDF-, JPG- en PNG-bestanden zijn toegestaan.";
        }

        return Modules.Security.UploadValidation.SignatureError(file);
    }

    public static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}
