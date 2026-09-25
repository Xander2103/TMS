using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

public record TransportOrderDocumentDto(
    Guid Id,
    /// <summary>D6: null for a document of the dossier as a whole (see <see cref="Scope"/>).</summary>
    Guid? TransportOrderId,
    TransportOrderDocumentType DocumentType, string? CustomTypeName,
    string Title, bool HasAttachment, string? FileName, DateOnly? IssueDate, string? Notes,
    /// <summary>H-14: whether this document is published to the customer portal (default false).</summary>
    bool CustomerVisible = false,
    Guid? DossierId = null,
    DossierDocumentScope Scope = DossierDocumentScope.Order);

public record SaveTransportOrderDocumentRequest(
    TransportOrderDocumentType DocumentType, string? CustomTypeName, string Title,
    DateOnly? IssueDate, string? Notes,
    /// <summary>
    /// H-14: publish this document to the customer portal. Tri-state on purpose — omitted/null
    /// means "leave the current publication state alone", so a caller that predates the field
    /// (or a partial metadata PUT) can never silently unpublish a document. A NEW document with
    /// no value stays internal, which is the safe default.
    /// </summary>
    bool? CustomerVisible = null);

/// <summary>D6 (contract 4.2): one document of a dossier — of the dossier as a whole, or of one of its orders.</summary>
public record DossierDocumentDto(
    Guid Id, Guid? DossierId, Guid? TransportOrderId, string? OrderNumber, DossierDocumentScope Scope,
    TransportOrderDocumentType DocumentType, string? CustomTypeName, string Title,
    string? FileName, string? ContentType, DateOnly? IssueDate, string? Notes,
    bool CustomerVisible, bool HasFile, DateTime CreatedAt, string? CreatedByName);

/// <summary>D6: <c>TransportOrderId</c> null = a document of the dossier as a whole. A new document is internal unless the uploader says otherwise.</summary>
public record CreateDossierDocumentRequest(
    Guid? TransportOrderId, TransportOrderDocumentType DocumentType, string? CustomTypeName, string Title,
    DateOnly? IssueDate, string? Notes, bool? CustomerVisible = null);

/// <summary>D6: null = to dossier level; else an order of the SAME dossier.</summary>
public record MoveOrderDocumentRequest(Guid? TargetTransportOrderId);

public interface ITransportOrderDocumentService
{
    /// <summary>The order's OWN documents only — dossier-level documents come from <see cref="ListForDossierAsync"/>.</summary>
    Task<IReadOnlyList<TransportOrderDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken);
    Task<TransportOrderDocumentDto?> CreateAsync(Guid orderId, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken);

    // The flat (by-id) operations run in ONE scope: the caller states the scope it was authorized
    // for and a row of the other scope is simply not found. The default is the ORDER scope, so an
    // order-scoped code path can never modify or delete a dossier document.
    Task<TransportOrderDocumentDto?> UpdateAsync(Guid id, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken, DossierDocumentScope scope = DossierDocumentScope.Order);
    Task<TransportOrderDocumentDto?> AttachFileAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order);
    Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order);
    Task<bool> RemoveFileAsync(Guid id, CancellationToken cancellationToken, DossierDocumentScope scope = DossierDocumentScope.Order);

    /// <summary>D6: the scope of a document of this tenant; null = unknown (→ 404).</summary>
    Task<DossierDocumentScope?> GetScopeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>D6: every document of the dossier, both scopes, each exactly once. Null = unknown dossier.</summary>
    Task<IReadOnlyList<DossierDocumentDto>?> ListForDossierAsync(Guid dossierId, CancellationToken cancellationToken);

    Task<DossierDocumentDto?> CreateForDossierAsync(Guid dossierId, CreateDossierDocumentRequest request, CancellationToken cancellationToken);

    /// <summary>D6: changes the LINK only (file, name, type and visibility stay untouched). Null = unknown document.</summary>
    Task<DossierDocumentDto?> MoveAsync(Guid id, Guid? targetTransportOrderId, CancellationToken cancellationToken);
}

/// <summary>
/// Order and dossier documents (customer delivery note, CMR, ...) using the shared file storage.
/// Same two-step model as fleet documents: metadata first, binary via the upload endpoint.
/// <para>
/// D6 (master sprint 2026-09-21): ONE entity for both levels. An order document's
/// <c>DossierId</c> always equals the owning dossier of its order (set here on create, followed by
/// <see cref="OrderDocumentDossierSync"/> when order↔dossier links change). Writing a DOSSIER-level
/// document — and creating or moving through the dossier — is refused on a closed dossier with the
/// same message as every other dossier child mutation; order documents through the order routes
/// behave as before.
/// </para>
/// </summary>
public class TransportOrderDocumentService : ITransportOrderDocumentService
{
    private const string EntityType = "TransportOrderDocument";
    private const string StorageCategory = "order-documents";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUserContext? _currentUser;

