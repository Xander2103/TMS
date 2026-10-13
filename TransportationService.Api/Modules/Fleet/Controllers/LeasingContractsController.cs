using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Services;

namespace TransportationService.Api.Modules.Fleet.Controllers;

/// <summary>
/// Leasing contracts for vehicles/trailers. Metadata is visible to fleet viewers; financial
/// values are only served with fleet_finance.view and mutable with fleet_finance.manage.
/// </summary>
[ApiController]
public class LeasingContractsController : ControllerBase
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly ILeasingContractService _service;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _authorization;

    public LeasingContractsController(ILeasingContractService service,
        ICurrentUserContext currentUser, IPermissionAuthorizationService authorization)
    {
        _service = service;
        _currentUser = currentUser;
        _authorization = authorization;
    }

    private async Task<bool> HasFinanceViewAsync(CancellationToken cancellationToken)
        => _currentUser.CurrentUserId is { } userId
           && await _authorization.UserHasPermissionAsync(userId, PermissionCodes.FleetFinanceView, cancellationToken);

    [HttpGet("api/vehicles/{vehicleId:guid}/leasing-contracts")]
    [RequirePermission(PermissionCodes.VehiclesView)]
    public async Task<ActionResult<IReadOnlyList<LeasingContractDto>>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var rows = await _service.ListForVehicleAsync(vehicleId, await HasFinanceViewAsync(cancellationToken), cancellationToken);
        return rows is null ? NotFound() : Ok(rows);
    }

    [HttpGet("api/trailers/{trailerId:guid}/leasing-contracts")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<IReadOnlyList<LeasingContractDto>>> ListForTrailer(Guid trailerId, CancellationToken cancellationToken)
    {
        var rows = await _service.ListForTrailerAsync(trailerId, await HasFinanceViewAsync(cancellationToken), cancellationToken);
        return rows is null ? NotFound() : Ok(rows);
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/leasing-contracts")]
    [RequirePermission(PermissionCodes.FleetFinanceManage)]
    public async Task<ActionResult<LeasingContractDto>> CreateForVehicle(Guid vehicleId, SaveLeasingContractRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateForVehicleAsync(vehicleId, request, await HasFinanceViewAsync(cancellationToken), cancellationToken));
    }

    [HttpPost("api/trailers/{trailerId:guid}/leasing-contracts")]
    [RequirePermission(PermissionCodes.FleetFinanceManage)]
    public async Task<ActionResult<LeasingContractDto>> CreateForTrailer(Guid trailerId, SaveLeasingContractRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateForTrailerAsync(trailerId, request, await HasFinanceViewAsync(cancellationToken), cancellationToken));
    }

    [HttpPut("api/leasing-contracts/{id:guid}")]
    [RequirePermission(PermissionCodes.FleetFinanceManage)]
    public async Task<ActionResult<LeasingContractDto>> Update(Guid id, SaveLeasingContractRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, await HasFinanceViewAsync(cancellationToken), cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("api/leasing-contracts/{id:guid}")]
    [RequirePermission(PermissionCodes.FleetFinanceManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("api/leasing-contracts/{id:guid}/document")]
    [RequirePermission(PermissionCodes.FleetFinanceManage)]
    [RequestSizeLimit(MaxBytes + 1024)]
    public async Task<ActionResult<LeasingContractDto>> Upload(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (FleetFileHelpers.Validate(file, MaxBytes, AllowedExtensions) is { } error)
        {
            return BadRequest(new { message = error });
        }

        await using var stream = file.OpenReadStream();
        var updated = await _service.AttachFileAsync(id, file.FileName, FleetFileHelpers.ContentTypeFor(file.FileName), stream, await HasFinanceViewAsync(cancellationToken), cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("api/leasing-contracts/{id:guid}/document")]
    [RequirePermission(PermissionCodes.FleetFinanceView)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var file = await _service.OpenFileAsync(id, cancellationToken);
        return file is { } found ? File(found.Content, found.ContentType, found.FileName) : NotFound();
    }
}
