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

    // --- Wave 6 §4: unified problem view ---

    [HttpGet("/api/problems")]
    [RequirePermission(PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IReadOnlyList<ProblemListItemDto>>> ListProblems(CancellationToken cancellationToken)
        => Ok(await _service.ListProblemsAsync(cancellationToken));

    // --- Wave 6: charge decision + linked redelivery ---

    [HttpPost("{id:guid}/charge/propose")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> ProposeCharge(
        Guid id, ProposeIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.ProposeChargeAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    // The attribute gate stays IncidentsManage: the SERVICE enforces problems.approve_charge
    // fail-closed (registered in Phase8SupplyChainTests), mirroring the L7 override pattern.
    [HttpPost("{id:guid}/charge/decide")]
    [RequirePermission(PermissionCodes.IncidentsManage)]
    public async Task<ActionResult<IncidentDetailDto>> DecideCharge(
        Guid id, DecideIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var incident = await _service.DecideChargeAsync(id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpPost("{id:guid}/redelivery")]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IncidentDetailDto>> CreateRedelivery(Guid id, CancellationToken cancellationToken)
    {
        var incident = await _service.CreateRedeliveryAsync(id, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }
}
