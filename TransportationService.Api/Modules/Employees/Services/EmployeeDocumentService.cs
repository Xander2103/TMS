using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

public record EmployeeDocumentDto(
    Guid Id,
    EmployeeDocumentCategory Category,
    string? CustomLabel,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateOnly? ExpiryDate,
    string? Notes,
    bool IsArchived,
    bool IsSensitive,
    DateTime UploadedAt,
    Guid? UploadedByUserId);

public record SaveEmployeeDocumentMetadata(
    EmployeeDocumentCategory Category,
    string? CustomLabel,
    DateOnly? ExpiryDate,
    string? Notes);

/// <summary>
/// Opened personnel document. <see cref="SensitiveRestricted"/> distinguishes "exists but the
/// caller lacks employee_documents.view_sensitive" (403) from "unknown document" (null, 404).
/// </summary>
public sealed record EmployeeDocumentContentResult(
    Stream? Content, string? FileName, string? ContentType, bool SensitiveRestricted)
{
    public static readonly EmployeeDocumentContentResult AccessDenied = new(null, null, null, true);
}

public interface IEmployeeDocumentService
{
    Task<IReadOnlyList<EmployeeDocumentDto>?> ListAsync(Guid employeeId, bool includeSensitive, bool includeArchived, CancellationToken cancellationToken);
    Task<EmployeeDocumentDto?> UploadAsync(Guid employeeId, SaveEmployeeDocumentMetadata metadata, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken cancellationToken);
    Task<EmployeeDocumentDto?> UpdateMetadataAsync(Guid employeeId, Guid documentId, SaveEmployeeDocumentMetadata metadata, CancellationToken cancellationToken);
    Task<EmployeeDocumentDto?> ReplaceFileAsync(Guid employeeId, Guid documentId, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken cancellationToken);
    Task<EmployeeDocumentDto?> SetArchivedAsync(Guid employeeId, Guid documentId, bool archived, CancellationToken cancellationToken);

    /// <summary>Sensitive-category documents open only with <paramref name="includeSensitive"/>;
    /// every successful sensitive download is read-audited (SensitiveDocumentDownloaded).</summary>
    Task<EmployeeDocumentContentResult?> OpenAsync(Guid employeeId, Guid documentId, bool includeSensitive, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid employeeId, Guid documentId, CancellationToken cancellationToken);

    bool IsSensitive(EmployeeDocumentCategory category);
}

public class EmployeeDocumentService : IEmployeeDocumentService
{
    private const string EntityType = "EmployeeDocument";
    private const string StorageCategory = "employee-documents";

    // Categories that require employee_documents.view_sensitive to list/download.
    private static readonly HashSet<EmployeeDocumentCategory> SensitiveCategories =
    [
        EmployeeDocumentCategory.IdentityCardFront,
        EmployeeDocumentCategory.IdentityCardBack,
        EmployeeDocumentCategory.MedicalDocument,
        EmployeeDocumentCategory.Contract,
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IFileStorageService _fileStorage;

    public EmployeeDocumentService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _fileStorage = fileStorage;
    }

    public bool IsSensitive(EmployeeDocumentCategory category) => SensitiveCategories.Contains(category);

    /// <summary>Static variant so read-side projections (employee history) share the exact same
    /// sensitivity definition without needing the full service.</summary>
    public static bool IsSensitiveCategory(EmployeeDocumentCategory category) => SensitiveCategories.Contains(category);

    public async Task<IReadOnlyList<EmployeeDocumentDto>?> ListAsync(
        Guid employeeId, bool includeSensitive, bool includeArchived, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        var documents = await _dbContext.EmployeeDocuments
            .Where(d => d.TenantId == _tenantContext.TenantId && d.EmployeeId == employeeId
                        && (includeArchived || !d.IsArchived))
            .OrderBy(d => d.Category).ThenByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        // Sensitive documents are hidden entirely from callers without the permission.
        return documents
            .Where(d => includeSensitive || !IsSensitive(d.Category))
            .Select(Map)
            .ToList();
    }

    public async Task<EmployeeDocumentDto?> UploadAsync(
        Guid employeeId, SaveEmployeeDocumentMetadata metadata, string fileName, string contentType, long sizeBytes,
        Stream content, CancellationToken cancellationToken)
    {
        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return null;
        }

