using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Portal.Entities;
using TransportationService.Api.Modules.Portal.Services;

namespace TransportationService.Api.Modules.Portal.Controllers;

/// <summary>
/// Favorites, recent items and pinned resources of the signed-in user. Self-scoped like the
/// rest of the /api/me surface: authentication is the gate; the service rechecks per-type
/// view permissions when listing.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me/resource-links")]
public class MeResourceLinksController : ControllerBase
{
    private readonly IResourceLinkService _service;

    public MeResourceLinksController(IResourceLinkService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ResourceLinkDto>>> List(
        [FromQuery] ResourceLinkKind? kind, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListMineAsync(kind, cancellationToken));
    }

    [HttpPut]
    public async Task<ActionResult<ResourceLinkDto>> Touch(
        TouchResourceLinkRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.TouchAsync(request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _service.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("order")]
    public async Task<IActionResult> Reorder(ReorderResourceLinksRequest request, CancellationToken cancellationToken)
    {
        await _service.ReorderAsync(request.Ids, cancellationToken);
        return NoContent();
    }
}
