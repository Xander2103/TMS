using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tasks.Services;

public record TaskTemplateItemDto(
    Guid Id, int SortOrder, string Title, string? Description, Guid? CategoryId,
    TaskPriority Priority, int? DueInDays, bool RequiresReview, bool RequiresCompletionNote, bool RequiresEvidence);

public record TaskTemplateDto(
    Guid Id, string Name, string? Description, bool IsActive, int SortOrder,
    IReadOnlyList<TaskTemplateItemDto> Items);

public record SaveTaskTemplateItemRequest(
    string Title, string? Description = null, Guid? CategoryId = null,
    TaskPriority Priority = TaskPriority.Normal, int? DueInDays = null,
    bool RequiresReview = false, bool RequiresCompletionNote = false, bool RequiresEvidence = false);

public record SaveTaskTemplateRequest(
    string Name, string? Description, bool IsActive, int SortOrder,
    IReadOnlyList<SaveTaskTemplateItemRequest> Items);

public record ApplyTaskTemplateRequest(Guid EmployeeId, DateTime? StartAt = null);

public record TaskRecurrenceDto(
    Guid Id, Guid TemplateId, string TemplateName, Guid AssignedEmployeeId, string AssignedEmployeeName,
    TaskRecurrenceInterval Interval, int? CustomIntervalDays, DateOnly StartDate, DateOnly? EndDate,
    bool IsActive, DateOnly? LastGeneratedPeriod);

public record SaveTaskRecurrenceRequest(
    Guid TemplateId, Guid AssignedEmployeeId, TaskRecurrenceInterval Interval,
    int? CustomIntervalDays, DateOnly StartDate, DateOnly? EndDate, bool IsActive);

