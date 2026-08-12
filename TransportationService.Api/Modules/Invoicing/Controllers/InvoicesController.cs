using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Invoicing.Services;

namespace TransportationService.Api.Modules.Invoicing.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _service;
    private readonly IInvoicePdfService _pdfService;

    public InvoicesController(IInvoiceService service, IInvoicePdfService pdfService)
    {
        _service = service;
        _pdfService = pdfService;
    }

    [HttpGet("{id:guid}/pdf")]
    [RequirePermission(PermissionCodes.InvoicesView)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var pdf = await _pdfService.RenderAsync(id, cancellationToken);
        return pdf is null ? NotFound() : File(pdf.Value.Bytes, "application/pdf", pdf.Value.FileName);
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.InvoicesView)]
    public async Task<ActionResult<PagedResult<InvoiceListItemDto>>> Search(
        [FromQuery] string? search,
        [FromQuery] InvoiceStatus? status,
        [FromQuery] Guid? customerId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        return Ok(await _service.SearchAsync(search, status, customerId, PageRequest.Of(page, pageSize), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.InvoicesView)]
    public async Task<ActionResult<InvoiceDetailDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _service.GetByIdAsync(id, cancellationToken);
        return invoice is null ? NotFound() : Ok(invoice);
    }

    /// <summary>Completed, not-yet-invoiced orders of a customer for the invoice builder.</summary>
    [HttpGet("uninvoiced-orders")]
    [RequirePermission(PermissionCodes.InvoicesView)]
    public async Task<ActionResult<IReadOnlyList<UninvoicedOrderDto>>> UninvoicedOrders(
        [FromQuery] Guid customerId, CancellationToken cancellationToken)
    {
        return Ok(await _service.ListUninvoicedOrdersAsync(customerId, cancellationToken));
    }

    /// <summary>Wave 10: the invoice-control workspace — proposals per grouping preference,
    /// review queue with readiness reasons, pending incident charges.</summary>
    [HttpGet("/api/invoice-control")]
    [RequirePermission(PermissionCodes.InvoicesView, PermissionCodes.InvoicesEdit)]
    public async Task<ActionResult<InvoiceControlDto>> Control(
        [FromServices] IInvoiceControlService control, CancellationToken cancellationToken)
        => Ok(await control.GetAsync(cancellationToken));

    public record SnoozeOrderRequest(DateOnly? Until, string? Reason);

    /// <summary>P12: postpone (or clear: Until = null) an order's invoicing — audited, and the
    /// workspace shows postponed orders in their own section until the date passes.</summary>
    [HttpPut("/api/invoice-control/orders/{orderId:guid}/snooze")]
    [RequirePermission(PermissionCodes.InvoicesEdit, PermissionCodes.InvoicesCreate)]
    public async Task<IActionResult> Snooze(
        Guid orderId, SnoozeOrderRequest request,
        [FromServices] TransportationService.Api.Data.TransportationDbContext dbContext,
        [FromServices] TransportationService.Api.Modules.Tenancy.Services.ITenantContext tenantContext,
        [FromServices] TransportationService.Api.Modules.Auditing.Services.IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var order = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            dbContext.TransportOrders,
            o => o.TenantId == tenantContext.TenantId && o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var before = new { order.InvoiceSnoozeUntil, order.InvoiceSnoozeReason };
        order.InvoiceSnoozeUntil = request.Until;
        order.InvoiceSnoozeReason = request.Until is null ? null : request.Reason?.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("TransportOrder", order.Id.ToString(),
            request.Until is null ? "InvoiceSnoozeCleared" : "InvoiceSnoozed", before,
            new { order.InvoiceSnoozeUntil, order.InvoiceSnoozeReason }, cancellationToken);
        return NoContent();
    }

    /// <summary>Next expected invoice number for an entity + period (informative, never claims).</summary>
    [HttpGet("next-number")]
    [RequirePermission(PermissionCodes.InvoicesView, PermissionCodes.InvoicesCreate)]
    public async Task<ActionResult<InvoiceNumberPreviewDto>> NextNumber(
        [FromQuery] Guid? legalEntityId, [FromQuery] int? year, [FromQuery] int? month, CancellationToken cancellationToken)
    {
        var preview = await _service.PreviewNextNumberAsync(legalEntityId, year, month, cancellationToken);
        return preview is null
            ? NotFound(new { message = "Er is geen actieve facturerende entiteit geconfigureerd." })
            : Ok(preview);
    }

    /// <summary>Manual number correction (Draft only). Requires a reason; fully audited.</summary>
    [HttpPost("{id:guid}/number-override")]
    [RequirePermission(PermissionCodes.InvoicesOverrideNumber)]
    public async Task<ActionResult<InvoiceDetailDto>> OverrideNumber(
        Guid id, OverrideInvoiceNumberRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.OverrideNumberAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    /// <summary>
    /// Fills only the MISSING ledger snapshots of a Sent/Paid invoice from the current mapping
    /// (§7.4 remediation) — never rewrites already-frozen values. Requires accounting.manage.
    /// </summary>
    [HttpPost("{id:guid}/complete-ledger-snapshots")]
    [RequirePermission(PermissionCodes.AccountingManage)]
    public async Task<ActionResult<InvoiceDetailDto>> CompleteLedgerSnapshots(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.CompleteLedgerSnapshotsAsync(id, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpPost]
    [RequirePermission(PermissionCodes.InvoicesCreate)]
    public async Task<ActionResult<InvoiceDetailDto>> Create(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCodes.InvoicesEdit)]
    public async Task<ActionResult<InvoiceDetailDto>> Update(
        Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAsync(id, request, cancellationToken);
        return Handle(result, created: false);
    }

    /// <summary>Creates a Draft credit note for a Sent/Paid invoice (positive amounts, linked to the original).</summary>
    [HttpPost("{id:guid}/credit-note")]
    [RequirePermission(PermissionCodes.InvoicesCreate)]
    public async Task<ActionResult<InvoiceDetailDto>> CreateCreditNote(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.CreateCreditNoteAsync(id, cancellationToken);
        return Handle(result, created: true);
    }

    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionCodes.InvoicesChangeStatus)]
    public async Task<ActionResult<InvoiceDetailDto>> ChangeStatus(
        Guid id, ChangeInvoiceStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ChangeStatusAsync(id, request.Status, cancellationToken);
        return Handle(result, created: false);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionCodes.InvoicesDelete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.DeleteAsync(id, cancellationToken);
        return result.Outcome switch
        {
            InvoiceOperationOutcome.Success => NoContent(),
            InvoiceOperationOutcome.NotFound => NotFound(),
            InvoiceOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
            _ => Conflict(),
        };
    }

    private ActionResult<InvoiceDetailDto> Handle(InvoiceOperationResult result, bool created) => result.Outcome switch
    {
        InvoiceOperationOutcome.Success when created =>
            CreatedAtAction(nameof(GetById), new { id = result.Invoice!.Id }, result.Invoice),
        InvoiceOperationOutcome.Success => Ok(result.Invoice),
        InvoiceOperationOutcome.NotFound => NotFound(),
        InvoiceOperationOutcome.InvalidReference => BadRequest(new { message = result.Error }),
        InvoiceOperationOutcome.InvalidState => BadRequest(new { message = result.Error }),
        InvoiceOperationOutcome.ValidationFailed => BadRequest(new { message = result.Error }),
        _ => Conflict(),
    };
}
