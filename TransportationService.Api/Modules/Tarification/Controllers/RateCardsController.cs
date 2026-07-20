using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Services;

namespace TransportationService.Api.Modules.Tarification.Controllers;

[ApiController]
[Route("api/rate-cards")]
public class RateCardsController : ControllerBase
{
    private readonly IRateCardService _service;

    public RateCardsController(IRateCardService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<IReadOnlyList<RateCardDto>>> List([FromQuery] Guid? customerId, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(customerId, cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<RateCardDto>> Create(SaveRateCardRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<ActionResult<RateCardDto>> Update(Guid id, SaveRateCardRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TariffsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("quote")]
    [RequirePermission(PermissionCodes.TariffsView, PermissionCodes.TariffsManage)]
    public async Task<ActionResult<QuoteDto>> Quote(QuoteRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.QuoteAsync(request, cancellationToken));
    }
}
