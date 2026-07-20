using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Services;

namespace TransportationService.Api.Modules.Incidents.Controllers;

[ApiController]
[Route("api/incidents")]
public class IncidentsController : ControllerBase
{
    private readonly IIncidentService _service;

    public IncidentsController(IIncidentService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IReadOnlyList<IncidentListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? severity,
        [FromQuery] Guid? dossierId, [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(search, status, severity, dossierId, customerId, cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> Create(SaveIncidentRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _service.GetAsync(id, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> Update(Guid id, SaveIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.UpdateAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> ChangeStatus(Guid id, ChangeIncidentStatusRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.ChangeStatusAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }
}
