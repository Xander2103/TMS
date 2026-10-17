using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Dossiers.Controllers;

/// <summary>
/// T-H7 driver app: the standalone activities (crane work on site, plateau …) assigned to the
/// signed-in driver, which the driver starts and completes on site. Same rights as "my trips";
/// someone else's activity is a 404, never a 403 that would confirm it exists.
/// </summary>
[ApiController]
[Route("api/my/activities")]
public class MyActivitiesController : ControllerBase
{
    private readonly IMyActivitiesService _service;

    public MyActivitiesController(IMyActivitiesService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DriverWorkflowView)]
    public async Task<ActionResult<IReadOnlyList<MyActivityDto>>> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListMineAsync(from, to, cancellationToken));
    }

    [HttpPost("{activityId:guid}/start")]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<MyActivityDto>> Start(Guid activityId, CancellationToken cancellationToken)
    {
        var activity = await _service.StartMineAsync(activityId, cancellationToken);
        return activity is null ? NotFound() : Ok(activity);
    }

    [HttpPost("{activityId:guid}/complete")]
    [RequirePermission(PermissionCodes.DriverWorkflowExecute)]
    public async Task<ActionResult<MyActivityDto>> Complete(Guid activityId, CancellationToken cancellationToken)
    {
        var activity = await _service.CompleteMineAsync(activityId, cancellationToken);
        return activity is null ? NotFound() : Ok(activity);
    }
}
