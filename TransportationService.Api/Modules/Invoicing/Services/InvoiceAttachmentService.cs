using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public record InvoiceAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IncludeWhenSending,
    string? Notes,
    DateTime UploadedAt,
    Guid? UploadedByUserId);

public record UpdateInvoiceAttachmentRequest(bool IncludeWhenSending, string? Notes);

public interface IInvoiceAttachmentService
{
    Task<IReadOnlyList<InvoiceAttachmentDto>?> ListAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<InvoiceAttachmentDto?> UploadAsync(Guid invoiceId, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken cancellationToken);
    Task<InvoiceAttachmentDto?> UpdateAsync(Guid invoiceId, Guid attachmentId, UpdateInvoiceAttachmentRequest request, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName, string ContentType)?> OpenAsync(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Invoice attachments: metadata rows + IFileStorageService content (category "invoices").
/// Attachments are INTERNAL by default; IncludeWhenSending marks the ones meant to
/// accompany an outgoing invoice (no automatic mailing exists yet — the flag is the seam).
/// Deleting is Draft-only so the file trail of a sent invoice stays intact.
/// </summary>
public class InvoiceAttachmentService : IInvoiceAttachmentService
{
    private const string EntityType = "Invoice";
    private const string StorageCategory = "invoices";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _fileStorage;

    public InvoiceAttachmentService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<InvoiceAttachmentDto>?> ListAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (await FindInvoiceAsync(invoiceId, cancellationToken) is null)
        {
            return null;
        }

        return await _dbContext.InvoiceAttachments
            .Where(a => a.TenantId == _tenantContext.TenantId && a.InvoiceId == invoiceId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new InvoiceAttachmentDto(
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.IncludeWhenSending, a.Notes, a.CreatedAt, a.CreatedByUserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<InvoiceAttachmentDto?> UploadAsync(
        Guid invoiceId, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken cancellationToken)
    {
        var invoice = await FindInvoiceAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            throw new DomainValidationException("Aan een geannuleerde factuur kunnen geen bijlagen worden toegevoegd.");
        }

        var attachment = new InvoiceAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            InvoiceId = invoiceId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken),
            IncludeWhenSending = false,
        };
        _dbContext.InvoiceAttachments.Add(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoiceId.ToString(), "AttachmentAdded", null,
            new { attachment.Id, attachment.FileName, attachment.SizeBytes }, cancellationToken);

        return Map(attachment);
    }

    public async Task<InvoiceAttachmentDto?> UpdateAsync(
        Guid invoiceId, Guid attachmentId, UpdateInvoiceAttachmentRequest request, CancellationToken cancellationToken)
    {
        var attachment = await FindAttachmentAsync(invoiceId, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var before = new { attachment.IncludeWhenSending };
        attachment.IncludeWhenSending = request.IncludeWhenSending;
        attachment.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoiceId.ToString(), "AttachmentChanged", before,
            new { attachment.Id, attachment.IncludeWhenSending }, cancellationToken);

        return Map(attachment);
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenAsync(
        Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await FindAttachmentAsync(invoiceId, attachmentId, cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        return (stream, attachment.FileName, attachment.ContentType);
    }

    public async Task<bool> DeleteAsync(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var invoice = await FindInvoiceAsync(invoiceId, cancellationToken);
        var attachment = await FindAttachmentAsync(invoiceId, attachmentId, cancellationToken);
        if (invoice is null || attachment is null)
        {
            return false;
        }

        if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled))
        {
            throw new DomainValidationException("Bijlagen van een verzonden factuur kunnen niet worden verwijderd.");
        }

        var storageKey = attachment.StorageKey;
        _dbContext.Remove(attachment); // soft delete: metadata trail stays
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _fileStorage.DeleteAsync(storageKey, cancellationToken);

        await _auditService.RecordAsync(EntityType, invoiceId.ToString(), "AttachmentRemoved",
            new { attachment.Id, attachment.FileName }, null, cancellationToken);

        return true;
    }

    private Task<Invoice?> FindInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        _dbContext.Invoices.FirstOrDefaultAsync(
            i => i.TenantId == _tenantContext.TenantId && i.Id == invoiceId, cancellationToken)!;

    private Task<InvoiceAttachment?> FindAttachmentAsync(Guid invoiceId, Guid attachmentId, CancellationToken cancellationToken) =>
        _dbContext.InvoiceAttachments.FirstOrDefaultAsync(
            a => a.TenantId == _tenantContext.TenantId && a.InvoiceId == invoiceId && a.Id == attachmentId, cancellationToken)!;

    private static InvoiceAttachmentDto Map(InvoiceAttachment a) =>
        new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.IncludeWhenSending, a.Notes, a.CreatedAt, a.CreatedByUserId);
}
