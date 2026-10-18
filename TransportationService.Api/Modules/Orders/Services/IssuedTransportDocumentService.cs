using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>
/// Contract 4.3. <c>Kind</c>: Cmr | DeliveryNote | WorkOrder. M9: <c>IsActive</c> = not replaced;
/// a replaced document names its successor ("Vervangen door CMR-…") and the reason, a replacement
/// names the document it replaced.
/// </summary>
public record IssuedTransportDocumentDto(
    Guid Id, Guid TransportOrderId, IssuedTransportDocumentKind Kind, string DocumentNumber,
    string? ExternalNumber, DateTime IssuedAt, string? IssuedByName,
    bool IsActive = true,
    Guid? ReplacedByDocumentId = null,
    string? ReplacedByDocumentNumber = null,
    DateTime? ReplacedAt = null,
    string? ReplacementReason = null,
    Guid? ReplacesDocumentId = null,
    string? ReplacesDocumentNumber = null);

/// <summary>
/// <c>RequestId</c> is the client's idempotency key: the same id (double click, retry after a
/// timeout) returns the SAME record and never claims a second number. <c>Kind</c> is a string so
/// an unknown value is answered with a Dutch message instead of a binder error.
/// M9: while the order has an ACTIVE document of the kind, a new one is only issued as an explicit
/// replacement — <c>ReplacesDocumentId</c> = that active document, with a <c>ReplacementReason</c>.
/// </summary>
public record IssueTransportDocumentRequest(
    string? Kind, Guid RequestId, string? ExternalNumber = null,
    Guid? ReplacesDocumentId = null, string? ReplacementReason = null);

public interface IIssuedTransportDocumentService
{
    /// <summary>Oldest first. Null = unknown order.</summary>
    Task<IReadOnlyList<IssuedTransportDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Null = unknown order.</summary>
    Task<IssuedTransportDocumentDto?> IssueAsync(Guid orderId, IssueTransportDocumentRequest request, CancellationToken cancellationToken);

    /// <summary>MD-B1 follow-up: the order's ACTIVE (not replaced) issued document of a kind ("cmr", "delivery-note", "work-order"), if any.</summary>
    Task<Guid?> FindActiveAsync(Guid orderId, string kind, CancellationToken cancellationToken);

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
        var documentId = Guid.NewGuid();

        // MD-B1: the content is frozen NOW, in the same save as the number claim — every later
        // download renders exactly this, whatever happens to customer, carrier, vehicle or unit.
        var content = await _documents.BuildCurrentSnapshotAsync(orderId, kind.ToString(), cancellationToken)
            ?? throw new DomainValidationException("orderId", "De opdracht kon niet worden geladen om het document uit te geven.");

        // M9: one ACTIVE document per order and kind — another one only as an explicit replacement.
        var replaced = await ClaimReplacementAsync(orderId, kind, documentId, request, now, cancellationToken);
        if (replaced is { Winner: { } replayed })
        {
            return replayed;
        }

        var document = new IssuedTransportDocument
        {
            Id = documentId,
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
            SnapshotJson = FrozenTransportDocumentJson.Serialize(owner?.DossierNumber, content),
            SnapshotVersion = FrozenTransportDocumentJson.CurrentVersion,
            SnapshotFrozenAt = now,
        };