public interface ITaskTemplateService
{
    Task<IReadOnlyList<TaskTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<TaskTemplateDto> CreateTemplateAsync(SaveTaskTemplateRequest request, CancellationToken cancellationToken);
    Task<TaskTemplateDto?> UpdateTemplateAsync(Guid id, SaveTaskTemplateRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Materialises the template into tasks for one employee via the task service
    /// (scope checks + notifications included).</summary>
    Task<IReadOnlyList<EmployeeTaskDto>?> ApplyTemplateAsync(Guid templateId, ApplyTaskTemplateRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRecurrenceDto>> ListRecurrencesAsync(CancellationToken cancellationToken);
    Task<TaskRecurrenceDto> CreateRecurrenceAsync(SaveTaskRecurrenceRequest request, CancellationToken cancellationToken);
    Task<TaskRecurrenceDto?> UpdateRecurrenceAsync(Guid id, SaveTaskRecurrenceRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRecurrenceAsync(Guid id, CancellationToken cancellationToken);
}

public class TaskTemplateService : ITaskTemplateService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IEmployeeTaskService _taskService;
    private readonly TimeProvider _timeProvider;

    public TaskTemplateService(TransportationDbContext dbContext, ITenantContext tenantContext,
        IAuditService auditService, IEmployeeTaskService taskService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _taskService = taskService;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<TaskTemplateDto>> ListTemplatesAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var templates = await _dbContext.TaskTemplates.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && (includeInactive || t.IsActive))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        var templateIds = templates.Select(t => t.Id).ToList();
        var items = await _dbContext.TaskTemplateItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && templateIds.Contains(i.TemplateId))
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);
        var byTemplate = items.ToLookup(i => i.TemplateId);
        return templates.Select(t => Map(t, byTemplate[t.Id])).ToList();
    }

    public async Task<TaskTemplateDto> CreateTemplateAsync(SaveTaskTemplateRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var template = new TaskTemplate { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        Apply(template, request);
        _dbContext.Add(template);
        var items = await ReplaceItemsAsync(template.Id, request.Items, [], cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TaskTemplate", template.Id.ToString(), "Created", null,
            new { template.Name, ItemCount = items.Count }, cancellationToken);
        return Map(template, items);
    }

    public async Task<TaskTemplateDto?> UpdateTemplateAsync(Guid id, SaveTaskTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await _dbContext.TaskTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (template is null)
        {
            return null;
        }

        Validate(request);
        Apply(template, request);
        var existing = await _dbContext.TaskTemplateItems
            .Where(i => i.TenantId == _tenantContext.TenantId && i.TemplateId == id)
            .ToListAsync(cancellationToken);
        var items = await ReplaceItemsAsync(id, request.Items, existing, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TaskTemplate", template.Id.ToString(), "Updated", null,
            new { template.Name, ItemCount = items.Count }, cancellationToken);
        return Map(template, items);
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _dbContext.TaskTemplates
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (template is null)
        {
            return false;
        }

        var usedByRecurrence = await _dbContext.TaskRecurrences
            .AnyAsync(r => r.TenantId == _tenantContext.TenantId && r.TemplateId == id, cancellationToken);
        if (usedByRecurrence)
        {
            throw new DomainValidationException(
                "Dit sjabloon wordt door een terugkerende taak gebruikt. Verwijder eerst de herhaling.");
        }

        _dbContext.Remove(template); // soft delete; generated tasks keep their snapshots
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TaskTemplate", id.ToString(), "Deleted", new { template.Name }, null, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EmployeeTaskDto>?> ApplyTemplateAsync(
        Guid templateId, ApplyTaskTemplateRequest request, CancellationToken cancellationToken)
    {
        var template = await _dbContext.TaskTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == templateId && t.IsActive, cancellationToken);
        if (template is null)
        {
            return null;
        }

        var items = await _dbContext.TaskTemplateItems.AsNoTracking()
            .Where(i => i.TenantId == _tenantContext.TenantId && i.TemplateId == templateId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            throw new DomainValidationException("Dit sjabloon bevat geen taken.");
        }

        var start = request.StartAt ?? _timeProvider.GetUtcNow().UtcDateTime;
        var created = new List<EmployeeTaskDto>();
        foreach (var item in items)
        {
            created.AddRange(await _taskService.CreateAsync(new CreateTaskRequest(
                item.Title, [request.EmployeeId], item.Description, item.CategoryId, item.Priority,
                StartAt: start,
                DueAt: item.DueInDays is { } days ? start.AddDays(days) : null,
                RequiresReview: item.RequiresReview,
                RequiresCompletionNote: item.RequiresCompletionNote,
                RequiresEvidence: item.RequiresEvidence,
                RelatedEntityType: "task_template",
                RelatedEntityId: template.Id.ToString()), cancellationToken));
        }

        await _auditService.RecordAsync("TaskTemplate", template.Id.ToString(), "Applied", null,
            new { template.Name, request.EmployeeId, Tasks = created.Count }, cancellationToken);
        return created;
    }

    // ------------------------------------------------------------- recurrences

    public async Task<IReadOnlyList<TaskRecurrenceDto>> ListRecurrencesAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.TaskRecurrences.AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .Join(_dbContext.TaskTemplates.AsNoTracking(), r => r.TemplateId, t => t.Id,
                (r, t) => new { Recurrence = r, TemplateName = t.Name })
            .Join(_dbContext.Employees.AsNoTracking(), x => x.Recurrence.AssignedEmployeeId, e => e.Id,
                (x, e) => new { x.Recurrence, x.TemplateName, EmployeeName = e.FirstName + " " + e.LastName })
            .OrderBy(x => x.TemplateName)
            .ToListAsync(cancellationToken);
        return rows.Select(x => Map(x.Recurrence, x.TemplateName, x.EmployeeName)).ToList();
    }

    public async Task<TaskRecurrenceDto> CreateRecurrenceAsync(SaveTaskRecurrenceRequest request, CancellationToken cancellationToken)
    {
        var (templateName, employeeName) = await ValidateRecurrenceAsync(request, cancellationToken);
        var recurrence = new TaskRecurrence { Id = Guid.NewGuid(), TenantId = _tenantContext.TenantId };
        Apply(recurrence, request);
        _dbContext.Add(recurrence);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TaskRecurrence", recurrence.Id.ToString(), "Created", null,
            new { Template = templateName, Employee = employeeName, recurrence.Interval }, cancellationToken);
        return Map(recurrence, templateName, employeeName);
    }

    public async Task<TaskRecurrenceDto?> UpdateRecurrenceAsync(Guid id, SaveTaskRecurrenceRequest request, CancellationToken cancellationToken)
    {
        var recurrence = await _dbContext.TaskRecurrences
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.Id == id, cancellationToken);
        if (recurrence is null)
        {
            return null;
        }

        var (templateName, employeeName) = await ValidateRecurrenceAsync(request, cancellationToken);
        Apply(recurrence, request);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TaskRecurrence", recurrence.Id.ToString(), "Updated", null,
            new { Template = templateName, recurrence.Interval, recurrence.IsActive }, cancellationToken);
        return Map(recurrence, templateName, employeeName);
    }

    public async Task<bool> DeleteRecurrenceAsync(Guid id, CancellationToken cancellationToken)
    {
        var recurrence = await _dbContext.TaskRecurrences
            .FirstOrDefaultAsync(r => r.TenantId == _tenantContext.TenantId && r.Id == id, cancellationToken);
        if (recurrence is null)
        {
            return false;
        }

        _dbContext.Remove(recurrence); // soft delete; generated tasks remain
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("TaskRecurrence", id.ToString(), "Deleted", null, null, cancellationToken);
        return true;
    }

    // ------------------------------------------------------------- helpers

    private async Task<(string TemplateName, string EmployeeName)> ValidateRecurrenceAsync(
        SaveTaskRecurrenceRequest request, CancellationToken cancellationToken)
    {
        if (request.Interval == TaskRecurrenceInterval.CustomDays && request.CustomIntervalDays is not (> 0))
        {
            throw new DomainValidationException("customIntervalDays", "Geef een interval in dagen (minstens 1).");
        }

        if (request.EndDate is { } end && end < request.StartDate)
        {
            throw new DomainValidationException("endDate", "De einddatum kan niet vóór de startdatum liggen.");
        }

        var templateName = await _dbContext.TaskTemplates.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.Id == request.TemplateId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainValidationException("templateId", "Het sjabloon bestaat niet.");
        var employeeName = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == request.AssignedEmployeeId)
            .Select(e => e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainValidationException("assignedEmployeeId", "De medewerker bestaat niet.");
        return (templateName, employeeName);
    }

    private static void Validate(SaveTaskTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("name", "De naam is verplicht.");
        }

        if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.Title)))
        {
            throw new DomainValidationException("items", "Elke sjabloontaak heeft een titel nodig.");
        }
    }

    private static void Apply(TaskTemplate template, SaveTaskTemplateRequest request)
    {
        template.Name = request.Name.Trim();
        template.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        template.IsActive = request.IsActive;
        template.SortOrder = request.SortOrder;
    }

    private static void Apply(TaskRecurrence recurrence, SaveTaskRecurrenceRequest request)
    {
        recurrence.TemplateId = request.TemplateId;
        recurrence.AssignedEmployeeId = request.AssignedEmployeeId;
        recurrence.Interval = request.Interval;
        recurrence.CustomIntervalDays = request.Interval == TaskRecurrenceInterval.CustomDays ? request.CustomIntervalDays : null;
        recurrence.StartDate = request.StartDate;
        recurrence.EndDate = request.EndDate;
        recurrence.IsActive = request.IsActive;
    }

    /// <summary>Items are replaced wholesale (small lists); generated tasks are snapshots, so
    /// rewriting items never rewrites history.</summary>
    private Task<List<TaskTemplateItem>> ReplaceItemsAsync(
        Guid templateId, IReadOnlyList<SaveTaskTemplateItemRequest> requested,
        List<TaskTemplateItem> existing, CancellationToken cancellationToken)
    {
        _dbContext.RemoveRange(existing);
        var items = new List<TaskTemplateItem>();
        for (var index = 0; index < requested.Count; index++)
        {
            var request = requested[index];
            var item = new TaskTemplateItem
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TemplateId = templateId,
                SortOrder = index,
                Title = request.Title.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                CategoryId = request.CategoryId,
                Priority = request.Priority,
                DueInDays = request.DueInDays,
                RequiresReview = request.RequiresReview,
                RequiresCompletionNote = request.RequiresCompletionNote,
                RequiresEvidence = request.RequiresEvidence,
            };
            items.Add(item);
            _dbContext.Add(item);
        }

        return Task.FromResult(items);
    }

    private static TaskTemplateDto Map(TaskTemplate t, IEnumerable<TaskTemplateItem> items) =>
        new(t.Id, t.Name, t.Description, t.IsActive, t.SortOrder,
            items.OrderBy(i => i.SortOrder).Select(i => new TaskTemplateItemDto(
                i.Id, i.SortOrder, i.Title, i.Description, i.CategoryId, i.Priority, i.DueInDays,
                i.RequiresReview, i.RequiresCompletionNote, i.RequiresEvidence)).ToList());

    private static TaskRecurrenceDto Map(TaskRecurrence r, string templateName, string employeeName) =>
        new(r.Id, r.TemplateId, templateName, r.AssignedEmployeeId, employeeName,
            r.Interval, r.CustomIntervalDays, r.StartDate, r.EndDate, r.IsActive, r.LastGeneratedPeriod);
}
