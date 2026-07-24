using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

public record TransportOrderDocumentDto(
    Guid Id, Guid TransportOrderId, TransportOrderDocumentType DocumentType, string? CustomTypeName,
    string Title, bool HasAttachment, string? FileName, DateOnly? IssueDate, string? Notes);

public record SaveTransportOrderDocumentRequest(
    TransportOrderDocumentType DocumentType, string? CustomTypeName, string Title,
    DateOnly? IssueDate, string? Notes);

public interface ITransportOrderDocumentService
{
    Task<IReadOnlyList<TransportOrderDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken);
    Task<TransportOrderDocumentDto?> CreateAsync(Guid orderId, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken);
    Task<TransportOrderDocumentDto?> UpdateAsync(Guid id, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<TransportOrderDocumentDto?> AttachFileAsync(Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> RemoveFileAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// Order documents (customer delivery note, CMR, ...) using the shared file storage. Same
/// two-step model as fleet documents: metadata first, binary via the upload endpoint.
/// </summary>
public class TransportOrderDocumentService : ITransportOrderDocumentService
{
    private const string EntityType = "TransportOrderDocument";
    private const string StorageCategory = "order-documents";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _fileStorage;

    public TransportOrderDocumentService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<TransportOrderDocumentDto>?> ListAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!await OrderExistsAsync(orderId, cancellationToken))
        {
            return null;
        }

        return await _dbContext.TransportOrderDocuments.AsNoTracking()
            .Where(d => d.TenantId == _tenantContext.TenantId && d.TransportOrderId == orderId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => Map(d))
            .ToListAsync(cancellationToken);
    }

    public async Task<TransportOrderDocumentDto?> CreateAsync(
        Guid orderId, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!await OrderExistsAsync(orderId, cancellationToken))
        {
            return null;
        }

        Validate(request);
        var document = new TransportOrderDocument { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId, TransportOrderId = orderId };
        Apply(document, request);
        _dbContext.TransportOrderDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Created", null,
            new { document.TransportOrderId, document.DocumentType, document.Title }, cancellationToken);
        return Map(document);
    }

    public async Task<TransportOrderDocumentDto?> UpdateAsync(
        Guid id, SaveTransportOrderDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        Validate(request);
        Apply(document, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Updated", null,
            new { document.DocumentType, document.Title }, cancellationToken);
        return Map(document);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
        if (document is null)
        {
            return false;
        }

        if (document.DocumentPath is { } path)
        {
            await _fileStorage.DeleteAsync(path, cancellationToken);
        }

        _dbContext.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Deleted", new { document.Title }, null, cancellationToken);
        return true;
    }

    public async Task<TransportOrderDocumentDto?> AttachFileAsync(
        Guid id, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
        if (document is null)
        {
            return null;
        }

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

    public async Task<(Stream Content, string FileName, string ContentType)?> OpenFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
        if (document?.DocumentPath is not { } path)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(path, cancellationToken);
        return (stream, document.FileName ?? "document", document.ContentType ?? "application/octet-stream");
    }

    public async Task<bool> RemoveFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await FindAsync(id, cancellationToken);
        if (document?.DocumentPath is not { } path)
        {
            return false;
        }

        await _fileStorage.DeleteAsync(path, cancellationToken);
        document.DocumentPath = null;
        document.FileName = null;
        document.ContentType = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "FileRemoved", null, null, cancellationToken);
        return true;
    }

    private static void Validate(SaveTransportOrderDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainValidationException("title", "De titel is verplicht.");
        }
    }

    private static void Apply(TransportOrderDocument document, SaveTransportOrderDocumentRequest request)
    {
        document.DocumentType = request.DocumentType;
        document.CustomTypeName = string.IsNullOrWhiteSpace(request.CustomTypeName) ? null : request.CustomTypeName.Trim();
        document.Title = request.Title.Trim();
        document.IssueDate = request.IssueDate;
        document.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }

    private static TransportOrderDocumentDto Map(TransportOrderDocument d) => new(
        d.Id, d.TransportOrderId, d.DocumentType, d.CustomTypeName, d.Title,
        d.DocumentPath != null, d.FileName, d.IssueDate, d.Notes);

    private Task<TransportOrderDocument?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.TransportOrderDocuments.FirstOrDefaultAsync(
            d => d.TenantId == _tenantContext.TenantId && d.Id == id, cancellationToken);

    private Task<bool> OrderExistsAsync(Guid orderId, CancellationToken cancellationToken) =>
        _dbContext.TransportOrders.AnyAsync(o => o.TenantId == _tenantContext.TenantId && o.Id == orderId, cancellationToken);
}
