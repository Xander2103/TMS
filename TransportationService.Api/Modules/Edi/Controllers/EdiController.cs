using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Edi.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Edi.Controllers;

/// <summary>
/// EDI inbox/outbox, trading partners and the development simulator. Inbound ingestion is
/// permission-gated for now; real partner endpoints would authenticate with API keys once a
/// partner integration exists.
/// </summary>
[ApiController]
public class EdiController : ControllerBase
{
    private readonly IEdiService _service;
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public EdiController(IEdiService service, TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _service = service;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public record IngestRequest(string Payload);

    [HttpPost("api/edi/inbound/{partnerCode}/{messageType}")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> Ingest(
        string partnerCode, string messageType, IngestRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.IngestAsync(partnerCode, messageType, request.Payload, cancellationToken);
        return result.Outcome switch
        {
            EdiIngestOutcome.Accepted => Ok(ToRow(result.Message!)),
            EdiIngestOutcome.UnknownPartner => NotFound(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    public record EdiMessageRow(
        Guid Id, EdiDirection Direction, string PartnerCode, string MessageType, string? ExternalReference,
        EdiProcessingStatus Status, int AttemptCount, DateTime? ProcessedAt, string? ErrorDetail,
        string? ResultEntityType, string? ResultEntityId, DateTime CreatedAt);

    [HttpGet("api/edi/messages")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<ActionResult<PagedResult<EdiMessageRow>>> Messages(
        [FromQuery] EdiDirection? direction, [FromQuery] EdiProcessingStatus? status,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var pageRequest = PageRequest.Of(page, pageSize);
        var query = _dbContext.EdiMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId);
        if (direction is { } d) query = query.Where(m => m.Direction == d);
        if (status is { } s) query = query.Where(m => m.Status == s);

        var partners = _dbContext.TradingPartners.AsNoTracking().Where(p => p.TenantId == _tenantContext.TenantId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .Join(partners, m => m.TradingPartnerId, p => p.Id, (m, p) => new EdiMessageRow(
                m.Id, m.Direction, p.Code, m.MessageType, m.ExternalReference, m.Status, m.AttemptCount,
                m.ProcessedAt, m.ErrorDetail, m.ResultEntityType, m.ResultEntityId, m.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<EdiMessageRow>(items, totalCount, pageRequest.Page, pageRequest.PageSize));
    }

    [HttpGet("api/edi/messages/{id:guid}")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> MessageDetail(Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.EdiMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        return message is null ? NotFound() : Ok(message);
    }

    [HttpPost("api/edi/messages/{id:guid}/replay")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken cancellationToken)
    {
        var message = await _service.ReplayAsync(id, cancellationToken);
        return message is null ? NotFound() : Ok(new { message.Id, message.Status, message.ErrorDetail });
    }

    public record PartnerDto(
        Guid Id, string Code, string Name, Guid? CustomerId, string? ExternalCustomerIdentifier,
        string MappingProfile, bool IsActive, IReadOnlyList<PartnerLocationDto> Locations);

    public record PartnerLocationDto(Guid Id, string ExternalLocationCode, Guid LocationId);

    [HttpGet("api/edi/partners")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<ActionResult<IReadOnlyList<PartnerDto>>> Partners(CancellationToken cancellationToken)
    {
        var partners = await _dbContext.TradingPartners.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
        var locations = await _dbContext.EdiPartnerLocations.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId)
            .ToListAsync(cancellationToken);

        return Ok(partners.Select(p => new PartnerDto(
            p.Id, p.Code, p.Name, p.CustomerId, p.ExternalCustomerIdentifier, p.MappingProfile, p.IsActive,
            locations.Where(l => l.TradingPartnerId == p.Id)
                .Select(l => new PartnerLocationDto(l.Id, l.ExternalLocationCode, l.LocationId))
                .ToList())).ToList());
    }

    public record UpsertPartnerRequest(
        string Code, string Name, Guid? CustomerId, string? ExternalCustomerIdentifier, bool IsActive);

    [HttpPost("api/edi/partners")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> UpsertPartner(UpsertPartnerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Code en naam zijn verplicht." });
        }

        var partner = await _dbContext.TradingPartners
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Code == request.Code.Trim(), cancellationToken);
        if (partner is null)
        {
            partner = new TradingPartner
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                Code = request.Code.Trim(),
            };
            _dbContext.Add(partner);
        }

        partner.Name = request.Name.Trim();
        partner.CustomerId = request.CustomerId;
        partner.ExternalCustomerIdentifier = request.ExternalCustomerIdentifier;
        partner.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { partner.Id });
    }

    public record AddLocationMappingRequest(string ExternalLocationCode, Guid LocationId);

    [HttpPost("api/edi/partners/{partnerId:guid}/locations")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> AddLocationMapping(
        Guid partnerId, AddLocationMappingRequest request, CancellationToken cancellationToken)
    {
        var partnerExists = await _dbContext.TradingPartners.AsNoTracking()
            .AnyAsync(p => p.Id == partnerId && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (!partnerExists)
        {
            return NotFound();
        }

        var existing = await _dbContext.EdiPartnerLocations
            .FirstOrDefaultAsync(l => l.TradingPartnerId == partnerId
                                      && l.ExternalLocationCode == request.ExternalLocationCode.Trim(), cancellationToken);
        if (existing is null)
        {
            _dbContext.Add(new EdiPartnerLocation
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TradingPartnerId = partnerId,
                ExternalLocationCode = request.ExternalLocationCode.Trim(),
                LocationId = request.LocationId,
            });
        }
        else
        {
            existing.LocationId = request.LocationId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public record SimulateRequest(string PartnerCode);

    /// <summary>Development simulator: builds a valid sample payload for the partner and ingests it.</summary>
    [HttpPost("api/edi/simulate")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> Simulate(SimulateRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            externalOrderId = $"SIM-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            customerReference = "SIM-REF",
            goodsDescription = "Simulatie-opdracht via EDI",
            stops = new object[]
            {
                new { type = "Loading", city = "Antwerpen" },
                new { type = "Unloading", city = "Gent" },
            },
            cargoItems = new object[]
            {
                new { description = "Simulatiepallet", quantity = 4 },
            },
        });

        var result = await _service.IngestAsync(request.PartnerCode, "order", payload, cancellationToken);
        return result.Outcome switch
        {
            EdiIngestOutcome.Accepted => Ok(ToRow(result.Message!)),
            EdiIngestOutcome.UnknownPartner => NotFound(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    private static object ToRow(EdiMessage message) => new
    {
        message.Id,
        message.Direction,
        message.MessageType,
        message.ExternalReference,
        message.Status,
        message.AttemptCount,
        message.ErrorDetail,
        message.ResultEntityType,
        message.ResultEntityId,
    };
}
