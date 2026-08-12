using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Orders.Services;

namespace TransportationService.Api.Modules.Orders.Controllers;

/// <summary>
/// Wave 9: delivery-note/CMR generation — per order and as one merged batch per trip
/// ("print everything for this trip", in route order).
/// </summary>
[ApiController]
public class TransportDocumentsController : ControllerBase
{
    private readonly ITransportDocumentService _service;

    public TransportDocumentsController(ITransportDocumentService service)
    {
        _service = service;
    }

    [HttpGet("api/orders/{id:guid}/documents/{kind}")]
    [RequirePermission(PermissionCodes.OrdersView, PermissionCodes.OrdersManage)]
    public async Task<IActionResult> ForOrder(Guid id, string kind, CancellationToken cancellationToken)
    {
        if (!IsKnownKind(kind))
        {
            return BadRequest();
        }

        var result = await _service.RenderAsync(id, kind, cancellationToken);
        return result is null ? NotFound() : File(result.Value.Content, "application/pdf", result.Value.FileName);
    }

    [HttpGet("api/trips/{id:guid}/documents/{kind}")]
    [RequirePermission(PermissionCodes.PlanningView)]
    public async Task<IActionResult> ForTrip(Guid id, string kind, CancellationToken cancellationToken)
    {
        if (!IsKnownKind(kind))
        {
            return BadRequest();
        }

        var result = await _service.RenderTripBatchAsync(id, kind, cancellationToken);
        return result is null ? NotFound() : File(result.Value.Content, "application/pdf", result.Value.FileName);
    }

    private static bool IsKnownKind(string kind) =>
        string.Equals(kind, "cmr", StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "delivery-note", StringComparison.OrdinalIgnoreCase);
}