        try
        {
            if (await SaveWithNumberAsync(document, kind, now, request.RequestId, orderId, cancellationToken) is { } winner)
            {
                await ReleaseReplacementAsync(documentId);
                return winner;
            }
        }
        catch
        {
            // The new document never landed: the replaced one is active again.
            await ReleaseReplacementAsync(documentId);
            throw;
        }

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Issued", null,
            new
            {
                document.TransportOrderId, document.DossierId, Kind = document.Kind.ToString(), document.DocumentNumber, document.ExternalNumber,
                ReplacesDocumentNumber = replaced?.DocumentNumber,
                ReplacementReason = replaced is null ? null : request.ReplacementReason?.Trim(),
            },
            cancellationToken);
        if (replaced is not null)
        {
            await _auditService.RecordAsync(EntityType, replaced.DocumentId.ToString(), "Replaced", null,
                new { replaced.DocumentNumber, ReplacedByDocumentNumber = document.DocumentNumber, Reason = request.ReplacementReason?.Trim() },
                cancellationToken);
        }

        return (await MapAsync([document], cancellationToken))[0];
    }

    private sealed record ReplacementClaim(Guid DocumentId, string DocumentNumber, IssuedTransportDocumentDto? Winner = null);

    /// <summary>
    /// M9: with an active document of this kind on the order, the request must name it as the one it
    /// replaces and give a reason. The claim is ONE conditional update ("still active?"), so two
    /// replacements of the same document can never both succeed; the loser of an identical-request
    /// race gets the winner's record. Null = nothing active, nothing to replace.
    /// </summary>
    private async Task<ReplacementClaim?> ClaimReplacementAsync(
        Guid orderId, IssuedTransportDocumentKind kind, Guid newDocumentId, IssueTransportDocumentRequest request, DateTime now,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var active = await _dbContext.IssuedTransportDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.TransportOrderId == orderId && d.Kind == kind && d.ReplacedByDocumentId == null)
            .OrderByDescending(d => d.IssuedAt)
            .Select(d => new { d.Id, d.DocumentNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (active is null)
        {
            if (request.ReplacesDocumentId is not null)
            {
                throw new DomainValidationException("replacesDocumentId",
                    "Het te vervangen document is niet meer actief. Vernieuw de lijst en probeer opnieuw.");
            }

            return null;
        }

        if (request.ReplacesDocumentId is null)
        {
            throw new DomainValidationException("replacesDocumentId",
                $"Er bestaat al een actief document {active.DocumentNumber} voor deze opdracht. Maak een nieuw document aan ter vervanging, met een reden.");
        }

        if (request.ReplacesDocumentId != active.Id)
        {
            throw new DomainValidationException("replacesDocumentId",
                $"Het te vervangen document is niet (meer) het actieve document; het actieve document is {active.DocumentNumber}.");
        }

        var reason = request.ReplacementReason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new DomainValidationException("replacementReason", "Geef een reden op waarom het document vervangen wordt.");
        }

        if (reason.Length > IssuedTransportDocument.ReplacementReasonMaxLength)
        {
            throw new DomainValidationException("replacementReason",
                $"De reden mag maximaal {IssuedTransportDocument.ReplacementReasonMaxLength} tekens lang zijn.");
        }

        var userId = _currentUser?.CurrentUserId;
        var claimed = await _dbContext.IssuedTransportDocuments
            .Where(d => d.TenantId == tenantId && d.Id == active.Id && d.ReplacedByDocumentId == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.ReplacedByDocumentId, newDocumentId)
                .SetProperty(d => d.ReplacedAt, now)
                .SetProperty(d => d.ReplacementReason, reason)
                .SetProperty(d => d.UpdatedAt, now)
                .SetProperty(d => d.UpdatedByUserId, userId), cancellationToken);
        if (claimed == 0)
        {
            // Someone replaced it a moment ago — maybe this very request (a double click).
            if (await FindByRequestAsync(request.RequestId, cancellationToken) is { } winner)
            {
                return new ReplacementClaim(active.Id, active.DocumentNumber, await ReturnExistingAsync(winner, orderId, cancellationToken));
            }

            throw new DomainValidationException("replacesDocumentId",
                $"Document {active.DocumentNumber} is intussen al vervangen. Vernieuw de lijst en probeer opnieuw.");
        }

        return new ReplacementClaim(active.Id, active.DocumentNumber);
    }

    /// <summary>Undo a replacement claim whose new document never landed (no-op without a claim).</summary>
    private Task<int> ReleaseReplacementAsync(Guid newDocumentId) =>
        _dbContext.IssuedTransportDocuments
            .Where(d => d.TenantId == _tenantContext.TenantId && d.ReplacedByDocumentId == newDocumentId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.ReplacedByDocumentId, (Guid?)null)
                .SetProperty(d => d.ReplacedAt, (DateTime?)null)
                .SetProperty(d => d.ReplacementReason, (string?)null), CancellationToken.None);

    /// <summary>
    /// Claims the next number and saves. Returns the WINNER's record when an identical request won
    /// the race (the caller then returns that one), null when <paramref name="document"/> was saved.
    /// </summary>
    private async Task<IssuedTransportDocumentDto?> SaveWithNumberAsync(
        IssuedTransportDocument document, IssuedTransportDocumentKind kind, DateTime now, Guid requestId, Guid orderId,
        CancellationToken cancellationToken)
    {
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
                if (await FindByRequestAsync(requestId, cancellationToken) is { } winner)
                {
                    return await ReturnExistingAsync(winner, orderId, cancellationToken);
                }

                // (2) Two first claims created the same sequence row, or (3) the number exists.
                skipTakenNumbers = true;
            }
        }

        return null;
    }

    public async Task<Guid?> FindActiveAsync(Guid orderId, string kind, CancellationToken cancellationToken)
    {
        var issuedKind = kind.ToLowerInvariant() switch
        {
            "cmr" => IssuedTransportDocumentKind.Cmr,
            "work-order" or "workorder" => IssuedTransportDocumentKind.WorkOrder,
            _ => IssuedTransportDocumentKind.DeliveryNote,
        };
        var tenantId = _tenantContext.TenantId;
        return await _dbContext.IssuedTransportDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.TransportOrderId == orderId && d.Kind == issuedKind && d.ReplacedByDocumentId == null)
            .OrderByDescending(d => d.IssuedAt)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
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

        if (document.SnapshotJson is null)
        {
            // A pre-MD-B1 document the startup backfill has not reached (e.g. issued by an older
            // instance during a rolling deploy): frozen once now, never again — the conditional
            // update keeps a concurrent first freeze from being overwritten.
            if (!await IssuedTransportDocumentSnapshotBackfill.FreezeAsync(
                    _dbContext, document, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
            {
                return null;
            }

            await _dbContext.IssuedTransportDocuments
                .Where(d => d.TenantId == tenantId && d.Id == id && d.SnapshotJson == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(d => d.SnapshotJson, document.SnapshotJson)
                    .SetProperty(d => d.SnapshotVersion, document.SnapshotVersion)
                    .SetProperty(d => d.SnapshotFrozenAt, document.SnapshotFrozenAt)
                    .SetProperty(d => d.SnapshotIsBackfilled, true), cancellationToken);
            document = await _dbContext.IssuedTransportDocuments.AsNoTracking()
                .FirstAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken);
        }

        // MD-B1: an issued document renders its FROZEN content only — never the live order,
        // customer, legal entity, vehicle, driver or unit master.
        if (!FrozenTransportDocumentJson.TryDeserialize(document.SnapshotJson, out var frozen))
        {
            return null;
        }

        // M9: a replaced document still renders (it is history), marked with its successor.
        var replacedByNumber = document.ReplacedByDocumentId is { } successorId
            ? await _dbContext.IssuedTransportDocuments.IgnoreQueryFilters().AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.Id == successorId)
                .Select(d => d.DocumentNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var snapshot = frozen.Document with
        {
            Reference = new TransportDocumentReference(document.DocumentNumber, frozen.DossierNumber, document.ExternalNumber, replacedByNumber),
        };
        return (TransportDocumentRenderer.RenderIssued(snapshot, document.Id, document.IssuedAt), $"{document.DocumentNumber}.pdf", snapshot);
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

        // M9: both directions of a replacement, in one query for the whole list.
        var ids = documents.Select(d => d.Id).ToList();
        var successorIds = documents.Where(d => d.ReplacedByDocumentId is not null).Select(d => d.ReplacedByDocumentId!.Value).ToList();
        var related = await _dbContext.IssuedTransportDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == _tenantContext.TenantId
                        && (successorIds.Contains(d.Id) || (d.ReplacedByDocumentId != null && ids.Contains(d.ReplacedByDocumentId.Value))))
            .Select(d => new { d.Id, d.DocumentNumber, d.ReplacedByDocumentId })
            .ToListAsync(cancellationToken);
        var numberById = related.ToDictionary(d => d.Id, d => d.DocumentNumber);
        var predecessorBySuccessor = related.Where(d => d.ReplacedByDocumentId is not null)
            .GroupBy(d => d.ReplacedByDocumentId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        return documents
            .Select(d =>
            {
                var predecessor = predecessorBySuccessor.GetValueOrDefault(d.Id);
                return new IssuedTransportDocumentDto(
                    d.Id, d.TransportOrderId, d.Kind, d.DocumentNumber, d.ExternalNumber, d.IssuedAt,
                    d.CreatedByUserId is { } userId ? names.GetValueOrDefault(userId) : null,
                    IsActive: d.ReplacedByDocumentId is null,
                    ReplacedByDocumentId: d.ReplacedByDocumentId,
                    ReplacedByDocumentNumber: d.ReplacedByDocumentId is { } successor ? numberById.GetValueOrDefault(successor) : null,
                    ReplacedAt: d.ReplacedAt,
                    ReplacementReason: d.ReplacementReason,
                    ReplacesDocumentId: predecessor?.Id,
                    ReplacesDocumentNumber: predecessor?.DocumentNumber);
            })
            .ToList();
    }
}