    public TransportOrderDocumentService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, IFileStorageService fileStorage, ICurrentUserContext? currentUser = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<TransportOrderDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!await OrderExistsAsync(orderId, cancellationToken))
        {
            return null;
        }

        return (await _dbContext.TransportOrderDocuments.AsNoTracking()
                .Where(d => d.TenantId == _tenantContext.TenantId && d.TransportOrderId == orderId)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(Map)
            .ToList();
    }

    public async Task<TransportOrderDocumentDto?> CreateAsync(
        Guid orderId, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!await OrderExistsAsync(orderId, cancellationToken))
        {
            return null;
        }

        Validate(request.Title);
        var owner = await OwningDossierResolver.ResolveAsync(_dbContext, _tenantContext.TenantId, orderId, cancellationToken);
        var document = NewDocument(orderId, owner?.Id);
        Apply(document, request);
        _dbContext.TransportOrderDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Created", null,
            new { document.TransportOrderId, document.DossierId, document.DocumentType, document.Title, document.CustomerVisible }, cancellationToken);
        return Map(document);
    }

    public async Task<TransportOrderDocumentDto?> UpdateAsync(
        Guid id, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order)
    {
        var document = await FindAsync(id, scope, cancellationToken);
        if (document is null)
        {
            return null;
        }

        await RequireWritableAsync(document, cancellationToken);
        Validate(request.Title);
        Apply(document, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Updated", null,
            new { document.DocumentType, document.Title, document.CustomerVisible }, cancellationToken);
        return Map(document);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order)
    {
        var document = await FindAsync(id, scope, cancellationToken);
        if (document is null)
        {
            return false;
        }

        await RequireWritableAsync(document, cancellationToken);
        if (document.DocumentPath is { } path)
        {
            await _fileStorage.DeleteAsync(path, cancellationToken);
        }

        _dbContext.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Deleted",
            new { document.Title, Scope = document.Scope.ToString() }, null, cancellationToken);
        return true;
    }

    public async Task<TransportOrderDocumentDto?> AttachFileAsync(
        Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order)
    {
        var document = await FindAsync(id, scope, cancellationToken);
        if (document is null)
        {
            return null;
        }

        await RequireWritableAsync(document, cancellationToken);
        if (document.DocumentPath is { } previous)
        {
            await _fileStorage.DeleteAsync(previous, cancellationToken);
        }

        document.DocumentPath = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        document.FileName = fileName;
        document.ContentType = contentType;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "FileAttached", null, new { fileName }, cancellationToken);
        return Map(document);
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order)
    {
        var document = await FindAsync(id, scope, cancellationToken);
        if (document?.DocumentPath is not { } path)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(path, cancellationToken);
        return (stream, document.FileName ?? "document", document.ContentType ?? "application/octet-stream");
    }

    public async Task<bool> RemoveFileAsync(Guid id, CancellationToken cancellationToken,
        DossierDocumentScope scope = DossierDocumentScope.Order)
    {
        var document = await FindAsync(id, scope, cancellationToken);
        if (document?.DocumentPath is not { } path)
        {
            return false;
        }

        await RequireWritableAsync(document, cancellationToken);
        await _fileStorage.DeleteAsync(path, cancellationToken);
        document.DocumentPath = null;
        document.FileName = null;
        document.ContentType = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "FileRemoved", null, null, cancellationToken);
        return true;
    }

    public async Task<DossierDocumentScope?> GetScopeAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _dbContext.TransportOrderDocuments.AsNoTracking()
            .Where(d => d.TenantId == _tenantContext.TenantId && d.Id == id)
            .Select(d => new { d.TransportOrderId })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null
            ? null
            : row.TransportOrderId is null ? DossierDocumentScope.Dossier : DossierDocumentScope.Order;
    }

    public async Task<IReadOnlyList<DossierDocumentDto>?> ListForDossierAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        if (await FindDossierAsync(dossierId, cancellationToken) is null)
        {
            return null;
        }

        var documents = await DossierDocumentQuery.ForDossier(_dbContext, _tenantContext.TenantId, dossierId)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
            .ToListAsync(cancellationToken);
        return await MapForDossierAsync(documents, dossierId, cancellationToken);
    }

    public async Task<DossierDocumentDto?> CreateForDossierAsync(
        Guid dossierId, CreateDossierDocumentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindDossierAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        RequireOpen(dossier);
        Validate(request.Title);

        if (request.TransportOrderId is { } orderId)
        {
            // Never an order of another tenant, and never an order of another dossier: the
            // document's DossierId must equal the owning dossier of its order.
            await _dbContext.TransportOrders.EnsureBelongsToTenantAsync(orderId, tenantId, "De gekozen opdracht", cancellationToken);
            var owner = await OwningDossierResolver.ResolveAsync(_dbContext, tenantId, orderId, cancellationToken);
            if (owner?.Id != dossierId)
            {
                throw new DomainValidationException("transportOrderId", "Deze opdracht hoort niet bij dit dossier.");
            }
        }

        var document = NewDocument(request.TransportOrderId, dossierId);
        Apply(document, new SaveTransportOrderDocumentRequest(
            request.DocumentType, request.CustomTypeName, request.Title, request.IssueDate, request.Notes, request.CustomerVisible));
        _dbContext.TransportOrderDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Created", null,
            new { document.TransportOrderId, document.DossierId, document.DocumentType, document.Title, document.CustomerVisible }, cancellationToken);
        return (await MapForDossierAsync([document], dossierId, cancellationToken))[0];
    }

    public async Task<DossierDocumentDto?> MoveAsync(Guid id, Guid? targetTransportOrderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var document = await _dbContext.TransportOrderDocuments
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        await _dbContext.TransportOrders.EnsureBelongsToTenantAsync(targetTransportOrderId, tenantId, "De gekozen opdracht", cancellationToken);

        // Idempotent: already there = nothing to do (and nothing to audit).
        if (document.TransportOrderId == targetTransportOrderId)
        {
            return (await MapForDossierAsync([document], document.DossierId, cancellationToken))[0];
        }

        // Moving happens INSIDE one dossier. An order document of an order without dossier has no
        // dossier level to move to, and no sibling orders.
        if (document.DossierId is not { } dossierId)
        {
            throw new DomainValidationException("targetTransportOrderId", "Dit document hoort niet bij een dossier en kan niet verplaatst worden.");
        }

        if (targetTransportOrderId is { } targetOrderId)
        {
            var targetOwner = await OwningDossierResolver.ResolveAsync(_dbContext, tenantId, targetOrderId, cancellationToken);
            if (targetOwner?.Id != dossierId)
            {
                throw new DomainValidationException("targetTransportOrderId", "De doelopdracht moet in hetzelfde dossier zitten als het document.");
            }
        }

        var dossier = await FindDossierAsync(dossierId, cancellationToken);
        if (dossier is not null)
        {
            RequireOpen(dossier);
        }

        // ONLY the link changes — DocumentPath, FileName, ContentType and CustomerVisible are not
        // touched, the stored file is neither moved nor copied.
        var before = new { Scope = document.Scope.ToString(), document.TransportOrderId, document.DossierId };
        document.TransportOrderId = targetTransportOrderId;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Moved", before,
            new { Scope = document.Scope.ToString(), document.TransportOrderId, document.DossierId }, cancellationToken);
        return (await MapForDossierAsync([document], dossierId, cancellationToken))[0];
    }

    private static void Validate(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainValidationException("title", "De titel is verplicht.");
        }
    }

    private TransportOrderDocument NewDocument(Guid? orderId, Guid? dossierId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantContext.TenantId,
        TransportOrderId = orderId,
        DossierId = dossierId,
        // The auditing interceptor stamps the same user for a request; setting it here keeps
        // "uploaded by" correct outside an HTTP request too (the interceptor only fills a null).
        CreatedByUserId = _currentUser?.CurrentUserId,
    };

    private static void Apply(TransportOrderDocument document, SaveTransportOrderDocumentRequest request)
    {
        document.DocumentType = request.DocumentType;
        document.CustomTypeName = string.IsNullOrWhiteSpace(request.CustomTypeName) ? null : request.CustomTypeName.Trim();
        document.Title = request.Title.Trim();
        document.IssueDate = request.IssueDate;
        document.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        // H-14: publication to the customer portal is always an explicit choice of the uploader —
        // and an omitted value changes nothing (a new document starts internal either way).
        if (request.CustomerVisible is { } customerVisible)
        {
            document.CustomerVisible = customerVisible;
        }
    }

    private static TransportOrderDocumentDto Map(TransportOrderDocument d) => new(
        d.Id, d.TransportOrderId, d.DocumentType, d.CustomTypeName, d.Title,
        d.DocumentPath != null, d.FileName, d.IssueDate, d.Notes, d.CustomerVisible, d.DossierId, d.Scope);

    /// <summary>Order numbers and uploader names in ONE query each for the whole list.</summary>
    private async Task<List<DossierDocumentDto>> MapForDossierAsync(
        IReadOnlyList<TransportOrderDocument> documents, Guid? dossierId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var orderIds = documents.Where(d => d.TransportOrderId is not null).Select(d => d.TransportOrderId!.Value).Distinct().ToList();
        var orderNumbers = orderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber })
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);

        var userIds = documents.Where(d => d.CreatedByUserId is not null).Select(d => d.CreatedByUserId!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(u => u.Id, u => u.Name.Trim(), cancellationToken);

        return documents
            .Select(d => new DossierDocumentDto(
                // A legacy order document without DossierId is listed under the dossier it is reached through.
                d.Id, d.DossierId ?? dossierId, d.TransportOrderId,
                d.TransportOrderId is { } orderId ? orderNumbers.GetValueOrDefault(orderId) : null,
                d.Scope, d.DocumentType, d.CustomTypeName, d.Title, d.FileName, d.ContentType, d.IssueDate, d.Notes,
                d.CustomerVisible, d.DocumentPath != null, d.CreatedAt,
                d.CreatedByUserId is { } userId ? userNames.GetValueOrDefault(userId) : null))
            .ToList();
    }

    /// <summary>A row of the OTHER scope is not found: the caller was authorized for <paramref name="scope"/> only.</summary>
    private async Task<TransportOrderDocument?> FindAsync(Guid id, DossierDocumentScope scope, CancellationToken cancellationToken)
    {
        var document = await _dbContext.TransportOrderDocuments.FirstOrDefaultAsync(
            d => d.TenantId == _tenantContext.TenantId && d.Id == id, cancellationToken);
        return document is not null && document.Scope == scope ? document : null;
    }

    /// <summary>A DOSSIER-level document follows the dossier: no writes while it is closed. Order documents are unchanged.</summary>
    private async Task RequireWritableAsync(TransportOrderDocument document, CancellationToken cancellationToken)
    {
        if (document.Scope == DossierDocumentScope.Dossier
            && document.DossierId is { } dossierId
            && await FindDossierAsync(dossierId, cancellationToken) is { } dossier)
        {
            RequireOpen(dossier);
        }
    }

    private static void RequireOpen(TransportDossier dossier)
    {
        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Een gesloten dossier kan niet worden bewerkt. Heropen het dossier eerst.");
        }
    }

    private Task<TransportDossier?> FindDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        _dbContext.TransportDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == _tenantContext.TenantId && d.Id == dossierId, cancellationToken);

    private Task<bool> OrderExistsAsync(Guid orderId, CancellationToken cancellationToken) =>
        _dbContext.TransportOrders.AnyAsync(o => o.TenantId == _tenantContext.TenantId && o.Id == orderId, cancellationToken);
}

