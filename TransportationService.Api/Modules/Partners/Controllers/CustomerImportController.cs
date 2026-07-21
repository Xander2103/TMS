using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Partners.Services;

namespace TransportationService.Api.Modules.Partners.Controllers;

/// <summary>
/// Excel customer import (template / preview / commit) with existing customer numbers.
/// Mirrors the package import endpoints; all routes require customers.import.
/// </summary>
[ApiController]
[Route("api/customers/import")]
public class CustomerImportController : ControllerBase
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private readonly ICustomerImportService _importService;

    public CustomerImportController(ICustomerImportService importService)
    {
        _importService = importService;
    }

    [HttpGet("template")]
    [RequirePermission(PermissionCodes.CustomersImport)]
    public IActionResult Template()
    {
        return File(_importService.BuildTemplate(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "klanten-import.xlsx");
    }

    [HttpPost("preview")]
    [RequirePermission(PermissionCodes.CustomersImport)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<IActionResult> Preview(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return BadRequest(new { message = "Het bestand moet tussen 1 byte en 5 MB groot zijn." });
        }

        await using var stream = file.OpenReadStream();
        var (preview, error) = await _importService.PreviewAsync(stream, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : Ok(preview);
    }

    [HttpPost("commit")]
    [RequirePermission(PermissionCodes.CustomersImport)]
    [RequestSizeLimit(MaxUploadBytes + 1024)]
    public async Task<IActionResult> Commit(
        IFormFile file,
        [FromQuery] bool allOrNothing = true,
        [FromQuery] bool allowUpdates = false,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0 || file.Length > MaxUploadBytes)
        {
            return BadRequest(new { message = "Het bestand moet tussen 1 byte en 5 MB groot zijn." });
        }

        await using var stream = file.OpenReadStream();
        var (result, error) = await _importService.CommitAsync(stream, allOrNothing, allowUpdates, cancellationToken);
        return error is not null ? BadRequest(new { message = error }) : Ok(result);
    }
}
