using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Dossiers.Controllers;

[ApiController]
[Route("api/activity-types")]
public class ActivityTypesController : ControllerBase
{
    private readonly IActivityTypeService _service;

    public ActivityTypesController(IActivityTypeService service)
    {
        _service = service;
    }

    // dossiers.view is accepted alongside activity_types.view: every dossier picker needs
    // the type catalogue, and dossier readers should not need a second permission for it.
    [HttpGet]
    [RequirePermission(PermissionCodes.ActivityTypesView, PermissionCodes.DossiersView)]
    public async Task<ActionResult<IReadOnlyList<ActivityTypeDto>>> List(
        [FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(includeInactive, cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.ActivityTypesManage)]
    public async Task<ActionResult<ActivityTypeDto>> Create(SaveActivityTypeRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.ActivityTypesManage)]
    public async Task<ActionResult<ActivityTypeDto>> Update(Guid id, SaveActivityTypeRequest request, CancellationToken cancellationToken)
    {
        var type = await _service.UpdateAsync(id, request, cancellationToken);
        return type is null ? NotFound() : Ok(type);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.ActivityTypesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await _service.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
