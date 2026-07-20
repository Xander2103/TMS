using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Services;

namespace TransportationService.Api.Modules.Planning.Controllers;

/// <summary>
/// The mobile driver app's self-scoped surface: home dashboard and authorized documents.
/// Everything resolves through the signed-in user's driver profile.
/// </summary>
[ApiController]
public class DriverAppController : ControllerBase
{
    private readonly IDriverAppService _service;

    public DriverAppController(IDriverAppService service)
    {
        _service = service;
    }

    [HttpGet("api/my/dashboard")]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<MyDashboardDto>> Dashboard(CancellationToken cancellationToken)
    {
        var dashboard = await _service.GetMyDashboardAsync(cancellationToken);
        return dashboard is null
            ? NotFound(new { message = "Er is geen chauffeursprofiel gekoppeld aan dit account." })
            : Ok(dashboard);
    }

    [HttpGet("api/my/documents")]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<DriverDocumentDto>>> Documents(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListMyDocumentsAsync(cancellationToken));
    }

    /// <summary>404 for anything outside the driver's active-trip asset scope — never 403 leaks.</summary>
    [HttpGet("api/my/documents/{id:guid}/download")]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var document = await _service.OpenMyDocumentAsync(id, cancellationToken);
        return document is null
            ? NotFound()
            : File(document.Value.Content, "application/octet-stream", document.Value.FileName);
    }
}
