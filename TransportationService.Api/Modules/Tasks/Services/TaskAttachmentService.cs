using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tasks.Services;

public record TaskAttachmentDto(
    Guid Id, Guid TaskId, string FileName, string ContentType, long SizeBytes, string? Note,
    Guid? UploadedByUserId, DateTime CreatedAt);

public record TaskAttachmentContent(Stream Stream, string FileName, string ContentType);

public interface ITaskAttachmentService
{
    Task<IReadOnlyList<TaskAttachmentDto>?> ListAsync(Guid taskId, CancellationToken cancellationToken);
    Task<TaskAttachmentDto?> UploadAsync(Guid taskId, string fileName, string contentType, long sizeBytes, Stream content, string? note, CancellationToken cancellationToken);
    Task<TaskAttachmentContent?> DownloadAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken);
}

/// <summary>
/// Evidence files on tasks. Visibility rides on the task service's scope resolution
/// (assignee/creator/team/all); bytes live in file storage under the tenant prefix
/// (same hardened pipeline as employee documents: validation happens in the controller,
/// malware scan inside the storage service).
/// </summary>
public class TaskAttachmentService : ITaskAttachmentService
{
    private const string StorageCategory = "task-attachments";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IEmployeeTaskService _taskService;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditService _auditService;

    public TaskAttachmentService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IEmployeeTaskService taskService, IFileStorageService fileStorage, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _taskService = taskService;
        _fileStorage = fileStorage;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<TaskAttachmentDto>?> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        if (await _taskService.GetAsync(taskId, cancellationToken) is null)
        {
            return null; // task invisible to the caller = 404
        }

        return await _dbContext.TaskAttachments.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.TaskId == taskId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new TaskAttachmentDto(a.Id, a.TaskId, a.FileName, a.ContentType, a.SizeBytes, a.Note, a.CreatedByUserId, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskAttachmentDto?> UploadAsync(
        Guid taskId, string fileName, string contentType, long sizeBytes, Stream content, string? note, CancellationToken cancellationToken)
    {
        var task = await _taskService.GetAsync(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (task.Status is EmployeeTaskStatus.Cancelled)
        {
            throw new DomainValidationException("Aan een geannuleerde taak kan geen bewijs toegevoegd worden.");
        }

        var storageKey = await _fileStorage.SaveAsync(_tenantContext.TenantId, StorageCategory, fileName, content, cancellationToken);
        var attachment = new TaskAttachment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            TaskId = taskId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = storageKey,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        _dbContext.Add(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("EmployeeTask", taskId.ToString(), "EvidenceAdded", null,
            new { attachment.FileName, attachment.SizeBytes }, cancellationToken);
        return new TaskAttachmentDto(attachment.Id, taskId, attachment.FileName, attachment.ContentType,
            attachment.SizeBytes, attachment.Note, attachment.CreatedByUserId, attachment.CreatedAt);
    }

    public async Task<TaskAttachmentContent?> DownloadAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (await _taskService.GetAsync(taskId, cancellationToken) is null)
        {
            return null;
        }

        var attachment = await _dbContext.TaskAttachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TenantId == _tenantContext.TenantId && a.TaskId == taskId && a.Id == attachmentId,
                cancellationToken);
        if (attachment is null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        return stream is null ? null : new TaskAttachmentContent(stream, attachment.FileName, attachment.ContentType);
    }

    public async Task<bool> DeleteAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (await _taskService.GetAsync(taskId, cancellationToken) is null)
        {
            return false;
        }

        var attachment = await _dbContext.TaskAttachments
            .FirstOrDefaultAsync(a => a.TenantId == _tenantContext.TenantId && a.TaskId == taskId && a.Id == attachmentId,
                cancellationToken);
        if (attachment is null)
        {
            return false;
        }

        _dbContext.Remove(attachment); // soft delete; storage blob kept for the audit trail
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("EmployeeTask", taskId.ToString(), "EvidenceRemoved",
            new { attachment.FileName }, null, cancellationToken);
        return true;
    }
}
