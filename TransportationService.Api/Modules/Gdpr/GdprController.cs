using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Gdpr;

/// <summary>
/// Data-subject rights (H13): dossier export (art. 15/20) and anonymisation (art. 17). Both are
/// heavyweight, confidential operations — export needs the confidential-fields permission, and
/// anonymisation has its own dedicated permission that no default template carries (system
/// administrators only, by explicit grant).
/// </summary>
[ApiController]
public class GdprController : ControllerBase
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataSubjectService _service;

    public GdprController(IDataSubjectService service)
    {
        _service = service;
    }

    [HttpGet("api/employees/{id:guid}/gdpr-export")]
    [RequirePermission(PermissionCodes.EmployeesViewConfidential)]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var export = await _service.ExportAsync(id, cancellationToken);
        if (export is null)
        {
            return NotFound();
        }

        return File(
            JsonSerializer.SerializeToUtf8Bytes(export, ExportJson),
            "application/json",
            $"gdpr-dossier-{id}.json");
    }

    [HttpPost("api/employees/{id:guid}/anonymize")]
    [RequirePermission(PermissionCodes.EmployeesAnonymize)]
    public async Task<IActionResult> Anonymize(Guid id, CancellationToken cancellationToken)
    {
        var error = await _service.AnonymizeAsync(id, cancellationToken);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }
}