/// <summary>
/// D6: THE definition of "the documents of a dossier" — shared by the dossier list and the dossier
/// count so they can never disagree. Every non-deleted document with <c>DossierId == dossier</c>
/// (both scopes), plus legacy order documents whose <c>DossierId</c> is still NULL and that are
/// reachable only through an order linked to the dossier. One row per document: counted once.
/// </summary>
public static class DossierDocumentQuery
{
    public static IQueryable<TransportOrderDocument> ForDossier(TransportationDbContext dbContext, Guid tenantId, Guid dossierId)
    {
        var linkedOrderIds = dbContext.DossierOrders
            .Where(l => l.TenantId == tenantId && l.DossierId == dossierId)
            .Select(l => (Guid?)l.TransportOrderId);
        return dbContext.TransportOrderDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId
                        && (d.DossierId == dossierId
                            || (d.DossierId == null && linkedOrderIds.Contains(d.TransportOrderId))));
    }
}

/// <summary>
/// D6: keeps <c>TransportOrderDocument.DossierId</c> of ORDER documents equal to the owning dossier
/// of their order when an order↔dossier link changes. Stages the change on the caller's context —
/// the caller's SaveChanges persists link and documents together. Only the link column changes; no
/// file is touched. Dossier-level documents are never affected (they have no order).
/// </summary>
public static class OrderDocumentDossierSync
{
    public static async Task StageAsync(
        TransportationDbContext dbContext, Guid tenantId, Guid orderId, Guid? owningDossierId, CancellationToken cancellationToken)
    {
        var documents = await dbContext.TransportOrderDocuments
            .Where(d => d.TenantId == tenantId && d.TransportOrderId == orderId && d.DossierId != owningDossierId)
            .ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            document.DossierId = owningDossierId;
        }
    }
}