        var document = new EmployeeDocument
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            Category = metadata.Category,
            CustomLabel = Trim(metadata.CustomLabel),
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken),
            ExpiryDate = metadata.ExpiryDate,
            Notes = Trim(metadata.Notes),
        };
        _dbContext.EmployeeDocuments.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Uploaded", null,
            new { document.EmployeeId, document.Category, document.FileName }, cancellationToken);

        return Map(document);
    }

    public async Task<EmployeeDocumentDto?> UpdateMetadataAsync(
        Guid employeeId, Guid documentId, SaveEmployeeDocumentMetadata metadata, CancellationToken cancellationToken)
    {
        var document = await FindAsync(employeeId, documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        document.Category = metadata.Category;
        document.CustomLabel = Trim(metadata.CustomLabel);
        document.ExpiryDate = metadata.ExpiryDate;
        document.Notes = Trim(metadata.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "MetadataChanged", null,
            new { document.Category, document.ExpiryDate }, cancellationToken);

        return Map(document);
    }

    public async Task<EmployeeDocumentDto?> ReplaceFileAsync(
        Guid employeeId, Guid documentId, string fileName, string contentType, long sizeBytes,
        Stream content, CancellationToken cancellationToken)
    {
        var document = await FindAsync(employeeId, documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var previousKey = document.StorageKey;
        document.StorageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        document.FileName = fileName;
        document.ContentType = contentType;
        document.SizeBytes = sizeBytes;
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (previousKey is not null)
        {
            await _fileStorage.DeleteAsync(previousKey, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "FileReplaced", null,
            new { document.FileName }, cancellationToken);

        return Map(document);
    }

    public async Task<EmployeeDocumentDto?> SetArchivedAsync(
        Guid employeeId, Guid documentId, bool archived, CancellationToken cancellationToken)
    {
        var document = await FindAsync(employeeId, documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        document.IsArchived = archived;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), archived ? "Archived" : "Unarchived",
            null, null, cancellationToken);

        return Map(document);
    }

    public async Task<EmployeeDocumentContentResult?> OpenAsync(
        Guid employeeId, Guid documentId, bool includeSensitive, CancellationToken cancellationToken)
    {
        var document = await FindAsync(employeeId, documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var sensitive = IsSensitive(document.Category);
        if (sensitive && !includeSensitive)
        {
            return EmployeeDocumentContentResult.AccessDenied;
        }

        var stream = await _fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);

        // Read-audit (M6/M14): downloads of ID/medical/contract documents leave a trace with a
        // data classification, so a reviewer can filter the trail down to special-category access.
        if (sensitive)
        {
            await _auditService.RecordAsync(EntityType, document.Id.ToString(),
                SecurityAuditEvents.SensitiveDocumentDownloaded, null,
                new
                {
                    document.EmployeeId,
                    document.Category,
                    document.FileName,
                    Classification = document.Category == EmployeeDocumentCategory.MedicalDocument
                        ? SecurityAuditEvents.Classification.Health
                        : SecurityAuditEvents.Classification.Confidential,
                }, cancellationToken);
        }

        return new EmployeeDocumentContentResult(stream, document.FileName, document.ContentType, false);
    }

    public async Task<bool> DeleteAsync(Guid employeeId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await FindAsync(employeeId, documentId, cancellationToken);
        if (document is null)
        {
            return false;
        }

        var storageKey = document.StorageKey;
        _dbContext.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _fileStorage.DeleteAsync(storageKey, cancellationToken);

        await _auditService.RecordAsync(EntityType, document.Id.ToString(), "Deleted",
            new { document.EmployeeId, document.Category, document.FileName }, null, cancellationToken);

        return true;
    }

    private Task<EmployeeDocument?> FindAsync(Guid employeeId, Guid documentId, CancellationToken cancellationToken) =>
        _dbContext.EmployeeDocuments.FirstOrDefaultAsync(
            d => d.TenantId == _tenantContext.TenantId && d.EmployeeId == employeeId && d.Id == documentId, cancellationToken)!;

    private Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken) =>
        _dbContext.Employees.AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId, cancellationToken);

    private EmployeeDocumentDto Map(EmployeeDocument d) => new(
        d.Id, d.Category, d.CustomLabel, d.FileName, d.ContentType, d.SizeBytes,
        d.ExpiryDate, d.Notes, d.IsArchived, IsSensitive(d.Category), d.CreatedAt, d.CreatedByUserId);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
