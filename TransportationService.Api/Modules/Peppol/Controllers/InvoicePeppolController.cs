using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Services;

namespace TransportationService.Api.Modules.Peppol.Controllers;

/// <summary>
/// Per-invoice Peppol readiness: validation checklist, structured preview and the rendered
/// UBL document. Sending itself (transmissions, dispatcher) is phase 13 — none of these
/// endpoints contact any provider.
/// </summary>
[ApiController]
[Route("api/invoices/{id:guid}/peppol")]
public class InvoicePeppolController : ControllerBase
{
    private readonly IPeppolInvoiceService _service;
    private readonly IPeppolTransmissionService _transmissions;

    public InvoicePeppolController(IPeppolInvoiceService service, IPeppolTransmissionService transmissions)
    {
        _service = service;
        _transmissions = transmissions;
    }

    /// <summary>Queues the invoice for Peppol sending; the dispatcher contacts the provider.</summary>
    [HttpPost("send")]
    [RequirePermission(PermissionCodes.PeppolSend)]
    public async Task<ActionResult<PeppolTransmissionDto>> Send(Guid id, CancellationToken cancellationToken)
    {
        var transmission = await _transmissions.QueueAsync(id, cancellationToken);
        return transmission is null ? NotFound() : Ok(transmission);
    }

    /// <summary>Transmission history (newest first) with per-transmission event timelines.</summary>
    [HttpGet("transmissions")]
    [RequirePermission(PermissionCodes.PeppolView, PermissionCodes.PeppolValidate)]
    public async Task<ActionResult<IReadOnlyList<PeppolTransmissionDto>>> Transmissions(
        Guid id, CancellationToken cancellationToken)
        => Ok(await _transmissions.ListForInvoiceAsync(id, cancellationToken));

    [HttpPost("validate")]
    [RequirePermission(PermissionCodes.PeppolValidate)]
    public async Task<ActionResult<PeppolInvoiceValidationResultDto>> Validate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.ValidateAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("preview")]
    [RequirePermission(PermissionCodes.PeppolValidate, PermissionCodes.PeppolView)]
    public async Task<ActionResult<PeppolInvoicePreviewDto>> Preview(Guid id, CancellationToken cancellationToken)
    {
        var preview = await _service.PreviewAsync(id, cancellationToken);
        return preview is null ? NotFound() : Ok(preview);
    }

    /// <summary>Rendered UBL XML (test/admin use); 400 with the issue list while invalid.</summary>
    [HttpGet("xml")]
    [RequirePermission(PermissionCodes.PeppolValidate)]
    public async Task<IActionResult> Xml(Guid id, CancellationToken cancellationToken)
    {
        var result = await _service.GetXmlAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        var (xml, failure) = result.Value;
        if (xml is null)
        {
            return BadRequest(failure);
        }

        return File(System.Text.Encoding.UTF8.GetBytes(xml), "application/xml", $"peppol-{id:N}.xml");
    }
}
