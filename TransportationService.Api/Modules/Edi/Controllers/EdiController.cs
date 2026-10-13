using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Persistence;
using System.Linq.Expressions;
using System.Text.Json;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Edi.Entities;
using TransportationService.Api.Modules.Edi.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Edi.Controllers;

/// <summary>
/// EDI inbox/outbox, trading partners and the development simulator. Inbound ingestion is
/// permission-gated for now; real partner endpoints would authenticate with API keys once a
/// partner integration exists. Read access (<see cref="PermissionCodes.EdiView"/>), replay
/// (<see cref="PermissionCodes.EdiRetry"/>) and test/validate (<see cref="PermissionCodes.EdiTest"/>)
/// are split from full management (<see cref="PermissionCodes.EdiManage"/>) so a wider audience
/// can monitor the inbox and rehearse mappings without being able to change partner configuration.
/// </summary>
[ApiController]
public class EdiController : ControllerBase
{
    private const string PartnerEntityType = "TradingPartner";
    private const string LocationEntityType = "EdiPartnerLocation";

    private readonly IEdiService _service;
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public EdiController(
        IEdiService service, TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        TimeProvider timeProvider)
    {
        _service = service;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
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
        bool MappingIssue, string? ResultEntityType, string? ResultEntityId, DateTime CreatedAt);

    [HttpGet("api/edi/messages")]
    [RequirePermission(PermissionCodes.EdiView, PermissionCodes.EdiManage)]
    public async Task<ActionResult<PagedResult<EdiMessageRow>>> Messages(
        [FromQuery] EdiDirection? direction, [FromQuery] EdiProcessingStatus? status,
        [FromQuery] Guid? partnerId, [FromQuery] bool? mappingIssues, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var pageRequest = PageRequest.Of(page, pageSize);
        var query = _dbContext.EdiMessages.AsNoTracking()
            .Where(m => m.TenantId == _tenantContext.TenantId);
        if (direction is { } d) query = query.Where(m => m.Direction == d);
        if (status is { } s) query = query.Where(m => m.Status == s);
        if (partnerId is { } pid) query = query.Where(m => m.TradingPartnerId == pid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(m => m.ExternalReference != null && m.ExternalReference.Contains(search));
        }

        if (mappingIssues == true)
        {
            query = query.Where(MappingIssueExpression);
        }

        var partners = _dbContext.TradingPartners.AsNoTracking().Where(p => p.TenantId == _tenantContext.TenantId);
        var totalCount = await query.CountAsync(cancellationToken);
        // Pull the raw failure fields through the query, then compute MappingIssue client-side â€”
        // keeping the derivation logic in one place (IsMappingIssue) instead of duplicating it as
        // a second EF-translatable expression inside the projection.
        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(pageRequest.Skip)
            .Take(pageRequest.PageSize)
            .Join(partners, m => m.TradingPartnerId, p => p.Id, (m, p) => new
            {
                m.Id, m.Direction, PartnerCode = p.Code, m.MessageType, m.ExternalReference, m.Status,
                m.AttemptCount, m.ProcessedAt, m.ErrorDetail, m.FailureKind, m.ValidationErrorsJson,
                m.ResultEntityType, m.ResultEntityId, m.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(r => new EdiMessageRow(
            r.Id, r.Direction, r.PartnerCode, r.MessageType, r.ExternalReference, r.Status, r.AttemptCount,
            r.ProcessedAt, r.ErrorDetail, IsMappingIssue(r.FailureKind, r.ErrorDetail, r.ValidationErrorsJson),
            r.ResultEntityType, r.ResultEntityId, r.CreatedAt)).ToList();

        return Ok(new PagedResult<EdiMessageRow>(items, totalCount, pageRequest.Page, pageRequest.PageSize));
    }

    /// <summary>
    /// The single source of truth for "is this a mapping problem": the machine-readable
    /// <see cref="EdiMessage.FailureKind"/> going forward, falling back to a text match on the
    /// exact Dutch sentences a location/customer mapping failure produces for rows recorded
    /// before that column existed. EF-translatable as-is for use inside a query (<c>.Where</c>,
    /// <c>CountAsync</c>); <see cref="IsMappingIssueCompiled"/> compiles this same expression
    /// once for client-side use once fields are already materialized â€” no second definition.
    /// </summary>
    private static readonly Expression<Func<EdiMessage, bool>> MappingIssueExpression = m =>
        m.FailureKind == EdiService.FailureKindMapping
        || (m.FailureKind == null
            && ((m.ErrorDetail != null && (m.ErrorDetail.Contains("Locatiemapping") || m.ErrorDetail.Contains("klant gekoppeld")))
                || (m.ValidationErrorsJson != null && m.ValidationErrorsJson.Contains("locatiecode"))));

    private static readonly Func<EdiMessage, bool> IsMappingIssueCompiled = MappingIssueExpression.Compile();

    /// <summary>Client-side check once FailureKind/ErrorDetail/ValidationErrorsJson are already materialized.</summary>
    private static bool IsMappingIssue(string? failureKind, string? errorDetail, string? validationErrorsJson) =>
        IsMappingIssueCompiled(new EdiMessage
        {
            FailureKind = failureKind, ErrorDetail = errorDetail, ValidationErrorsJson = validationErrorsJson,
        });

    [HttpGet("api/edi/messages/{id:guid}")]
    [RequirePermission(PermissionCodes.EdiView, PermissionCodes.EdiManage)]
    public async Task<IActionResult> MessageDetail(Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.EdiMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == _tenantContext.TenantId, cancellationToken);
        if (message is null)
        {
            return NotFound();
        }

        var partnerCode = await _dbContext.TradingPartners.AsNoTracking()
            .Where(p => p.Id == message.TradingPartnerId && p.TenantId == _tenantContext.TenantId)
            .Select(p => p.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            message.Id,
            message.Direction,
            PartnerCode = partnerCode,
            message.MessageType,
            message.ExternalReference,
            message.Status,
            message.AttemptCount,
            message.ProcessedAt,
            message.ErrorDetail,
            ValidationErrors = message.ValidationErrorsJson != null
                ? JsonSerializer.Deserialize<string[]>(message.ValidationErrorsJson)
                : null,
            MappingIssue = IsMappingIssueCompiled(message),
            message.PayloadJson,
            message.ResultEntityType,
            message.ResultEntityId,
            message.CreatedAt,
        });
    }

