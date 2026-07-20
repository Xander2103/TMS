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

    public DossiersController(IDossierService service)
    {
        _service = service;
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
