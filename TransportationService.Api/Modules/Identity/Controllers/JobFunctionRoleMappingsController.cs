using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Identity.Controllers;

/// <summary>
/// Configurable HR-function → role mappings. Mappings only SUGGEST roles during account
/// creation; the administrator always confirms, and function changes never silently touch
/// existing role assignments.
/// </summary>
[ApiController]
[Route("api/job-function-role-mappings")]
public class JobFunctionRoleMappingsController : ControllerBase
{
    private readonly IJobFunctionRoleMappingService _service;

    public JobFunctionRoleMappingsController(IJobFunctionRoleMappingService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.RolesView, PermissionCodes.RolesManagePermissions)]
    public async Task<ActionResult<IReadOnlyList<JobFunctionRoleMappingDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _service.ListAsync(cancellationToken));

    [HttpPost]
    [RequirePermission(PermissionCodes.RolesManagePermissions)]
    public async Task<ActionResult<JobFunctionRoleMappingDto>> Create(
        SaveJobFunctionRoleMappingRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.CreateAsync(request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.RolesManagePermissions)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
