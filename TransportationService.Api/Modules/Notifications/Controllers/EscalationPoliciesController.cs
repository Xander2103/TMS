using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;

namespace TransportationService.Api.Modules.Notifications.Controllers;

/// <summary>Bounded escalation configuration (per condition: delay, target permission, active).</summary>
[ApiController]
[Route("api/escalation-policies")]
[RequirePermission(PermissionCodes.EscalationsManage)]
public class EscalationPoliciesController : ControllerBase
{
    private readonly IEscalationPolicyService _service;

    public EscalationPoliciesController(IEscalationPolicyService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EscalationPolicyDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _service.ListAsync(cancellationToken));

    [HttpPut("{kind}")]
    public async Task<ActionResult<EscalationPolicyDto>> Update(
        EscalationKind kind, SaveEscalationPolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await _service.UpdateAsync(kind, request, cancellationToken);
        return policy is null ? NotFound() : Ok(policy);
    }
}
