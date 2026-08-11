using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Dossiers.Controllers;

[ApiController]
[Route("api/dossiers")]
public class DossiersController : ControllerBase
{
    private readonly IDossierService _service;
    private readonly IDossierActivityService _activityService;
    private readonly IDossierReadinessService _readinessService;

    public DossiersController(
        IDossierService service,
        IDossierActivityService activityService,
        IDossierReadinessService readinessService)
    {
        _service = service;
        _activityService = activityService;
        _readinessService = readinessService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<IReadOnlyList<DossierListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(search, status, customerId, cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Create(SaveDossierRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dossier = await _service.GetAsync(id, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Update(Guid id, SaveDossierRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _service.UpdateAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/close")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Close(Guid id, CancellationToken cancellationToken)
    {
        var dossier = await _service.CloseAsync(id, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/reopen")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Reopen(Guid id, CancellationToken cancellationToken)
    {
        var dossier = await _service.ReopenAsync(id, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPut("{id:guid}/legal-entity")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> ChangeLegalEntity(
        Guid id, ChangeDossierEntityRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _service.ChangeLegalEntityAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/activities")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> AddActivity(
        Guid id, SaveDossierActivityRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityService.AddAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPut("{id:guid}/activities/{activityId:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> UpdateActivity(
        Guid id, Guid activityId, SaveDossierActivityRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityService.UpdateAsync(id, activityId, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpDelete("{id:guid}/activities/{activityId:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> DeleteActivity(
        Guid id, Guid activityId, [FromQuery] Guid? version, CancellationToken cancellationToken)
    {
        var dossier = await _activityService.DeleteAsync(id, activityId, version, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/activities/reorder")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> ReorderActivities(
        Guid id, ReorderDossierActivitiesRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityService.ReorderAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    /// <summary>Dashboard tile: open dossiers with structural attention.</summary>
    [HttpGet("attention-count")]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<object>> AttentionCount(CancellationToken cancellationToken)
    {
        return Ok(new { Count = await _readinessService.CountDossiersWithAttentionAsync(cancellationToken) });
    }

    [HttpPost("{id:guid}/orders")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> LinkOrder(Guid id, LinkDossierOrderRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _service.LinkOrderAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpDelete("{id:guid}/orders/{transportOrderId:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> UnlinkOrder(Guid id, Guid transportOrderId, CancellationToken cancellationToken)
    {
        var dossier = await _service.UnlinkOrderAsync(id, transportOrderId, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/relations")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> AddRelation(Guid id, AddDossierRelationRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _service.AddRelationAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpDelete("{id:guid}/relations/{relationId:guid}")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> RemoveRelation(Guid id, Guid relationId, CancellationToken cancellationToken)
    {
        var dossier = await _service.RemoveRelationAsync(id, relationId, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }
}
