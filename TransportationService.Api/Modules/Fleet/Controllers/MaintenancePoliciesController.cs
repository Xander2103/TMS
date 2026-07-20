using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/maintenance-policies")]
public class MaintenancePoliciesController : ControllerBase
{
    private readonly IMaintenancePolicyService _service;

    public MaintenancePoliciesController(IMaintenancePolicyService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.MaintenancePoliciesView, PermissionCodes.MaintenancePoliciesManage)]
    public async Task<ActionResult<IReadOnlyList<MaintenancePolicyDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.MaintenancePoliciesManage)]
    public async Task<ActionResult<MaintenancePolicyDto>> Create(SaveMaintenancePolicyRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.MaintenancePoliciesManage)]
    public async Task<ActionResult<MaintenancePolicyDto>> Update(Guid id, SaveMaintenancePolicyRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.MaintenancePoliciesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
