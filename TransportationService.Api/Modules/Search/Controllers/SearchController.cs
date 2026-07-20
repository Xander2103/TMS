using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Search.Services;

namespace TransportationService.Api.Modules.Search.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly IGlobalSearchService _service;

    public SearchController(IGlobalSearchService service)
    {
        _service = service;
    }

    /// <summary>
    /// Global command-palette search. No dedicated permission: every category is filtered
    /// on the caller's own view permissions inside the service.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<SearchHitDto>>> Search([FromQuery] string q, CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchAsync(q ?? string.Empty, cancellationToken));
    }
}
