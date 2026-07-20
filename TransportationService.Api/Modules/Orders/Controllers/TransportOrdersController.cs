using System.Text;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>
/// Every endpoint lists its specific permission plus the orders.manage umbrella (any-of).
/// View/create/edit/change-status/cancel/delete/export are deliberately separate permissions
/// so read-only, planner and management roles can be scoped precisely.
/// </summary>
[ApiController]
[Route("api/transport-orders")]
public class TransportOrdersController : ControllerBase
{
    private readonly ITransportOrderService _service;
    private readonly TransportationService.Api.Modules.Edi.Services.IEdiService _ediService;
    private readonly ITransportOrderTimelineService _timelineService;
    private readonly TransportationService.Api.Modules.Packages.Services.IPackageGenerationService _packageGeneration;

    public TransportOrdersController(
        ITransportOrderService service,
        TransportationService.Api.Modules.Edi.Services.IEdiService ediService,
        ITransportOrderTimelineService timelineService,
        TransportationService.Api.Modules.Packages.Services.IPackageGenerationService packageGeneration)
    {
        _service = service;
        _ediService = ediService;
        _timelineService = timelineService;
        _packageGeneration = packageGeneration;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<PagedResult<TransportOrderListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] TransportOrderStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchAsync(
            search, status, customerId, fromDate, toDate, PageRequest.Of(page, pageSize), cancellationToken));
    }

    [HttpGet("export")]
    [RequirePermission(PermissionCodes.OrdersExport, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> Export(
        [FromQuery] string? search,
        [FromQuery] TransportOrderStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var rows = await _service.ListForExportAsync(search, status, customerId, fromDate, toDate, cancellationToken);

        // Semicolon-separated CSV with UTF-8 BOM so Excel (EU locale) opens it correctly.
        var csv = new StringBuilder();
        csv.AppendLine("Opdrachtnummer;Datum;Klant;Referentie;Status;Omschrijving;Van;Naar;Stops;ADR;Kraan");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(';',
                Escape(row.OrderNumber), row.OrderDate.ToString("yyyy-MM-dd"), Escape(row.CustomerName),
                Escape(row.CustomerReference), row.Status.ToString(), Escape(row.GoodsDescription),
                Escape(row.FirstLoadingCity), Escape(row.LastUnloadingCity), row.StopCount.ToString(),
                row.AdrRequired ? "ja" : "nee", row.CraneRequired ? "ja" : "nee"));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "transportopdrachten.csv");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Guard against CSV formula injection: user-entered text (customer names,
        // references) must never execute as a formula when the export opens in Excel.
        if ("=+-@\t\r".IndexOf(value[0]) >= 0)
        {
            value = "'" + value;
        }

        return value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _service.GetByIdAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.OrdersCreate, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> Create(
        CreateTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> Update(
        Guid id, UpdateTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    /// <summary>
    /// Confirms the time window / appointment / instructions of one stop. Deliberately separate
    /// from the full update so dispatch can still confirm windows after planning locked the order.
    /// </summary>
    [HttpPut("{id:guid}/stops/{stopId:guid}/execution-plan")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> UpdateStopExecutionPlan(
        Guid id, Guid stopId, UpdateStopExecutionPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateStopExecutionPlanAsync(id, stopId, request, cancellationToken);
        return Handle(result, created: false);
    }

    /// <summary>Inline priority edit; lighter permission than a full order edit on purpose.</summary>
    [HttpPost("{id:guid}/priority")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> ChangePriority(
        Guid id, ChangeOrderPriorityRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ChangePriorityAsync(id, request.Priority, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> ChangeStatus(
        Guid id, ChangeTransportOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ChangeStatusAsync(id, request.Status, cancellationToken);
        if (result.Outcome == TransportOrderOperationOutcome.Success)
        {
            // Orders that entered via EDI report their status back (no-op for the rest).
            await _ediService.QueueOutboundStatusAsync(id, request.Status.ToString(), cancellationToken);

            if (request.Status == Entities.TransportOrderStatus.Confirmed)
            {
                // Confirmation generates the packages the cargo lines call for (idempotent —
                // re-confirming after a Draft correction never duplicates).
                await _packageGeneration.GenerateForOrderAsync(id, cancellationToken);
            }
        }

        return Handle(result, created: false);
    }

    /// <summary>
    /// Bulk status change: each order runs through the exact same transition rules and side
    /// effects as a single change; failures are reported per order and never block the rest.
    /// </summary>
    [HttpPost("bulk-status")]
    [RequirePermission(PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<BulkStatusResultDto>> BulkChangeStatus(
        BulkChangeStatusRequest request, CancellationToken cancellationToken)
    {
        var ids = (request.OrderIds ?? []).Distinct().Take(100).ToList();
        if (ids.Count == 0)
        {
            return BadRequest(new { message = "Geen opdrachten geselecteerd." });
        }

        var results = new List<BulkStatusItemResultDto>();
        foreach (var orderId in ids)
        {
            var result = await _service.ChangeStatusAsync(orderId, request.Status, cancellationToken);
            if (result.Outcome == TransportOrderOperationOutcome.Success)
            {
                await _ediService.QueueOutboundStatusAsync(orderId, request.Status.ToString(), cancellationToken);
                if (request.Status == Entities.TransportOrderStatus.Confirmed)
                {
                    await _packageGeneration.GenerateForOrderAsync(orderId, cancellationToken);
                }

                results.Add(new BulkStatusItemResultDto(orderId, true, null));
            }
            else
            {
                results.Add(new BulkStatusItemResultDto(orderId, false,
                    result.Outcome == TransportOrderOperationOutcome.NotFound
                        ? "Opdracht niet gevonden."
                        : result.Error ?? "De statuswijziging is niet toegestaan."));
            }
        }

        return Ok(new BulkStatusResultDto(
            results.Count(r => r.Success), results.Count(r => !r.Success), results));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.OrdersCancel, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> Cancel(
        Guid id, CancelTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(id, request.Reason, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpGet("{id:guid}/timeline")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<OrderTimelineEventDto>>> Timeline(Guid id, CancellationToken cancellationToken)
    {
        var timeline = await _timelineService.GetTimelineAsync(id, cancellationToken);
        return timeline is null ? NotFound() : Ok(timeline);
    }

    [HttpPost("{id:guid}/correct-status")]
    [RequirePermission(PermissionCodes.OrdersCorrectStatus, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> CorrectStatus(
        Guid id, CorrectTransportOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CorrectStatusAsync(id, request.TargetStatus, request.Reason, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.OrdersDelete, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Outcome switch
        {
            TransportOrderOperationOutcome.Success => NoContent(),
            TransportOrderOperationOutcome.NotFound => NotFound(),
            TransportOrderOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
    }

    private ActionResult<TransportOrderDetailDto> Handle(TransportOrderOperationResult result, bool created) =>
        result.Outcome switch
        {
            TransportOrderOperationOutcome.Success when created =>
                CreatedAtAction(nameof(GetById), new { id = result.Order!.Id }, result.Order),
            TransportOrderOperationOutcome.Success => Ok(result.Order),
            TransportOrderOperationOutcome.NotFound => NotFound(),
            TransportOrderOperationOutcome.InvalidReference => BadRequest(new { message = result.Error }),
            TransportOrderOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            TransportOrderOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
}
