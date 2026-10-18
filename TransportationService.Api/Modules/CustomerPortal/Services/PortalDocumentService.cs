using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.CustomerPortal.Services;

public interface IPortalDocumentService
{
    Task<PortalResult<IReadOnlyList<PortalDocumentDto>>> ListMyDocumentsAsync(CancellationToken cancellationToken);
    Task<PortalResult<PortalFileDto>> GetDocumentContentAsync(
        PortalDocumentSource source, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Aggregates every document a portal user may see across three source tables — order
/// documents, customer-visible proof-of-delivery signatures, and invoice attachments marked
/// IncludeWhenSending — behind ONE list and ONE re-checked content endpoint. No new storage:
/// every byte still lives behind <see cref="IFileStorageService"/> under its owning module.
/// POD photos are deliberately NOT included (see task report — deferred, signature only).
/// </summary>
public class PortalDocumentService : IPortalDocumentService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IFileStorageService _fileStorage;
    private readonly Modules.Orders.Services.IIssuedTransportDocumentService? _issuedDocuments;

    public PortalDocumentService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUserContext,
        IFileStorageService fileStorage, Modules.Orders.Services.IIssuedTransportDocumentService? issuedDocuments = null)
    {
        _issuedDocuments = issuedDocuments;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _fileStorage = fileStorage;
    }

