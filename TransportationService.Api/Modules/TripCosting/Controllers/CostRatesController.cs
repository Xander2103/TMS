using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.TripCosting.Dtos;
using TransportationService.Api.Modules.TripCosting.Services;

namespace TransportationService.Api.Modules.TripCosting.Controllers;

/// <summary>Effective-dated cost rate cards. Rates are sensitive: even reading needs trip_costs.view.</summary>
[ApiController]
[Route("api/cost-rates")]
public class CostRatesController : ControllerBase
{
    private readonly ICostRateService _service;

    public CostRatesController(ICostRateService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.TripCostsView, PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<IReadOnlyList<CostRateSetDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _service.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.TripCostsView, PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<CostRateSetDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var set = await _service.GetByIdAsync(id, cancellationToken);
        return set is null ? NotFound() : Ok(set);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<CostRateSetDto>> Create(SaveCostRateSetRequest request, CancellationToken cancellationToken)
    {
        var (result, error) = await _service.CreateAsync(request, cancellationToken);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<ActionResult<CostRateSetDto>> Update(
        Guid id, SaveCostRateSetRequest request, CancellationToken cancellationToken)
    {
        var (result, error) = await _service.UpdateAsync(id, request, cancellationToken);
        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TripCostsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}
