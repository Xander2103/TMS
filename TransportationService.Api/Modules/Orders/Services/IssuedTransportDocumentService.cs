using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>Contract 4.3. <c>Kind</c>: Cmr | DeliveryNote | WorkOrder.</summary>
public record IssuedTransportDocumentDto(
    Guid Id, Guid TransportOrderId, IssuedTransportDocumentKind Kind, string DocumentNumber,
    string? ExternalNumber, DateTime IssuedAt, string? IssuedByName);

/// <summary>
/// <c>RequestId</c> is the client's idempotency key: the same id (double click, retry after a
/// timeout) returns the SAME record and never claims a second number. <c>Kind</c> is a string so
/// an unknown value is answered with a Dutch message instead of a binder error.
/// </summary>
public record IssueTransportDocumentRequest(string? Kind, Guid RequestId, string? ExternalNumber = null);

public interface IIssuedTransportDocumentService
{
    /// <summary>Oldest first. Null = unknown order.</summary>
    Task<IReadOnlyList<IssuedTransportDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Null = unknown order.</summary>
    Task<IssuedTransportDocumentDto?> IssueAsync(Guid orderId, IssueTransportDocumentRequest request, CancellationToken cancellationToken);

    /// <summary>The issued document through the existing renderer, with its reference block. Null = unknown document.</summary>
    Task<(byte[] Content, string FileName, TransportDocumentSnapshot Snapshot)?> RenderPdfAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// D6 (master sprint 2026-09-21): issues consignment notes, delivery notes and work orders with a
/// unique own number — <c>CMR-2026-00001</c>, <c>LB-2026-00001</c>, <c>WB-2026-00001</c>, per
/// tenant + kind + year.
/// <para>
/// Numbering follows the <c>InvoiceNumberService</c> pattern (a sequence ROW per tenant/kind/year
/// whose <c>NextValue</c> is a concurrency token) rather than a <c>TenantSettings</c> counter: a
/// settings counter is one value per tenant and cannot start a fresh series per kind and year
/// without a column per series. Two concurrent claims conflict at SaveChanges and the loser
/// re-claims; the unique index on (TenantId, DocumentNumber) is the final arbiter — a collision
/// skips forward to the first free number. An issued number is never changed and no sequence is
/// ever reset. The unique index on (TenantId, RequestId) settles two identical requests arriving
/// at the same time: the loser reloads and returns the winner's record.
/// </para>
/// </summary>
public class IssuedTransportDocumentService : IIssuedTransportDocumentService
{
    public const int ExternalNumberMaxLength = 60;
    private const string EntityType = "IssuedTransportDocument";
    private const int MaxAttempts = 5;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ITransportDocumentService _documents;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserContext? _currentUser;

    public IssuedTransportDocumentService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ITransportDocumentService documents, TimeProvider timeProvider, ICurrentUserContext? currentUser = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _documents = documents;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    /// <summary>CMR-2026-00001 · LB-2026-00001 (leveringsbon) · WB-2026-00001 (werkbon).</summary>
    public static string Compose(IssuedTransportDocumentKind kind, int year, int sequenceValue)
    {
        var prefix = kind switch
        {
            IssuedTransportDocumentKind.Cmr => "CMR",
            IssuedTransportDocumentKind.WorkOrder => "WB",
            _ => "LB",
        };
        return $"{prefix}-{year:D4}-{sequenceValue:D5}";
    }

    public async Task<IReadOnlyList<IssuedTransportDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (!await _dbContext.TransportOrders.AnyAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken))
        {
            return null;
        }

