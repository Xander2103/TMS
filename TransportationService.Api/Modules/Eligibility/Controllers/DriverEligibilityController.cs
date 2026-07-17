using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Eligibility.Dtos;
using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Eligibility.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Eligibility.Controllers;

[ApiController]
[Route("api/driver-eligibility")]
public class DriverEligibilityController : ControllerBase
{
    private readonly IDriverEligibilityService _eligibilityService;

    public DriverEligibilityController(IDriverEligibilityService eligibilityService)
    {
        _eligibilityService = eligibilityService;
    }

    [HttpPost("check")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<ActionResult<EligibilityResult>> Check(CheckEligibilityRequest request, CancellationToken cancellationToken)
    {
        var evaluationRequest = new DriverEligibilityRequest(
            request.RequiredDrivingLicenceCategory, request.RequiresCode95, request.RequiresAdr,
            request.RequiresMedicalFitness, request.RequiresCraneCertificate, request.RequiredAdditionalQualificationCodes,
            request.PlannedStartDate, request.PlannedEndDate);

        var result = await _eligibilityService.CheckEligibilityAsync(request.EmployeeId, evaluationRequest, cancellationToken);
        return Ok(result);
    }
}
