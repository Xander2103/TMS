using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Services;

namespace TransportationService.Api.Modules.Packages.Controllers;

/// <summary>
/// Trip-scoped package views and incident actions. Drivers see and act on their own trips;
/// dispatch (planning.edit) and warehouse (warehouse.view) see any trip. Resolving incidents
/// is a dispatcher capability, never restricted to the assigned driver.
/// </summary>
[ApiController]
public class TripPackagesController : ControllerBase
{
    private readonly ITripPackageService _service;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly ICurrentUserContext _currentUserContext;

    public TripPackagesController(
        ITripPackageService service,
        IPermissionAuthorizationService permissionService,
        ICurrentUserContext currentUserContext)
    {
        _service = service;
        _permissionService = permissionService;
        _currentUserContext = currentUserContext;
    }

    private async Task<bool> IsBackOfficeAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return false;
        }
        return await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.PlanningEdit, cancellationToken)
               || await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.WarehouseView, cancellationToken);
    }

    [HttpGet("api/trips/{tripId:guid}/packages")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.DriverWorkflowView, PermissionCodes.WarehouseView)]
    public async Task<ActionResult<TripPackageChecklistDto>> Checklist(
        Guid tripId, [FromQuery] Guid? stopId, CancellationToken cancellationToken)
    {
        var restrict = !await IsBackOfficeAsync(cancellationToken);
        return Handle(await _service.GetChecklistAsync(tripId, stopId, restrict, cancellationToken));
    }

    [HttpGet("api/trips/{tripId:guid}/package-readiness")]
    [RequirePermission(PermissionCodes.PackagesView, PermissionCodes.DriverWorkflowView, PermissionCodes.WarehouseView)]
    public async Task<ActionResult<TripPackageReadinessDto>> Readiness(
        Guid tripId, CancellationToken cancellationToken)
    {
        var restrict = !await IsBackOfficeAsync(cancellationToken);
        return Handle(await _service.GetReadinessAsync(tripId, restrict, cancellationToken));
    }

    [HttpPost("api/trips/{tripId:guid}/packages/{packageId:guid}/missing")]
    [RequirePermission(PermissionCodes.ScanningExecute, PermissionCodes.PackageExceptionsCreate)]
    public async Task<IActionResult> MarkMissing(
        Guid tripId, Guid packageId, MarkPackageMissingRequest request, CancellationToken cancellationToken)
    {
        var restrict = !await IsBackOfficeAsync(cancellationToken);
        return Handle(await _service.MarkMissingAsync(tripId, packageId, request, restrict, cancellationToken));
    }

    [HttpPost("api/packages/{packageId:guid}/resolve-incident")]
    [RequirePermission(PermissionCodes.PackageExceptionsManage)]
    public async Task<IActionResult> ResolveIncident(
        Guid packageId, ResolvePackageIncidentRequest request, CancellationToken cancellationToken)
    {
        return Handle(await _service.ResolveIncidentAsync(packageId, request, cancellationToken));
    }

    private ObjectResult Handle(TripPackageOperationResult result) => result.Outcome switch
    {
        TripPackageOutcome.Success => Ok(result.Payload ?? new { }),
        TripPackageOutcome.NotFound => NotFound(new { }),
        TripPackageOutcome.NotYourTrip => StatusCode(StatusCodes.Status403Forbidden, new { message = result.Error }),
        TripPackageOutcome.InvalidState => BadRequest(new { message = result.Error }),
        TripPackageOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(new { }),
    };
}
