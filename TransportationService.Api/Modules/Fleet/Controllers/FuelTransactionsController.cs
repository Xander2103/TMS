using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
public class FuelTransactionsController : ControllerBase
{
    private readonly IFuelService _service;

    public FuelTransactionsController(IFuelService service)
    {
        _service = service;
    }

    [HttpGet("api/vehicles/{vehicleId:guid}/fuel-transactions")]
    [RequirePermission(PermissionCodes.FuelView)]
    public async Task<ActionResult<FuelOverviewDto>> ListForVehicle(Guid vehicleId, CancellationToken cancellationToken)
    {
        var overview = await _service.ListForVehicleAsync(vehicleId, cancellationToken);
        return overview is null ? NotFound() : Ok(overview);
    }

    [HttpPost("api/vehicles/{vehicleId:guid}/fuel-transactions")]
    [RequirePermission(PermissionCodes.FuelCreate)]
    public async Task<ActionResult<FuelTransactionDto>> Create(
        Guid vehicleId, CreateFuelTransactionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateForVehicleAsync(vehicleId, request, cancellationToken);
        return Handle(result, created: true);
    }

    /// <summary>Recent refuellings with anomalies (odometer regressions, consumption outliers) for the fleet dashboard.</summary>
    [HttpGet("api/fuel-transactions/warnings")]
    [RequirePermission(PermissionCodes.FuelView)]
    public async Task<ActionResult<IReadOnlyList<FuelWarningDto>>> RecentWarnings(
        [FromQuery] int take = 10, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListRecentWarningsAsync(take, cancellationToken));
    }

    [HttpPut("api/fuel-transactions/{id:guid}")]
    [RequirePermission(PermissionCodes.FuelEdit)]
    public async Task<ActionResult<FuelTransactionDto>> Update(
        Guid id, UpdateFuelTransactionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("api/fuel-transactions/{id:guid}")]
    [RequirePermission(PermissionCodes.FuelDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<FuelTransactionDto> Handle(FuelOperationResult result, bool created) => result.Outcome switch
    {
        FuelOperationOutcome.Success when created => StatusCode(StatusCodes.Status201Created, result.Transaction),
        FuelOperationOutcome.Success => Ok(result.Transaction),
        FuelOperationOutcome.NotFound => NotFound(),
        FuelOperationOutcome.OwnerNotFound => NotFound(new { message = "Het voertuig bestaat niet." }),
        FuelOperationOutcome.InvalidReference =>
            BadRequest(new { message = "De gekoppelde chauffeur of tankkaart bestaat niet." }),
        FuelOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
