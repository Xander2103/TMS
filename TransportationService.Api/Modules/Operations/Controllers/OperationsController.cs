using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Operations.Dtos;
using TransportationService.Api.Modules.Operations.Entities;
using TransportationService.Api.Modules.Operations.Services;

namespace TransportationService.Api.Modules.Operations.Controllers;

/// <summary>The live control center (/operations): overview projection + alert lifecycle.</summary>
[ApiController]
[Route("api/operations")]
public class OperationsController : ControllerBase
{
    private readonly IOperationsOverviewService _overviewService;
    private readonly IAlertService _alertService;
    private readonly IAlertSyncService _alertSyncService;

    public OperationsController(
        IOperationsOverviewService overviewService,
        IAlertService alertService,
        IAlertSyncService alertSyncService)
    {
        _overviewService = overviewService;
        _alertService = alertService;
        _alertSyncService = alertSyncService;
    }

    /// <summary>
    /// Read-only projection (PERF-HIGH 4). It used to run the alert sync inline, which turned every
    /// 30-second poll of every open tab into ~1,000 UPDATEs; reconciliation now belongs to
    /// <see cref="OperationalAlertSyncHostedService"/>, with <see cref="SyncAlerts"/> as the explicit
    /// manual trigger. A GET never writes.
    /// </summary>
    [HttpGet("overview")]
    [RequirePermission(PermissionCodes.OperationsView)]
    public async Task<ActionResult<OperationsOverviewDto>> Overview(CancellationToken cancellationToken)
    {
        return Ok(await _overviewService.GetOverviewAsync(cancellationToken));
    }

    /// <summary>
    /// Manual refresh / recovery: reconciles the current tenant's alerts now instead of waiting for
    /// the next background tick. Same right as the overview — it changes no business data, only the
    /// derived alert projection — and it is idempotent and bounded (one write per changed row).
    /// </summary>
    [HttpPost("alerts/sync")]
    [RequirePermission(PermissionCodes.OperationsView)]
    public async Task<IActionResult> SyncAlerts(CancellationToken cancellationToken)
    {
        await _alertSyncService.SyncAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("alerts")]
    [RequirePermission(PermissionCodes.OperationsView)]
    public async Task<ActionResult<IReadOnlyList<OperationalAlertDto>>> Alerts(
        [FromQuery] AlertStatus? status,
        [FromQuery] AlertSeverity? severity,
        [FromQuery] string? category,
        CancellationToken cancellationToken)
    {
        return Ok(await _alertService.ListAsync(new AlertQuery(status, severity, category), cancellationToken));
    }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    [RequirePermission(PermissionCodes.OperationsManageAlerts)]
    public async Task<ActionResult<OperationalAlertDto>> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        var alert = await _alertService.AcknowledgeAsync(id, cancellationToken);
        return alert is null ? NotFound() : Ok(alert);
    }

    [HttpPost("alerts/{id:guid}/resolve")]
    [RequirePermission(PermissionCodes.OperationsManageAlerts)]
    public async Task<ActionResult<OperationalAlertDto>> Resolve(Guid id, CancellationToken cancellationToken)
    {
        var alert = await _alertService.ResolveAsync(id, cancellationToken);
        return alert is null ? NotFound() : Ok(alert);
    }

    [HttpPost("alerts/{id:guid}/assign")]
    [RequirePermission(PermissionCodes.OperationsManageAlerts)]
    public async Task<ActionResult<OperationalAlertDto>> Assign(
        Guid id, AssignAlertRequest request, CancellationToken cancellationToken)
    {
        var alert = await _alertService.AssignAsync(id, request.UserId, cancellationToken);
        return alert is null ? NotFound() : Ok(alert);
    }
}