    private async Task<Guid?> MyCustomerIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await PortalCustomerResolver.ResolveCustomerIdAsync(
            _dbContext, _tenantContext.TenantId, userId, cancellationToken);
    }

    private sealed class VisibleDossierDocument
    {
        public required Modules.Orders.Entities.TransportOrderDocument Document { get; init; }
        public required string DossierNumber { get; init; }
    }

    /// <summary>
    /// D6: THE portal predicate for a DOSSIER-level document (no order) — published, has a file, and
    /// the dossier belongs to the portal user's customer. Used by the list AND re-checked by the
    /// download, so the two can never drift apart.
    /// </summary>
    private IQueryable<VisibleDossierDocument> VisibleDossierDocuments(Guid tenantId, Guid customerId) =>
        from d in _dbContext.TransportOrderDocuments.AsNoTracking()
        join dossier in _dbContext.TransportDossiers.AsNoTracking() on d.DossierId equals (Guid?)dossier.Id
        where d.TenantId == tenantId && dossier.TenantId == tenantId
            && d.TransportOrderId == null && dossier.CustomerId == customerId
            && d.CustomerVisible && d.DocumentPath != null
        select new VisibleDossierDocument { Document = d, DossierNumber = dossier.DossierNumber };

    public async Task<PortalResult<IReadOnlyList<PortalDocumentDto>>> ListMyDocumentsAsync(CancellationToken cancellationToken)
    {
        var customerId = await MyCustomerIdAsync(cancellationToken);
        if (customerId is null)
        {
            return PortalResult<IReadOnlyList<PortalDocumentDto>>.NoCustomerLink();
        }

        var tenantId = _tenantContext.TenantId;

        var orderDocs = await (
            from d in _dbContext.TransportOrderDocuments.AsNoTracking()
            join o in _dbContext.TransportOrders.AsNoTracking() on d.TransportOrderId equals (Guid?)o.Id
            // H-14: order documents are internal until a planner publishes them explicitly.
            where d.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == customerId
                && d.CustomerVisible && d.DocumentPath != null
            select new PortalDocumentDto(
                d.Id, PortalDocumentSource.OrderDocument, d.Title, d.FileName, d.CreatedAt, o.Id, o.OrderNumber, null, null))
            .ToListAsync(cancellationToken);

        // D6: documents of a dossier as a whole. The level a document hangs on never implies
        // visibility — same opt-in flag, same "has a file" rule, and the DOSSIER's customer must be
        // the portal user's customer (an inner join on the order would silently drop these rows).
        var dossierDocs = await VisibleDossierDocuments(tenantId, customerId.Value)
            .Select(x => new PortalDocumentDto(
                x.Document.Id, PortalDocumentSource.OrderDocument, x.Document.Title, x.Document.FileName,
                x.Document.CreatedAt, null, null, null, null, x.DossierNumber))
            .ToListAsync(cancellationToken);

        var pods = await (
            from p in _dbContext.ProofsOfDelivery.AsNoTracking()
            join o in _dbContext.TransportOrders.AsNoTracking() on p.TransportOrderId equals o.Id
            where p.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == customerId
                && p.IsCurrent && p.CustomerVisible && p.SignaturePath != null
            select new PortalDocumentDto(
                p.Id, PortalDocumentSource.Pod, "Afleverbewijs " + o.OrderNumber, "handtekening.png",
                p.DeliveredAt, o.Id, o.OrderNumber, null, null))
            .ToListAsync(cancellationToken);

        var invoiceAttachments = await (
            from a in _dbContext.InvoiceAttachments.AsNoTracking()
            join i in _dbContext.Invoices.AsNoTracking() on a.InvoiceId equals i.Id
            where a.TenantId == tenantId && i.TenantId == tenantId && i.CustomerId == customerId
                // ACC-H2: the invoice pages' allow-list (issued only) — a Cancelled draft's
                // attachment is not a customer document.
                && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid) && a.IncludeWhenSending
            select new PortalDocumentDto(
                a.Id, PortalDocumentSource.InvoiceAttachment, a.FileName, a.FileName, a.CreatedAt,
                null, null, i.Id, i.InvoiceNumber))
            .ToListAsync(cancellationToken);

        // KP-06: the ACTIVE issued CMR / delivery note / work order a planner published.
        var issued = await (
            from d in _dbContext.IssuedTransportDocuments.AsNoTracking()
            join o in _dbContext.TransportOrders.AsNoTracking() on d.TransportOrderId equals o.Id
            where d.TenantId == tenantId && o.TenantId == tenantId && o.CustomerId == customerId
                && d.CustomerVisible && d.ReplacedByDocumentId == null
            select new PortalDocumentDto(
                d.Id, PortalDocumentSource.IssuedTransportDocument, d.DocumentNumber, d.DocumentNumber + ".pdf",
                d.IssuedAt, o.Id, o.OrderNumber, null, null))
            .ToListAsync(cancellationToken);

        // THE customer visibility rule: documents of an order the customer may not see (an internal
        // draft) stay hidden even when published — the same rule as the order list and detail.
        var visibleOrders = await PortalOrderStatusRules.VisibleOrderIdsAsync(_dbContext, tenantId, customerId.Value,
            orderDocs.Concat(pods).Concat(issued).Select(d => d.OrderId!.Value).Distinct().ToList(),
            cancellationToken);
        issued = issued.Where(d => visibleOrders.Contains(d.OrderId!.Value)).ToList();
        orderDocs = orderDocs.Where(d => visibleOrders.Contains(d.OrderId!.Value)).ToList();
        pods = pods.Where(p => visibleOrders.Contains(p.OrderId!.Value)).ToList();

        var all = orderDocs.Concat(dossierDocs).Concat(pods).Concat(issued).Concat(invoiceAttachments)
            .OrderByDescending(d => d.CreatedAt)
            .ToList();
        return PortalResult<IReadOnlyList<PortalDocumentDto>>.Success(all);
    }

    public async Task<PortalResult<PortalFileDto>> GetDocumentContentAsync(
        PortalDocumentSource source, Guid id, CancellationToken cancellationToken)
    {
        var customerId = await MyCustomerIdAsync(cancellationToken);
        if (customerId is null)
        {
            return PortalResult<PortalFileDto>.NoCustomerLink();
        }

        var tenantId = _tenantContext.TenantId;

        switch (source)
        {
            case PortalDocumentSource.OrderDocument:
            {
                var doc = await (
                    from d in _dbContext.TransportOrderDocuments.AsNoTracking()
                    join o in _dbContext.TransportOrders.AsNoTracking() on d.TransportOrderId equals (Guid?)o.Id
                    where d.TenantId == tenantId && o.TenantId == tenantId
                        && d.Id == id && o.CustomerId == customerId && d.CustomerVisible
                    select new { d.DocumentPath, d.FileName, d.ContentType, OrderId = (Guid?)o.Id })
                    .FirstOrDefaultAsync(cancellationToken)
                    // D6: not an order document of this customer → maybe a published document of one
                    // of this customer's dossiers. The SAME predicate as the list is re-checked here;
                    // the id alone is never trusted.
                    ?? await VisibleDossierDocuments(tenantId, customerId.Value)
                        .Where(x => x.Document.Id == id)
                        .Select(x => new { x.Document.DocumentPath, x.Document.FileName, x.Document.ContentType, OrderId = (Guid?)null })
                        .FirstOrDefaultAsync(cancellationToken);
                if (doc?.DocumentPath is not { } path
                    || (doc.OrderId is { } docOrderId
                        && !await PortalOrderStatusRules.IsVisibleToCustomerAsync(_dbContext, tenantId, customerId.Value, docOrderId, cancellationToken)))
                {
                    return PortalResult<PortalFileDto>.NotFound();
                }

                var stream = await _fileStorage.OpenReadAsync(path, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                return PortalResult<PortalFileDto>.Success(new PortalFileDto(
                    buffer.ToArray(), doc.FileName ?? "document", doc.ContentType ?? "application/octet-stream"));
            }

            case PortalDocumentSource.Pod:
            {
                var pod = await (
                    from p in _dbContext.ProofsOfDelivery.AsNoTracking()
                    join o in _dbContext.TransportOrders.AsNoTracking() on p.TransportOrderId equals o.Id
                    where p.TenantId == tenantId && o.TenantId == tenantId && p.Id == id
                        && o.CustomerId == customerId && p.CustomerVisible && p.IsCurrent
                    select new { p.SignaturePath, OrderId = o.Id })
                    .FirstOrDefaultAsync(cancellationToken);
                if (pod?.SignaturePath is not { } signaturePath
                    || !await PortalOrderStatusRules.IsVisibleToCustomerAsync(_dbContext, tenantId, customerId.Value, pod.OrderId, cancellationToken))
                {
                    return PortalResult<PortalFileDto>.NotFound();
                }

                var stream = await _fileStorage.OpenReadAsync(signaturePath, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                return PortalResult<PortalFileDto>.Success(new PortalFileDto(buffer.ToArray(), "handtekening.png", "image/png"));
            }

            case PortalDocumentSource.InvoiceAttachment:
            {
                var attachment = await (
                    from a in _dbContext.InvoiceAttachments.AsNoTracking()
                    join i in _dbContext.Invoices.AsNoTracking() on a.InvoiceId equals i.Id
                    where a.TenantId == tenantId && i.TenantId == tenantId && a.Id == id
                        && i.CustomerId == customerId && (i.Status == InvoiceStatus.Sent || i.Status == InvoiceStatus.Paid)
                        && a.IncludeWhenSending
                    select new { a.StorageKey, a.FileName, a.ContentType })
                    .FirstOrDefaultAsync(cancellationToken);
                if (attachment is null)
                {
                    return PortalResult<PortalFileDto>.NotFound();
                }

                var stream = await _fileStorage.OpenReadAsync(attachment.StorageKey, cancellationToken);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);
                return PortalResult<PortalFileDto>.Success(new PortalFileDto(buffer.ToArray(), attachment.FileName, attachment.ContentType));
            }

            case PortalDocumentSource.IssuedTransportDocument:
            {
                // The SAME predicate as the list, re-checked: published, still the active document,
                // the order is this customer's and visible to them. The bytes are the frozen snapshot
                // the office downloads too (MD-B1) — never a re-render of live master data.
                var issuedDoc = await (
                    from d in _dbContext.IssuedTransportDocuments.AsNoTracking()
                    join o in _dbContext.TransportOrders.AsNoTracking() on d.TransportOrderId equals o.Id
                    where d.TenantId == tenantId && o.TenantId == tenantId && d.Id == id
                        && o.CustomerId == customerId && d.CustomerVisible && d.ReplacedByDocumentId == null
                    select new { d.Id, OrderId = o.Id })
                    .FirstOrDefaultAsync(cancellationToken);
                if (issuedDoc is null || _issuedDocuments is null
                    || !await PortalOrderStatusRules.IsVisibleToCustomerAsync(_dbContext, tenantId, customerId.Value, issuedDoc.OrderId, cancellationToken)
                    || await _issuedDocuments.RenderPdfAsync(issuedDoc.Id, cancellationToken) is not { } rendered)
                {
                    return PortalResult<PortalFileDto>.NotFound();
                }

                return PortalResult<PortalFileDto>.Success(new PortalFileDto(rendered.Content, rendered.FileName, "application/pdf"));
            }

            default:
                return PortalResult<PortalFileDto>.NotFound();
        }
    }
}
