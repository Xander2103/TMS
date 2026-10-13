using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Hr.Controllers;

[ApiController]
[Route("api/hr")]
public class HrReminderSettingsController : ControllerBase
{
    private readonly IHrReminderConfigService _service;

    public HrReminderSettingsController(IHrReminderConfigService service)
    {
        _service = service;
    }

    [HttpGet("reminder-settings")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<HrReminderSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetSettingsAsync(cancellationToken));
    }

    [HttpPut("reminder-settings")]
    [RequirePermission(PermissionCodes.HrSettingsManage)]
    public async Task<ActionResult<HrReminderSettingsDto>> UpdateSettings(HrReminderSettingsDto request, CancellationToken cancellationToken)
    {
        return Ok(await _service.UpdateSettingsAsync(request, cancellationToken));
    }

    [HttpGet("expiry-policies")]
    [RequirePermission(PermissionCodes.EmployeesView)]
    public async Task<ActionResult<IReadOnlyList<ExpiryReminderPolicyDto>>> ListPolicies(CancellationToken cancellationToken)
    {
        return Ok(await _service.ListPoliciesAsync(cancellationToken));
    }

    [HttpPost("expiry-policies")]
    [RequirePermission(PermissionCodes.HrSettingsManage)]
    public async Task<ActionResult<ExpiryReminderPolicyDto>> CreatePolicy(SaveExpiryReminderPolicyRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreatePolicyAsync(request, cancellationToken));
    }

    [HttpPut("expiry-policies/{id:guid}")]
    [RequirePermission(PermissionCodes.HrSettingsManage)]
    public async Task<ActionResult<ExpiryReminderPolicyDto>> UpdatePolicy(Guid id, SaveExpiryReminderPolicyRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdatePolicyAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("expiry-policies/{id:guid}")]
    [RequirePermission(PermissionCodes.HrSettingsManage)]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeletePolicyAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
