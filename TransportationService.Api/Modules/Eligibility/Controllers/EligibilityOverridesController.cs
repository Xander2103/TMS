using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Eligibility.Dtos;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Eligibility.Controllers;

[ApiController]
[Route("api/eligibility-overrides")]
public class EligibilityOverridesController : ControllerBase
{
    private readonly IEligibilityOverrideService _overrideService;
    private readonly ICurrentUserContext _currentUserContext;

    public EligibilityOverridesController(IEligibilityOverrideService overrideService, ICurrentUserContext currentUserContext)
    {
        _overrideService = overrideService;
        _currentUserContext = currentUserContext;
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.PlanningOverrideRestriction)]
    public async Task<ActionResult<EligibilityOverrideDto>> Create(CreateEligibilityOverrideRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } approvingUserId)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest("Reason is required.");
        }

        var created = await _overrideService.CreateAsync(approvingUserId, request, cancellationToken);
        return Ok(created);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<IReadOnlyList<EligibilityOverrideDto>>> ListForEmployee([FromQuery] Guid employeeId, CancellationToken cancellationToken)
    {
        return Ok(await _overrideService.ListForEmployeeAsync(employeeId, cancellationToken));
    }
}
