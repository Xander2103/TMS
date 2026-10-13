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
    private readonly IDossierActivityPricingService _activityPricingService;
    private readonly IDossierActivityPlanningService _activityPlanningService;
    private readonly IDossierLifecycleService _lifecycle;

    public DossiersController(
        IDossierService service,
        IDossierActivityService activityService,
        IDossierReadinessService readinessService,
        IDossierActivityPricingService activityPricingService,
        IDossierActivityPlanningService activityPlanningService,
        IDossierLifecycleService lifecycle)
    {
        _lifecycle = lifecycle;
        _service = service;
        _activityService = activityService;
        _readinessService = readinessService;
        _activityPricingService = activityPricingService;
        _activityPlanningService = activityPlanningService;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<IReadOnlyList<DossierListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.ListAsync(search, status, customerId, cancellationToken));
    }

    /// <summary>
    /// Dossier search sprint 2026-09-23: the server-side search behind the dossier list — global
    /// search, primary + advanced filters, whitelisted sort, bounded pagination. All tenant-scoped.
    /// </summary>
    [HttpGet("search")]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<Common.Models.PagedResult<DossierListItemDto>>> Search(
        [FromQuery] DossierSearchQuery query, CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchAsync(query, cancellationToken));
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

    // ------------------------------------------------------------ lifecycle (confirmation sprint 2026-09-23)

    /// <summary>What confirming this dossier means right now: blockers, warnings, auto-blockers, summary.</summary>
    [HttpGet("{id:guid}/confirmation")]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierConfirmationEvaluationDto>> Confirmation(Guid id, CancellationToken cancellationToken)
    {
        var evaluation = await _lifecycle.EvaluateAsync(id, cancellationToken);
        return evaluation is null ? NotFound() : Ok(evaluation);
    }

    /// <summary>Manual confirmation (Status Closed = "Bevestigd"). Warnings must be acknowledged; blockers refuse.</summary>
    [HttpPost("{id:guid}/confirm")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Confirm(Guid id, ConfirmDossierRequest? request, CancellationToken cancellationToken)
    {
        var dossier = await _lifecycle.ConfirmAsync(id, request ?? new ConfirmDossierRequest(), cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    /// <summary>Backward-compatible alias of confirm (pre-sprint clients): warnings are acknowledged implicitly.</summary>
    [HttpPost("{id:guid}/close")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Close(Guid id, CancellationToken cancellationToken)
    {
        var dossier = await _lifecycle.ConfirmAsync(id, new ConfirmDossierRequest(null, AcknowledgeWarnings: true), cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/reopen")]
    [RequirePermission(PermissionCodes.DossiersReopen)]
    public async Task<ActionResult<DossierDetailDto>> Reopen(Guid id, ReopenDossierRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _lifecycle.ReopenAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> Cancel(Guid id, CancelDossierRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _lifecycle.CancelAsync(id, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    /// <summary>Sprint 6: what moving this dossier (and its orders) to another customer would do.</summary>
    [HttpGet("{id:guid}/customer/impact")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierCustomerChangeImpactDto>> CustomerChangeImpact(
        Guid id, [FromQuery] Guid newCustomerId, [FromServices] IDossierCustomerChangeService customerChange,
        CancellationToken cancellationToken)
    {
        var impact = await customerChange.PreviewAsync(id, newCustomerId, cancellationToken);
        return impact is null ? NotFound() : Ok(impact);
    }

    [HttpPut("{id:guid}/customer")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> ChangeCustomer(
        Guid id, ChangeDossierCustomerRequest request, [FromServices] IDossierCustomerChangeService customerChange,
        CancellationToken cancellationToken)
    {
        var result = await customerChange.ApplyAsync(id, request, cancellationToken);
        if (result is null) return NotFound();
        var dossier = await _service.GetAsync(id, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    [HttpGet("{id:guid}/legal-entity/impact")]
    [RequirePermission(PermissionCodes.DossiersView, PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierLegalEntityChangeImpactDto>> LegalEntityChangeImpact(
        Guid id, [FromQuery] Guid legalEntityId, CancellationToken cancellationToken)
    {
        var impact = await _service.PreviewLegalEntityChangeAsync(id, legalEntityId, cancellationToken);
        return impact is null ? NotFound() : Ok(impact);
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

    /// <summary>Creates the linked draft order for an existing order-less transport activity.</summary>
    [HttpPost("{id:guid}/activities/{activityId:guid}/create-order")]
    [RequirePermission(PermissionCodes.DossiersManage)]
    public async Task<ActionResult<DossierDetailDto>> CreateActivityOrder(
        Guid id, Guid activityId, CreateActivityOrderRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityService.CreateOrderForActivityAsync(id, activityId, request.Version, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    /// <summary>
    /// Step 13: agreed sales price of a standalone billable activity (Opslag, Kraanwerk, …) —
    /// the activity-side twin of <c>POST /api/transport-orders/{id}/pricing/one-off</c>. Its own
    /// commercial right (<c>dossiers.price</c>), never <c>dossiers.manage</c>: editing a dossier
    /// is not pricing it.
    /// </summary>
    [HttpPut("{id:guid}/activities/{activityId:guid}/price")]
    [RequirePermission(PermissionCodes.DossiersPrice)]
    public async Task<ActionResult<DossierDetailDto>> SetActivityPrice(
        Guid id, Guid activityId, SetActivityPriceRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityPricingService.SetAgreedPriceAsync(id, activityId, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    /// <summary>
    /// D5: sales LINES of a standalone billable activity (id-preserving replace; the server
    /// computes every amount). Same commercial right as the fixed price — <c>dossiers.price</c>.
    /// </summary>
    [HttpPut("{id:guid}/activities/{activityId:guid}/price-lines")]
    [RequirePermission(PermissionCodes.DossiersPrice)]
    public async Task<ActionResult<DossierDetailDto>> SetActivityPriceLines(
        Guid id, Guid activityId, SetActivityPriceLinesRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityPricingService.SetPriceLinesAsync(id, activityId, request, cancellationToken);
        return dossier is null ? NotFound() : Ok(dossier);
    }

    /// <summary>
    /// D1: "Inplannen" — puts the activity's order on a Draft trip (or returns the existing open
    /// trip unchanged; idempotent). A PLANNING right, not a dossier right: it creates a trip.
    /// Driver/vehicle/trailer are changed afterwards through the trip endpoints.
    /// </summary>
    [HttpPost("{id:guid}/activities/{activityId:guid}/plan")]
    [RequirePermission(PermissionCodes.PlanningCreate)]
    public async Task<ActionResult<DossierDetailDto>> PlanActivity(
        Guid id, Guid activityId, PlanDossierActivityRequest request, CancellationToken cancellationToken)
    {
        var dossier = await _activityPlanningService.PlanAsync(id, activityId, request, cancellationToken);
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