    [HttpPost("api/edi/messages/{id:guid}/replay")]
    [RequirePermission(PermissionCodes.EdiRetry, PermissionCodes.EdiManage)]
    public async Task<IActionResult> Replay(Guid id, CancellationToken cancellationToken)
    {
        var message = await _service.ReplayAsync(id, cancellationToken);
        return message is null ? NotFound() : Ok(new { message.Id, message.Status, message.ErrorDetail });
    }

    public record PartnerDto(
        Guid Id, string Code, string Name, Guid? CustomerId, string? CustomerName, string? ExternalCustomerIdentifier,
        string MappingProfile, bool IsActive, string? Notes, IReadOnlyList<PartnerLocationDto> Locations);

    public record PartnerLocationDto(Guid Id, string ExternalLocationCode, Guid LocationId, string LocationName);

    [HttpGet("api/edi/partners")]
    [RequirePermission(PermissionCodes.EdiView, PermissionCodes.EdiManage)]
    public async Task<ActionResult<IReadOnlyList<PartnerDto>>> Partners(CancellationToken cancellationToken)
    {
        var partners = await _dbContext.TradingPartners.AsNoTracking()
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
        var customerNames = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var locations = await _dbContext.EdiPartnerLocations.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId)
            .ToListAsync(cancellationToken);
        var locationNames = await _dbContext.Locations.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId)
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        return Ok(partners.Select(p => new PartnerDto(
            p.Id, p.Code, p.Name, p.CustomerId,
            p.CustomerId is { } cid && customerNames.TryGetValue(cid, out var cName) ? cName : null,
            p.ExternalCustomerIdentifier, p.MappingProfile, p.IsActive, p.Notes,
            locations.Where(l => l.TradingPartnerId == p.Id)
                .Select(l => new PartnerLocationDto(
                    l.Id, l.ExternalLocationCode, l.LocationId,
                    locationNames.TryGetValue(l.LocationId, out var lName) ? lName : "(onbekend)"))
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

        await EnsureCustomerBelongsToTenantAsync(request.CustomerId, cancellationToken);

        var partner = await _dbContext.TradingPartners
            .FirstOrDefaultAsync(p => p.TenantId == _tenantContext.TenantId && p.Code == request.Code.Trim(), cancellationToken);
        var isNew = partner is null;
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

        await _auditService.RecordAsync(PartnerEntityType, partner.Id.ToString(), isNew ? "Created" : "Updated", null,
            new { partner.Code, partner.Name, partner.CustomerId, partner.IsActive }, cancellationToken);

        return Ok(new { partner.Id });
    }

    public record UpdatePartnerRequest(
        string Name, Guid? CustomerId, string? ExternalCustomerIdentifier, string MappingProfile,
        bool IsActive, string? Notes);

    [HttpPut("api/edi/partners/{id:guid}")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> UpdatePartner(Guid id, UpdatePartnerRequest request, CancellationToken cancellationToken)
    {
        var partner = await _dbContext.TradingPartners
            .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId, cancellationToken);
        if (partner is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "Naam is verplicht.");
        }

        await EnsureCustomerBelongsToTenantAsync(request.CustomerId, cancellationToken);

        var oldValues = new
        {
            partner.Name, partner.CustomerId, partner.ExternalCustomerIdentifier,
            partner.MappingProfile, partner.IsActive, partner.Notes,
        };

        partner.Name = request.Name.Trim();
        partner.CustomerId = request.CustomerId;
        partner.ExternalCustomerIdentifier = string.IsNullOrWhiteSpace(request.ExternalCustomerIdentifier)
            ? null
            : request.ExternalCustomerIdentifier.Trim();
        partner.MappingProfile = string.IsNullOrWhiteSpace(request.MappingProfile)
            ? partner.MappingProfile
            : request.MappingProfile.Trim();
        partner.IsActive = request.IsActive;
        partner.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(PartnerEntityType, partner.Id.ToString(), "Updated", oldValues,
            new
            {
                partner.Name, partner.CustomerId, partner.ExternalCustomerIdentifier,
                partner.MappingProfile, partner.IsActive, partner.Notes,
            }, cancellationToken);

        return Ok(new { partner.Id });
    }

    private async Task EnsureCustomerBelongsToTenantAsync(Guid? customerId, CancellationToken cancellationToken)
    {
        if (customerId is not { } id)
        {
            return;
        }

        var belongsToTenant = await _dbContext.Customers.AsNoTracking()
            .AnyAsync(c => c.Id == id && c.TenantId == _tenantContext.TenantId, cancellationToken);
        if (!belongsToTenant)
        {
            throw new DomainValidationException("customerId", "De geselecteerde klant behoort niet tot deze tenant.");
        }
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

        // The mapped location is a client-supplied id: without this check a guessed foreign id
        // would leak another tenant's location name/address through later EDI projections.
        await _dbContext.Locations.EnsureBelongsToTenantAsync(
            request.LocationId, _tenantContext.TenantId, "locatie", cancellationToken);

        var existing = await _dbContext.EdiPartnerLocations
            .FirstOrDefaultAsync(l => l.TradingPartnerId == partnerId
                                      && l.TenantId == _tenantContext.TenantId
                                      && l.ExternalLocationCode == request.ExternalLocationCode.Trim(), cancellationToken);
        var isNew = existing is null;
        if (existing is null)
        {
            existing = new EdiPartnerLocation
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TradingPartnerId = partnerId,
                ExternalLocationCode = request.ExternalLocationCode.Trim(),
                LocationId = request.LocationId,
            };
            _dbContext.Add(existing);
        }
        else
        {
            existing.LocationId = request.LocationId;
        }



        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(LocationEntityType, existing.Id.ToString(), isNew ? "Created" : "Updated", null,
            new { existing.ExternalLocationCode, existing.LocationId }, cancellationToken);
        return NoContent();
    }

    [HttpDelete("api/edi/partners/{partnerId:guid}/locations/{locationId:guid}")]
    [RequirePermission(PermissionCodes.EdiManage)]
    public async Task<IActionResult> DeleteLocationMapping(Guid partnerId, Guid locationId, CancellationToken cancellationToken)
    {
        var mapping = await _dbContext.EdiPartnerLocations
            .FirstOrDefaultAsync(l => l.Id == locationId && l.TradingPartnerId == partnerId
                                      && l.TenantId == _tenantContext.TenantId, cancellationToken);
        if (mapping is null)
        {
            return NotFound();
        }

        _dbContext.Remove(mapping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(LocationEntityType, mapping.Id.ToString(), "Deleted",
            new { mapping.ExternalLocationCode, mapping.LocationId }, null, cancellationToken);
        return NoContent();
    }

    public record EdiStatsResponse(
        IReadOnlyDictionary<string, int> PerStatus, int Failed, int DeadLettered, int MappingIssues,
        int ProcessedLast7Days, int TotalPartners, int PartnersWithoutCustomer);

    [HttpGet("api/edi/stats")]
    [RequirePermission(PermissionCodes.EdiView, PermissionCodes.EdiManage)]
    public async Task<ActionResult<EdiStatsResponse>> Stats(CancellationToken cancellationToken)
    {
        var messages = _dbContext.EdiMessages.AsNoTracking().Where(m => m.TenantId == _tenantContext.TenantId);
        var perStatus = await messages
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status.ToString(), x => x.Count, cancellationToken);

        var sevenDaysAgo = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-7);
        var processedLast7Days = await messages
            .CountAsync(m => m.Status == EdiProcessingStatus.Processed && m.ProcessedAt >= sevenDaysAgo, cancellationToken);
        var mappingIssues = await messages.CountAsync(MappingIssueExpression, cancellationToken);

        var partners = _dbContext.TradingPartners.AsNoTracking().Where(p => p.TenantId == _tenantContext.TenantId);
        var totalPartners = await partners.CountAsync(cancellationToken);
        var partnersWithoutCustomer = await partners.CountAsync(p => p.CustomerId == null, cancellationToken);

        return Ok(new EdiStatsResponse(
            perStatus,
            perStatus.GetValueOrDefault(nameof(EdiProcessingStatus.Failed)),
            perStatus.GetValueOrDefault(nameof(EdiProcessingStatus.DeadLettered)),
            mappingIssues,
            processedLast7Days,
            totalPartners,
            partnersWithoutCustomer));
    }

    public record ValidateRequest(string PartnerCode, string MessageType, string Payload);

    /// <summary>Dry-run: runs the same parse/mapping/unit pipeline as ingestion but never persists anything.</summary>
    [HttpPost("api/edi/validate")]
    [RequirePermission(PermissionCodes.EdiTest, PermissionCodes.EdiManage)]
    public async Task<IActionResult> Validate(ValidateRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.ValidateAsync(request.PartnerCode, request.MessageType, request.Payload, cancellationToken);
        return Ok(new { result.Valid, result.Errors, WouldCreate = result.WouldCreate });
    }

    public record SimulateRequest(string PartnerCode);

    /// <summary>Development simulator: builds a valid sample payload for the partner and ingests it.</summary>
    [HttpPost("api/edi/simulate")]
    [RequirePermission(PermissionCodes.EdiTest, PermissionCodes.EdiManage)]
    public async Task<IActionResult> Simulate(SimulateRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.IngestAsync(request.PartnerCode, "order", SimulatedPayload(), cancellationToken);
        return result.Outcome switch
        {
            EdiIngestOutcome.Accepted => Ok(ToRow(result.Message!)),
            EdiIngestOutcome.UnknownPartner => NotFound(new { message = result.Error }),
            _ => BadRequest(new { message = result.Error }),
        };
    }

    /// <summary>The sample "generic-json-v1" payload shape used by both the simulator and the Testen tab's prefilled textarea.</summary>
    private static string SimulatedPayload() => JsonSerializer.Serialize(new
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
