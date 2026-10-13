using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Modules.Employees.Controllers;

/// <summary>Read models over the stock domain: the status overview and the open alerts.</summary>
[ApiController]
[Route("api/inventory")]
public class InventoryInsightsController : ControllerBase
{
    private readonly IInventoryInsightsService _service;

    public InventoryInsightsController(IInventoryInsightsService service)
    {
        _service = service;
    }

    [HttpGet("overview")]
    [RequirePermission(PermissionCodes.InventoryView, PermissionCodes.InventoryManage)]
    public async Task<ActionResult<IReadOnlyList<InventoryOverviewRowDto>>> Overview(
        [FromQuery] InventoryStatus? status = null, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.GetOverviewAsync(status, cancellationToken));
    }

    [HttpGet("alerts")]
    [RequirePermission(PermissionCodes.InventoryView, PermissionCodes.InventoryManage)]
    public async Task<ActionResult<IReadOnlyList<InventoryAlertDto>>> Alerts(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetAlertsAsync(cancellationToken));
    }

    [HttpGet("loans")]
    [RequirePermission(PermissionCodes.InventoryLoansView, PermissionCodes.IssuedItemsManage, PermissionCodes.InventoryView)]
    public async Task<ActionResult<IReadOnlyList<InventoryLoanDto>>> Loans(
        [FromQuery] bool overdueOnly = false, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.GetLoansAsync(overdueOnly, cancellationToken));
    }
}

/// <summary>Reorder proposals: the reviewable precursor to purchasing.</summary>
[ApiController]
[Route("api/inventory/reorder-proposals")]
public class ReorderProposalsController : ControllerBase
{
    private readonly IReorderProposalService _service;

    public ReorderProposalsController(IReorderProposalService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.InventoryReorderView, PermissionCodes.InventoryReorderManage)]
    public async Task<ActionResult<IReadOnlyList<ReorderProposalDto>>> List(
        [FromQuery] bool openOnly = true, CancellationToken cancellationToken = default)
    {
        return Ok(await _service.ListAsync(openOnly, cancellationToken));
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.InventoryReorderManage)]
    public async Task<ActionResult<ReorderProposalDto>> Create(
        CreateReorderProposalRequest request, CancellationToken cancellationToken)
    {
        return Ok(await _service.CreateAsync(request, cancellationToken));
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.InventoryReorderManage)]
    public async Task<ActionResult<ReorderProposalDto>> ChangeStatus(
        Guid id, ReorderProposalStatusRequest request, CancellationToken cancellationToken)
    {
        var proposal = await _service.ChangeStatusAsync(id, request, cancellationToken);
        return proposal is null ? NotFound() : Ok(proposal);
    }
}
