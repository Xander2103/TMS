using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Services;

namespace TransportationService.Api.Modules.Peppol.Controllers;

/// <summary>Outbound transmissions (Uitgaand tab) + incoming review queue (Inkomend tab).</summary>
[ApiController]
[Route("api/peppol")]
public class PeppolTransmissionsController : ControllerBase
{
    private readonly IPeppolTransmissionService _transmissions;
    private readonly IPeppolIncomingService _incoming;

    public PeppolTransmissionsController(IPeppolTransmissionService transmissions, IPeppolIncomingService incoming)
    {
        _transmissions = transmissions;
        _incoming = incoming;
    }

    [HttpGet("transmissions")]
    [RequirePermission(PermissionCodes.PeppolView)]
    public async Task<ActionResult<PagedResult<PeppolTransmissionListItemDto>>> Search(
        [FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
        => Ok(await _transmissions.SearchAsync(status, search, PageRequest.Of(page, pageSize), cancellationToken));

    [HttpPost("transmissions/{id:guid}/retry")]
    [RequirePermission(PermissionCodes.PeppolRetry)]
    public async Task<ActionResult<PeppolTransmissionDto>> Retry(Guid id, CancellationToken cancellationToken)
    {
        var result = await _transmissions.RetryAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("transmissions/{id:guid}/cancel")]
    [RequirePermission(PermissionCodes.PeppolSend)]
    public async Task<ActionResult<PeppolTransmissionDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _transmissions.CancelAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("incoming")]
    [RequirePermission(PermissionCodes.PeppolViewIncoming)]
    public async Task<ActionResult<PagedResult<PeppolIncomingDocumentDto>>> SearchIncoming(
        [FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
        => Ok(await _incoming.SearchAsync(status, PageRequest.Of(page, pageSize), cancellationToken));

    [HttpGet("incoming/{id:guid}")]
    [RequirePermission(PermissionCodes.PeppolViewIncoming)]
    public async Task<ActionResult<PeppolIncomingDocumentDto>> GetIncoming(Guid id, CancellationToken cancellationToken)
    {
        var document = await _incoming.GetByIdAsync(id, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost("incoming/{id:guid}/review")]
    [RequirePermission(PermissionCodes.PeppolViewIncoming)]
    public async Task<ActionResult<PeppolIncomingDocumentDto>> Review(
        Guid id, PeppolIncomingDecisionRequest request, CancellationToken cancellationToken)
    {
        var document = await _incoming.MarkReviewedAsync(id, request.Note, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpPost("incoming/{id:guid}/reject")]
    [RequirePermission(PermissionCodes.PeppolViewIncoming)]
    public async Task<ActionResult<PeppolIncomingDocumentDto>> Reject(
        Guid id, PeppolIncomingDecisionRequest request, CancellationToken cancellationToken)
    {
        var document = await _incoming.RejectAsync(id, request.Note, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }
}
