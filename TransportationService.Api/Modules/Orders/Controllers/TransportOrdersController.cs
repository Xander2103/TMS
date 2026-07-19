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

    public TransportOrdersController(ITransportOrderService service)
    {
        _service = service;
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

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> ChangeStatus(
        Guid id, ChangeTransportOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ChangeStatusAsync(id, request.Status, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.OrdersCancel, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<TransportOrderDetailDto>> Cancel(
        Guid id, CancelTransportOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CancelAsync(id, request.Reason, cancellationToken);
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
