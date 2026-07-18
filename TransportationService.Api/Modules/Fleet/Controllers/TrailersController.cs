using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Fleet.Controllers;

[ApiController]
[Route("api/trailers")]
public class TrailersController : ControllerBase
{
    private readonly ITrailerService _service;

    public TrailersController(ITrailerService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<PagedResult<TrailerListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] TrailerOperationalStatus? status,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? categoryId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _service.SearchAsync(search, status, isActive, categoryId, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("options")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<IReadOnlyList<TrailerOptionDto>>> Options(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetOptionsAsync(cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.TrailersView)]
    public async Task<ActionResult<TrailerDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var trailer = await _service.GetByIdAsync(id, cancellationToken);
        return trailer is null ? NotFound() : Ok(trailer);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.TrailersCreate)]
    public async Task<ActionResult<TrailerDetailDto>> Create(CreateTrailerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return BadRequest(new { message = "Kenteken is verplicht." });
        }

        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.TrailersEdit)]
    public async Task<ActionResult<TrailerDetailDto>> Update(Guid id, UpdateTrailerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LicensePlate))
        {
            return BadRequest(new { message = "Kenteken is verplicht." });
        }

        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.TrailersDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<TrailerDetailDto> Handle(TrailerOperationResult result, bool created) => result.Outcome switch
    {
        TrailerOperationOutcome.Success when created => CreatedAtAction(nameof(GetById), new { id = result.Trailer!.Id }, result.Trailer),
        TrailerOperationOutcome.Success => Ok(result.Trailer),
        TrailerOperationOutcome.NotFound => NotFound(),
        TrailerOperationOutcome.DuplicateLicensePlate => Conflict(new { message = "Er bestaat al een oplegger met dit kenteken." }),
        _ => Conflict(),
    };
}
