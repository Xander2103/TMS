using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/tank-cards")]
public class TankCardsController : ControllerBase
{
    private readonly ITankCardService _service;

    public TankCardsController(ITankCardService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.TankCardsView)]
    public async Task<ActionResult<PagedResult<TankCardDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] TankCardStatus? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchAsync(search, status, PageRequest.Of(page, pageSize), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.TankCardsView)]
    public async Task<ActionResult<TankCardDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var card = await _service.GetByIdAsync(id, cancellationToken);
        return card is null ? NotFound() : Ok(card);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TankCardsCreate)]
    public async Task<ActionResult<TankCardDto>> Create(CreateTankCardRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TankCardsEdit)]
    public async Task<ActionResult<TankCardDto>> Update(Guid id, UpdateTankCardRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/blocked")]
    [RequirePermission(PermissionCodes.TankCardsBlock)]
    public async Task<ActionResult<TankCardDto>> SetBlocked(
        Guid id, SetTankCardBlockedRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.SetBlockedAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TankCardsDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<TankCardDto> Handle(TankCardOperationResult result, bool created) => result.Outcome switch
    {
        TankCardOperationOutcome.Success when created =>
            CreatedAtAction(nameof(GetById), new { id = result.Card!.Id }, result.Card),
        TankCardOperationOutcome.Success => Ok(result.Card),
        TankCardOperationOutcome.NotFound => NotFound(),
        TankCardOperationOutcome.DuplicateCardNumber =>
            Conflict(new { message = "Er bestaat al een tankkaart met dit kaartnummer." }),
        TankCardOperationOutcome.InvalidReference =>
            BadRequest(new { message = "Het gekoppelde voertuig of de gekoppelde chauffeur bestaat niet." }),
        TankCardOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
