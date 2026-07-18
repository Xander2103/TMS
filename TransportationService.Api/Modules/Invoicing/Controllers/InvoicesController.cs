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

    public InvoicesController(IInvoiceService service)
    {
        _service = service;
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
