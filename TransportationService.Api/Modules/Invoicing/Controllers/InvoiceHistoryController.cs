using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Invoicing.Services;

namespace TransportationService.Api.Modules.Invoicing.Controllers;

/// <summary>
/// ACC-M7: the invoice's financial history ("Historiek"). It is part of the invoice, so it follows
/// the invoice read right (<c>invoices.view</c>); the raw tenant-wide audit log keeps its own
/// <c>audit_logs.view</c>. An invoice of another tenant is a 404.
/// </summary>
[ApiController]
[Route("api/invoices/{invoiceId:guid}/history")]
public class InvoiceHistoryController : ControllerBase
{
    private readonly IInvoiceHistoryService _service;

    public InvoiceHistoryController(IInvoiceHistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequirePermission(PermissionCodes.InvoicesView)]
    public async Task<ActionResult<IReadOnlyList<InvoiceHistoryEventDto>>> Get(Guid invoiceId, CancellationToken cancellationToken)
    {
        var history = await _service.GetAsync(invoiceId, cancellationToken);
        return history is null ? NotFound() : Ok(history);
    }
}
