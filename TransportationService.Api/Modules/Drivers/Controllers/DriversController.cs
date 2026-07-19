using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Drivers.Controllers;

[ApiController]
[Route("api/drivers")]
public class DriversController : ControllerBase
{
    private readonly IDriverService _driverService;

    public DriversController(IDriverService driverService)
    {
        _driverService = driverService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DriversView)]
    public async Task<ActionResult<PagedResult<DriverListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isBlocked,
        [FromQuery] Guid? categoryId,
        [FromQuery] Entities.DriverAvailabilityStatus? availabilityStatus,
        [FromQuery] Guid? qualificationTypeId,
        [FromQuery] int? expiringWithinDays,
        [FromQuery] bool hasExpired = false,
        [FromQuery] bool eligibleOnly = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _driverService.SearchAsync(
            search, isActive, isBlocked, categoryId, availabilityStatus, qualificationTypeId,
            expiringWithinDays, hasExpired, eligibleOnly, PageRequest.Of(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.DriversView)]
    public async Task<ActionResult<DriverDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var driver = await _driverService.GetByIdAsync(id, cancellationToken);
        return driver is null ? NotFound() : Ok(driver);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DriversCreate)]
    public async Task<ActionResult<DriverDetailDto>> Create(CreateDriverRequest request, CancellationToken cancellationToken)
    {
        if (request.EmployeeId == Guid.Empty)
        {
            return BadRequest(new { message = "Een medewerker is verplicht." });
        }

        var result = await _driverService.CreateAsync(request, cancellationToken);
        return result.Outcome switch
        {
            DriverOperationOutcome.Success => CreatedAtAction(nameof(GetById), new { id = result.Driver!.Id }, result.Driver),
            DriverOperationOutcome.EmployeeNotFound => BadRequest(new { message = "De opgegeven medewerker bestaat niet." }),
            DriverOperationOutcome.EmployeeAlreadyDriver => Conflict(new { message = "Deze medewerker is al gekoppeld aan een chauffeur." }),
            DriverOperationOutcome.InvalidReference => BadRequest(new { message = "Eén of meer gekoppelde records (categorie, voertuig of oplegger) bestaan niet." }),
            _ => NotFound(),
        };
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.DriversEdit)]
    public async Task<ActionResult<DriverDetailDto>> Update(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken)
    {
        var result = await _driverService.UpdateAsync(id, request, cancellationToken);
        return Handle(result);
    }

    [HttpPost("{id:guid}/blocked")]
    [RequirePermission(PermissionCodes.DriversBlock)]
    public async Task<ActionResult<DriverDetailDto>> SetBlocked(Guid id, SetDriverBlockedRequest request, CancellationToken cancellationToken)
    {
        var result = await _driverService.SetBlockedAsync(id, request, cancellationToken);
        return Handle(result);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.DriversDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        return await _driverService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult<DriverDetailDto> Handle(DriverOperationResult result) => result.Outcome switch
    {
        DriverOperationOutcome.Success => Ok(result.Driver),
        DriverOperationOutcome.NotFound => NotFound(),
        DriverOperationOutcome.InvalidReference => BadRequest(new { message = "Eén of meer gekoppelde records (categorie, voertuig of oplegger) bestaan niet." }),
        _ => Conflict(),
    };
}
