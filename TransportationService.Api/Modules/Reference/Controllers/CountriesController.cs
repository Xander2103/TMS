using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Reference.Dtos;

namespace TransportationService.Api.Modules.Reference.Controllers;

/// <summary>
/// Read-only global country reference. Countries are not tenant master data: every
/// authenticated user may list them (country comboboxes appear on most forms), and no
/// tenant-level CRUD exists — the list is synchronised from the ISO seed at startup.
/// </summary>
[ApiController]
[Route("api/countries")]
[Authorize]
public class CountriesController : ControllerBase
{
    private readonly TransportationDbContext _dbContext;

    public CountriesController(TransportationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<CountryOptionDto>>> GetOptions(CancellationToken cancellationToken)
    {
        var options = await _dbContext.Countries
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CountryOptionDto(c.Code, c.Alpha3, c.Name, c.IsEuMember))
            .ToListAsync(cancellationToken);

        return Ok(options);
    }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<CountryOptionDto>>> GetAll(CancellationToken cancellationToken)
        => GetOptions(cancellationToken);
}