        var documents = await _dbContext.IssuedTransportDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.TransportOrderId == orderId)
            .OrderBy(d => d.IssuedAt).ThenBy(d => d.DocumentNumber)
            .ToListAsync(cancellationToken);
        return await MapAsync(documents, cancellationToken);
    }

    public async Task<IssuedTransportDocumentDto?> IssueAsync(
        Guid orderId, IssueTransportDocumentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Id == orderId)
            .Select(o => new
            {
                o.Id, o.CraneJobKind,
                HasGoodsStop = o.Stops.Any(s => !s.IsDeleted && (s.StopType == StopType.Loading || s.StopType == StopType.Unloading)),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (request.RequestId == Guid.Empty)
        {
            throw new DomainValidationException("requestId", "Een aanvraag-id is verplicht om een document uit te geven.");
        }

        if (!EnumParsing.TryParseDefined<IssuedTransportDocumentKind>(request.Kind, out var kind))
        {
            throw new DomainValidationException("kind", "Onbekende documentsoort. Toegestaan: Cmr, DeliveryNote of WorkOrder.");
        }

        // Same request = same record. Checked BEFORE the kind rules so a retry of a request that
        // succeeded keeps succeeding, whatever happened to the order in between.
        if (await FindByRequestAsync(request.RequestId, cancellationToken) is { } existing)
        {
            return await ReturnExistingAsync(existing, orderId, cancellationToken);
        }

        RequireKindAllowed(kind, order.CraneJobKind, order.HasGoodsStop);

        var externalNumber = string.IsNullOrWhiteSpace(request.ExternalNumber) ? null : request.ExternalNumber.Trim();
        if (externalNumber is { Length: > ExternalNumberMaxLength })
        {
            throw new DomainValidationException("externalNumber", $"Het externe nummer mag maximaal {ExternalNumberMaxLength} tekens lang zijn.");
        }

        var owner = await OwningDossierResolver.ResolveAsync(_dbContext, tenantId, orderId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var document = new IssuedTransportDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransportOrderId = orderId,
            DossierId = owner?.Id,
            Kind = kind,
            // Stored exactly as entered; our own number lives next to it and never replaces it.
            ExternalNumber = externalNumber,
            RequestId = request.RequestId,
            IssuedAt = now,
            // The issuer IS the creator (the interceptor stamps the same user inside a request).
            CreatedByUserId = _currentUser?.CurrentUserId,
        };

        var skipTakenNumbers = false;
        for (var attempt = 0; ; attempt++)
        {
            var sequence = await GetOrAddSequenceAsync(kind, now.Year, cancellationToken);
            if (skipTakenNumbers)
            {
                // A number collided with an existing document (a sequence row that fell behind):
                // move forward to the first free number — never backwards, never a reuse.
                while (await NumberExistsAsync(Compose(kind, now.Year, sequence.NextValue), cancellationToken))
                {
                    sequence.NextValue++;
                }
            }

            document.DocumentNumber = Compose(kind, now.Year, sequence.NextValue);
            sequence.NextValue++;
            if (_dbContext.Entry(document).State == EntityState.Detached)
            {
                _dbContext.IssuedTransportDocuments.Add(document);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // Another request claimed this value first: reload the counter and re-claim.
                await _dbContext.Entry(sequence).ReloadAsync(cancellationToken);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // A unique index spoke. Put our staged rows aside first, then find out which one.
                _dbContext.Entry(document).State = EntityState.Detached;
                var sequenceEntry = _dbContext.Entry(sequence);
                if (sequenceEntry.State == EntityState.Added)
                {
                    sequenceEntry.State = EntityState.Detached;
                }
                else
                {
                    await sequenceEntry.ReloadAsync(cancellationToken);
                }

                // (1) The identical request won the race a moment ago → its record is THE record.
                if (await FindByRequestAsync(request.RequestId, cancellationToken) is { } winner)
                {
                    return await ReturnExistingAsync(winner, orderId, cancellationToken);
                }

                // (2) Two first claims created the same sequence row, or (3) the number exists.
                skipTakenNumbers = true;
            }
        }

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Issued", null,
            new { document.TransportOrderId, document.DossierId, Kind = document.Kind.ToString(), document.DocumentNumber, document.ExternalNumber },
            cancellationToken);
        return (await MapAsync([document], cancellationToken))[0];
    }

    public async Task<(byte[] Content, string FileName, TransportDocumentSnapshot Snapshot)?> RenderPdfAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var document = await _dbContext.IssuedTransportDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        // The dossier number AS ISSUED; an order that sat in no dossier then shows its current one.
        var dossierNumber = document.DossierId is { } dossierId
            ? await _dbContext.TransportDossiers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.Id == dossierId)
                .Select(d => d.DossierNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : (await OwningDossierResolver.ResolveAsync(_dbContext, tenantId, document.TransportOrderId, cancellationToken))?.DossierNumber;

        var rendered = await _documents.RenderIssuedAsync(
            document.TransportOrderId, document.Kind.ToString(),
            new TransportDocumentReference(document.DocumentNumber, dossierNumber, document.ExternalNumber),
            cancellationToken);
        return rendered is null
            ? null
            : (rendered.Value.Content, $"{document.DocumentNumber}.pdf", rendered.Value.Snapshot);
    }

    /// <summary>
    /// A document kind must make sense for the order: on-site lifting work is documented with a
    /// work order (werkbon) and NEVER gets a CMR or delivery note forced onto it; a CMR or delivery
    /// note needs a goods transport (an order with a loading or unloading stop).
    /// </summary>
    private static void RequireKindAllowed(IssuedTransportDocumentKind kind, CraneJobKind craneJobKind, bool hasGoodsStop)
    {
        var onSiteWork = CraneJobRules.IsOnSiteWork(craneJobKind);
        if (kind == IssuedTransportDocumentKind.WorkOrder)
        {
            if (!onSiteWork)
            {
                throw new DomainValidationException("kind", "Een werkbon kan alleen worden uitgegeven voor kraanwerk ter plaatse.");
            }

            return;
        }

        if (onSiteWork)
        {
            throw new DomainValidationException("kind",
                "Voor kraanwerk ter plaatse wordt een werkbon uitgegeven, geen CMR of leveringsbon.");
        }

        if (!hasGoodsStop)
        {
            throw new DomainValidationException("kind",
                "Een CMR of leveringsbon kan alleen worden uitgegeven voor een goederentransport met een laad- of losstop.");
        }
    }

    private async Task<IssuedTransportDocumentDto> ReturnExistingAsync(
        IssuedTransportDocument existing, Guid orderId, CancellationToken cancellationToken)
    {
        if (existing.TransportOrderId != orderId)
        {
            // Never hand out the document of another order under this order's route.
            throw new DomainValidationException("requestId", "Deze aanvraag-id is al gebruikt voor een andere opdracht.");
        }

        return (await MapAsync([existing], cancellationToken))[0];
    }

    private Task<IssuedTransportDocument?> FindByRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        // Soft-deleted rows still occupy the unique index, so they are part of the lookup; the
        // explicit tenant predicate replaces the global filter this bypasses.
        _dbContext.IssuedTransportDocuments.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == _tenantContext.TenantId && d.RequestId == requestId, cancellationToken);

    private Task<bool> NumberExistsAsync(string documentNumber, CancellationToken cancellationToken) =>
        _dbContext.IssuedTransportDocuments.IgnoreQueryFilters()
            .AnyAsync(d => d.TenantId == _tenantContext.TenantId && d.DocumentNumber == documentNumber, cancellationToken);

    private async Task<TransportDocumentSequence> GetOrAddSequenceAsync(
        IssuedTransportDocumentKind kind, int year, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var sequence = _dbContext.TransportDocumentSequences.Local.FirstOrDefault(
                s => s.TenantId == tenantId && s.Kind == kind && s.Year == year && !s.IsDeleted)
            ?? await _dbContext.TransportDocumentSequences.FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.Kind == kind && s.Year == year, cancellationToken);
        if (sequence is null)
        {
            sequence = new TransportDocumentSequence
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Kind = kind, Year = year, NextValue = 1,
            };
            _dbContext.TransportDocumentSequences.Add(sequence);
        }

        return sequence;
    }

    /// <summary>Issuer names in ONE query for the whole list.</summary>
    private async Task<List<IssuedTransportDocumentDto>> MapAsync(
        IReadOnlyList<IssuedTransportDocument> documents, CancellationToken cancellationToken)
    {
        var userIds = documents.Where(d => d.CreatedByUserId is not null).Select(d => d.CreatedByUserId!.Value).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId && userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name.Trim(), cancellationToken);

        return documents
            .Select(d => new IssuedTransportDocumentDto(
                d.Id, d.TransportOrderId, d.Kind, d.DocumentNumber, d.ExternalNumber, d.IssuedAt,
                d.CreatedByUserId is { } userId ? names.GetValueOrDefault(userId) : null))
            .ToList();
    }
}
