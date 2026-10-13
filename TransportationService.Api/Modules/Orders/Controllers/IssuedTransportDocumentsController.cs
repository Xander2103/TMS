using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>
/// D6 (contract 4.3): transport documents ISSUED with a unique own number. Reading follows the
/// order (<c>orders.view</c>), issuing is an order edit. The number-less streamed endpoints of
/// <see cref="TransportDocumentsController"/> keep working unchanged next to these.
/// </summary>
[ApiController]
public class IssuedTransportDocumentsController : ControllerBase
{
    private readonly IIssuedTransportDocumentService _service;

    public IssuedTransportDocumentsController(IIssuedTransportDocumentService service)
    {
        _service = service;
    }

    [HttpGet("api/transport-orders/{orderId:guid}/issued-documents")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IReadOnlyList<IssuedTransportDocumentDto>>> List(Guid orderId, CancellationToken cancellationToken)
    {
        var documents = await _service.ListAsync(orderId, cancellationToken);
        return documents is null ? NotFound() : Ok(documents);
    }

    /// <summary>The same <c>requestId</c> returns the same record — never a second number.</summary>
    [HttpPost("api/transport-orders/{orderId:guid}/issued-documents")]
    [RequirePermission(PermissionCodes.OrdersEdit, PermissionCodes.OrdersManage)]
    public async Task<ActionResult<IssuedTransportDocumentDto>> Issue(
        Guid orderId, IssueTransportDocumentRequest request, CancellationToken cancellationToken)
    {
        var issued = await _service.IssueAsync(orderId, request, cancellationToken);
        return issued is null ? NotFound() : Ok(issued);
    }

    [HttpGet("api/issued-transport-documents/{id:guid}/pdf")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.RenderPdfAsync(id, cancellationToken);
        return result is null ? NotFound() : File(result.Value.Content, "application/pdf", result.Value.FileName);
    }
}
