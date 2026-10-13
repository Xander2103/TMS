using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tasks.Services;

public record CreateTaskRequest(
    string Title,
    IReadOnlyList<Guid> AssignedEmployeeIds,
    string? Description = null,
    Guid? CategoryId = null,
    TaskPriority Priority = TaskPriority.Normal,
    DateTime? StartAt = null,
    DateTime? DueAt = null,
    bool RequiresReview = false,
    bool RequiresCompletionNote = false,
    bool RequiresEvidence = false,
    string? RelatedEntityType = null,
    string? RelatedEntityId = null);

public record UpdateTaskRequest(
    string Title, string? Description, Guid? CategoryId, TaskPriority Priority,
    DateTime? StartAt, DateTime? DueAt,
    bool RequiresReview, bool RequiresCompletionNote, bool RequiresEvidence,
    int ExpectedVersion);

public record TaskStatusActionRequest(int ExpectedVersion, string? Note = null);

public record ReviewTaskRequest(int ExpectedVersion, bool Approve, string? Note = null);

public record TaskListQuery(
    bool Mine = false, bool CreatedByMe = false, Guid? EmployeeId = null, Guid? DepartmentId = null,
    Guid? CategoryId = null, EmployeeTaskStatus? Status = null, TaskPriority? Priority = null,
    DateTime? DueBefore = null, bool OverdueOnly = false, bool WaitingForReviewOnly = false,
    string? RelatedEntityType = null, string? RelatedEntityId = null, string? Search = null,
    int Page = 1, int PageSize = 50);

public record EmployeeTaskDto(
    Guid Id, string Title, string? Description, Guid? CategoryId, string? CategoryName, string? CategoryColor,
    TaskPriority Priority, EmployeeTaskStatus Status,
    DateTime? StartAt, DateTime? DueAt, DateTime? CompletedAt, DateTime? CancelledAt,
    Guid AssignedEmployeeId, string AssignedEmployeeName,
    Guid? CreatedByUserId, string? CreatedByName,
    bool RequiresReview, bool RequiresCompletionNote, bool RequiresEvidence,
    string? RelatedEntityType, string? RelatedEntityId,
    string? BlockedReason, string? CompletionNote, string? ReviewNote,
    Guid? ReviewedByUserId, DateTime? ReviewedAt, DateTime? ReopenedAt,
    bool IsOverdue, int Version, DateTime CreatedAt, DateTime UpdatedAt, Guid? BatchId, Guid? RecurrenceId);

public record TaskListResultDto(IReadOnlyList<EmployeeTaskDto> Items, int Total, int Page, int PageSize);

public record EmployeeOpenTaskSummaryDto(
    Guid EmployeeId, int Todo, int InProgress, int Blocked, int WaitingForReview, int Overdue);

public record RedistributeTasksRequest(
    Guid FromEmployeeId, string Action, Guid? TargetEmployeeId = null,
    DateTime? NewDueAt = null, string? Reason = null);

public record RedistributeResultDto(int AffectedTasks, string Action);

public interface IEmployeeTaskService
{
    Task<IReadOnlyList<EmployeeTaskDto>> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken);
    Task<TaskListResultDto> ListAsync(TaskListQuery query, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> StartAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> BlockAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> ResumeAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> SubmitForReviewAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> CompleteAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> ReviewAsync(Guid id, ReviewTaskRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> ReopenAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<EmployeeTaskDto?> CancelAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<EmployeeTaskDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<EmployeeOpenTaskSummaryDto?> OpenSummaryAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<RedistributeResultDto> RedistributeAsync(RedistributeTasksRequest request, CancellationToken cancellationToken);
}

public class EmployeeTaskService : IEmployeeTaskService
{
    private const string EntityType = "EmployeeTask";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAuditService _auditService;
    private readonly IPermissionAuthorizationService _permissionService;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;

    public EmployeeTaskService(
        TransportationDbContext dbContext, ITenantContext tenantContext, ICurrentUserContext currentUser,
        IAuditService auditService, IPermissionAuthorizationService permissionService,
        INotificationService notificationService, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditService = auditService;
        _permissionService = permissionService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
    }

    // ------------------------------------------------------------- create/update

    public async Task<IReadOnlyList<EmployeeTaskDto>> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainValidationException("title", "Een titel is verplicht.");
        }

        var assigneeIds = (request.AssignedEmployeeIds ?? []).Distinct().ToList();
        if (assigneeIds.Count == 0)
        {
            throw new DomainValidationException("assignedEmployeeIds", "Kies minstens één medewerker.");
        }

        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var assignees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && assigneeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.DepartmentId, Name = e.FirstName + " " + e.LastName, e.IsActive })
            .ToListAsync(cancellationToken);
        if (assignees.Count != assigneeIds.Count)
        {
            throw new DomainValidationException("assignedEmployeeIds", "Eén of meer medewerkers bestaan niet.");
        }

        foreach (var assignee in assignees)
        {
            await EnsureMayAssignAsync(scope, assignee.Id, assignee.DepartmentId, cancellationToken);
        }

        var category = await ResolveCategoryAsync(request.CategoryId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var batchId = assigneeIds.Count > 1 ? Guid.NewGuid() : (Guid?)null;
        var created = new List<EmployeeTask>();
        foreach (var assignee in assignees)
        {
            var task = new EmployeeTask
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                // Explicit: creator identity drives visibility/review rules, so it must not
                // depend on the request-scoped interceptor stamp (absent in jobs/tests).
                CreatedByUserId = userId,
                Title = request.Title.Trim(),
                Description = Trim(request.Description),
                CategoryId = category?.Id,
                CategorySnapshot = category?.Name,
                Priority = request.Priority,
                StartAt = request.StartAt,
                DueAt = request.DueAt,
                AssignedEmployeeId = assignee.Id,
                BatchId = batchId,
                RequiresReview = request.RequiresReview,
                RequiresCompletionNote = request.RequiresCompletionNote,
                RequiresEvidence = request.RequiresEvidence,
                RelatedEntityType = Trim(request.RelatedEntityType),
                RelatedEntityId = Trim(request.RelatedEntityId),
            };
            created.Add(task);
            _dbContext.Add(task);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var task in created)
        {
            await _auditService.RecordAsync(EntityType, task.Id.ToString(), "Created", null,
                new { task.Title, task.AssignedEmployeeId, task.Priority, task.DueAt, task.RequiresReview }, cancellationToken);
            await NotifyAssigneeAsync(task, "task_assigned", "Nieuwe taak",
                $"Taak \"{task.Title}\" is aan jou toegewezen{(task.DueAt is { } due ? $" (deadline {due:dd/MM/yyyy})" : "")}.",
                cancellationToken);
        }

        return await MapManyAsync(created.Select(t => t.Id).ToList(), cancellationToken);
    }

    public async Task<EmployeeTaskDto?> UpdateAsync(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var task = await FindVisibleAsync(id, scope, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var isCreator = task.CreatedByUserId == userId;
        var mayEdit = isCreator
            || await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksEdit, cancellationToken);
        if (!mayEdit)
        {
            throw new DomainValidationException("Je kan alleen taken bewerken die je zelf hebt aangemaakt.");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainValidationException("title", "Een titel is verplicht.");
        }

        EnsureVersion(task, request.ExpectedVersion);
        var category = await ResolveCategoryAsync(request.CategoryId, cancellationToken);
        var old = new { task.Title, task.Priority, task.DueAt };
        var dueChanged = task.DueAt != request.DueAt;
        task.Title = request.Title.Trim();
        task.Description = Trim(request.Description);
        task.CategoryId = category?.Id;
        task.CategorySnapshot = category?.Name;
        task.Priority = request.Priority;
        task.StartAt = request.StartAt;
        task.DueAt = request.DueAt;
        task.RequiresReview = request.RequiresReview;
        task.RequiresCompletionNote = request.RequiresCompletionNote;
        task.RequiresEvidence = request.RequiresEvidence;
        if (dueChanged)
        {
            task.OverdueNotifiedAt = null;
            task.DueSoonNotifiedAt = null;
        }

        task.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, task.Id.ToString(), "Updated", old,
            new { task.Title, task.Priority, task.DueAt }, cancellationToken);
        if (dueChanged)
        {
            await NotifyAssigneeAsync(task, "task_due_changed", "Deadline gewijzigd",
                $"De deadline van \"{task.Title}\" is {(task.DueAt is { } due ? $"gewijzigd naar {due:dd/MM/yyyy HH:mm}" : "verwijderd")}.",
                cancellationToken);
        }

        return await MapOneAsync(task.Id, cancellationToken);
    }

    // ------------------------------------------------------------- status actions

    public Task<EmployeeTaskDto?> StartAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ExpectedVersion, EmployeeTaskStatus.InProgress, "Started", request.Note, cancellationToken);

    public async Task<EmployeeTaskDto?> BlockAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            throw new DomainValidationException("note", "Een reden is verplicht bij het blokkeren van een taak.");
        }

        return await TransitionAsync(id, request.ExpectedVersion, EmployeeTaskStatus.Blocked, "Blocked", request.Note, cancellationToken);
    }

    public Task<EmployeeTaskDto?> ResumeAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ExpectedVersion, EmployeeTaskStatus.InProgress, "Resumed", request.Note, cancellationToken);

    public Task<EmployeeTaskDto?> SubmitForReviewAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ExpectedVersion, EmployeeTaskStatus.WaitingForReview, "SubmittedForReview", request.Note, cancellationToken);

    public Task<EmployeeTaskDto?> CompleteAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken) =>
        TransitionAsync(id, request.ExpectedVersion, EmployeeTaskStatus.Completed, "Completed", request.Note, cancellationToken);

    public async Task<EmployeeTaskDto?> ReviewAsync(Guid id, ReviewTaskRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var task = await FindVisibleAsync(id, scope, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var isCreator = task.CreatedByUserId == userId;
        var mayReview = isCreator
            || await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksReview, cancellationToken);
        if (!mayReview)
        {
            throw new DomainValidationException("Je hebt geen rechten om deze taak te controleren.");
        }

        // The executor can never approve their own work, whatever their permissions.
        if (scope.MyEmployeeId is { } myEmployeeId && myEmployeeId == task.AssignedEmployeeId)
        {
            throw new DomainValidationException("De uitvoerder kan zijn eigen taak niet goedkeuren.");
        }

        if (!request.Approve && string.IsNullOrWhiteSpace(request.Note))
        {
            throw new DomainValidationException("note", "Commentaar is verplicht bij het afkeuren van een taak.");
        }

        EnsureVersion(task, request.ExpectedVersion);
        var target = request.Approve ? EmployeeTaskStatus.Completed : EmployeeTaskStatus.InProgress;
        if (!TaskStatusMachine.CanTransition(task.Status, target, task.RequiresReview))
        {
            throw new DomainValidationException($"Deze taak wacht niet op controle (status: {task.Status}).");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        task.Status = target;
        task.ReviewedByUserId = userId;
        task.ReviewedAt = now;
        task.ReviewNote = Trim(request.Note);
        if (request.Approve)
        {
            task.CompletedAt = now;
        }

        task.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, task.Id.ToString(),
            request.Approve ? "ReviewApproved" : "ReviewRejected",
            null, new { task.Title, task.ReviewNote }, cancellationToken);
        await NotifyAssigneeAsync(task,
            request.Approve ? "task_review_approved" : "task_review_rejected",
            request.Approve ? "Taak goedgekeurd" : "Taak afgekeurd",
            request.Approve
                ? $"\"{task.Title}\" is gecontroleerd en goedgekeurd."
                : $"\"{task.Title}\" is afgekeurd: {task.ReviewNote}",
            cancellationToken);
        return await MapOneAsync(task.Id, cancellationToken);
    }

    public async Task<EmployeeTaskDto?> ReopenAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        if (!await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksReopen, cancellationToken))
        {
            throw new DomainValidationException("Alleen gebruikers met heropen-rechten kunnen een afgeronde taak heropenen.");
        }

        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var task = await FindVisibleAsync(id, scope, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (task.Status is not (EmployeeTaskStatus.Completed or EmployeeTaskStatus.Cancelled))
        {
            throw new DomainValidationException("Alleen voltooide of geannuleerde taken kunnen heropend worden.");
        }

        EnsureVersion(task, request.ExpectedVersion);
        task.Status = EmployeeTaskStatus.Todo;
        task.ReopenedAt = _timeProvider.GetUtcNow().UtcDateTime;
        task.CompletedAt = null;
        task.CancelledAt = null;
        task.ReviewedByUserId = null;
        task.ReviewedAt = null;
        task.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, task.Id.ToString(), "Reopened", null,
            new { task.Title, Note = Trim(request.Note) }, cancellationToken);
        await NotifyAssigneeAsync(task, "task_reopened", "Taak heropend",
            $"\"{task.Title}\" is heropend en staat opnieuw open.", cancellationToken);
        return await MapOneAsync(task.Id, cancellationToken);
    }

    public async Task<EmployeeTaskDto?> CancelAsync(Guid id, TaskStatusActionRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var task = await FindVisibleAsync(id, scope, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var isCreator = task.CreatedByUserId == userId;
        var mayCancel = isCreator
            || await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksCancel, cancellationToken);
        if (!mayCancel)
        {
            throw new DomainValidationException("Je kan alleen eigen taken annuleren.");
        }

        EnsureVersion(task, request.ExpectedVersion);
        if (!TaskStatusMachine.CanTransition(task.Status, EmployeeTaskStatus.Cancelled, task.RequiresReview))
        {
            throw new DomainValidationException($"Een taak in status {task.Status} kan niet geannuleerd worden.");
        }

        task.Status = EmployeeTaskStatus.Cancelled;
        task.CancelledAt = _timeProvider.GetUtcNow().UtcDateTime;
        task.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, task.Id.ToString(), "Cancelled", null,
            new { task.Title, Reason = Trim(request.Note) }, cancellationToken);
        await NotifyAssigneeAsync(task, "task_cancelled", "Taak geannuleerd",
            $"\"{task.Title}\" is geannuleerd.", cancellationToken);
        return await MapOneAsync(task.Id, cancellationToken);
    }

    private async Task<EmployeeTaskDto?> TransitionAsync(
        Guid id, int expectedVersion, EmployeeTaskStatus target, string auditAction, string? note,
        CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var task = await FindVisibleAsync(id, scope, cancellationToken);
        if (task is null)
        {
            return null;
        }

        // Executing transitions belongs to the assignee (own task) or a task manager.
        var isOwn = scope.MyEmployeeId is { } myEmployeeId && myEmployeeId == task.AssignedEmployeeId;
        if (!isOwn && !await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksEdit, cancellationToken))
        {
            throw new DomainValidationException("Alleen de toegewezen medewerker kan deze taak uitvoeren.");
        }

        EnsureVersion(task, expectedVersion);
        if (!TaskStatusMachine.CanTransition(task.Status, target, task.RequiresReview))
        {
            throw new DomainValidationException(
                task.Status == EmployeeTaskStatus.InProgress && target == EmployeeTaskStatus.Completed && task.RequiresReview
                    ? "Deze taak vereist controle: dien ze in ter controle in plaats van ze direct te voltooien."
                    : $"Overgang van {task.Status} naar {target} is niet toegestaan.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (target is EmployeeTaskStatus.Completed or EmployeeTaskStatus.WaitingForReview)
        {
            await EnsureCompletionRequirementsAsync(task, note, cancellationToken);
        }

        task.Status = target;
        switch (target)
        {
            case EmployeeTaskStatus.Blocked:
                task.BlockedReason = Trim(note);
                break;
            case EmployeeTaskStatus.InProgress:
                task.BlockedReason = null;
                break;
            case EmployeeTaskStatus.Completed:
                task.CompletedAt = now;
                task.CompletionNote = Trim(note) ?? task.CompletionNote;
                break;
            case EmployeeTaskStatus.WaitingForReview:
                task.CompletionNote = Trim(note) ?? task.CompletionNote;
                break;
        }

        task.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, task.Id.ToString(), auditAction, null,
            new { task.Title, task.Status, Note = Trim(note) }, cancellationToken);

        if (target == EmployeeTaskStatus.Blocked)
        {
            await NotifyCreatorAsync(task, "task_blocked", "Taak geblokkeerd",
                $"\"{task.Title}\" is geblokkeerd: {task.BlockedReason}", cancellationToken);
        }
        else if (target == EmployeeTaskStatus.WaitingForReview)
        {
            await NotifyCreatorAsync(task, "task_waiting_review", "Taak wacht op controle",
                $"\"{task.Title}\" is ingediend ter controle.", cancellationToken);
        }

        return await MapOneAsync(task.Id, cancellationToken);
    }

    /// <summary>Completion note and evidence requirements gate both direct completion and review submission.</summary>
    private async Task EnsureCompletionRequirementsAsync(EmployeeTask task, string? note, CancellationToken cancellationToken)
    {
        if (task.RequiresCompletionNote && string.IsNullOrWhiteSpace(note) && string.IsNullOrWhiteSpace(task.CompletionNote))
        {
            throw new DomainValidationException("note", "Een afrondingsnotitie is verplicht voor deze taak.");
        }

        if (task.RequiresEvidence)
        {
            var hasEvidence = await _dbContext.TaskAttachments.AsNoTracking()
                .AnyAsync(a => a.TenantId == _tenantContext.TenantId && a.TaskId == task.Id, cancellationToken);
            if (!hasEvidence)
            {
                throw new DomainValidationException("Deze taak vereist een bewijsstuk (foto of document) vóór afronding.");
            }
        }
    }

    // ------------------------------------------------------------- queries

    public async Task<TaskListResultDto> ListAsync(TaskListQuery query, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var rows = VisibleTasks(scope);

        if (query.Mine && scope.MyEmployeeId is { } mine)
        {
            rows = rows.Where(t => t.AssignedEmployeeId == mine);
        }

        if (query.CreatedByMe)
        {
            rows = rows.Where(t => t.CreatedByUserId == userId);
        }

        if (query.EmployeeId is { } employeeId)
        {
            rows = rows.Where(t => t.AssignedEmployeeId == employeeId);
        }

        if (query.DepartmentId is { } departmentId)
        {
            var inDepartment = _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == _tenantContext.TenantId && e.DepartmentId == departmentId)
                .Select(e => e.Id);
            rows = rows.Where(t => inDepartment.Contains(t.AssignedEmployeeId));
        }

        if (query.CategoryId is { } categoryId)
        {
            rows = rows.Where(t => t.CategoryId == categoryId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            rows = rows.Where(t => t.Priority == priority);
        }

        if (query.DueBefore is { } dueBefore)
        {
            rows = rows.Where(t => t.DueAt != null && t.DueAt <= dueBefore);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (query.OverdueOnly)
        {
            rows = rows.Where(t => t.DueAt != null && t.DueAt < now
                && TaskStatusMachine.OpenStatuses.Contains(t.Status));
        }

        if (query.WaitingForReviewOnly)
        {
            rows = rows.Where(t => t.Status == EmployeeTaskStatus.WaitingForReview);
        }

        if (!string.IsNullOrWhiteSpace(query.RelatedEntityType))
        {
            rows = rows.Where(t => t.RelatedEntityType == query.RelatedEntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.RelatedEntityId))
        {
            rows = rows.Where(t => t.RelatedEntityId == query.RelatedEntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            rows = rows.Where(t => EF.Functions.Like(t.Title, term)
                || (t.Description != null && EF.Functions.Like(t.Description, term)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var ids = await rows
            .OrderBy(t => t.Status == EmployeeTaskStatus.Completed || t.Status == EmployeeTaskStatus.Cancelled)
            .ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        return new TaskListResultDto(await MapManyAsync(ids, cancellationToken), total, page, pageSize);
    }

    public async Task<EmployeeTaskDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var task = await FindVisibleAsync(id, scope, cancellationToken);
        return task is null ? null : await MapOneAsync(task.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeTaskDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId)
            .Select(e => new { e.Id, e.DepartmentId })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null || !MayViewEmployee(scope, employee.Id, employee.DepartmentId))
        {
            return null;
        }

        var ids = await _dbContext.EmployeeTasks.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.AssignedEmployeeId == employeeId)
            .OrderBy(t => t.Status == EmployeeTaskStatus.Completed || t.Status == EmployeeTaskStatus.Cancelled)
            .ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenByDescending(t => t.CreatedAt)
            .Take(300)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        return await MapManyAsync(ids, cancellationToken);
    }

    public async Task<EmployeeOpenTaskSummaryDto?> OpenSummaryAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId)
            .Select(e => new { e.Id, e.DepartmentId })
            .FirstOrDefaultAsync(cancellationToken);
        if (employee is null || !MayViewEmployee(scope, employee.Id, employee.DepartmentId))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var open = await _dbContext.EmployeeTasks.AsNoTracking()
            .Where(t => t.TenantId == _tenantContext.TenantId && t.AssignedEmployeeId == employeeId
                        && TaskStatusMachine.OpenStatuses.Contains(t.Status))
            .Select(t => new { t.Status, t.DueAt })
            .ToListAsync(cancellationToken);
        return new EmployeeOpenTaskSummaryDto(
            employeeId,
            open.Count(t => t.Status == EmployeeTaskStatus.Todo),
            open.Count(t => t.Status == EmployeeTaskStatus.InProgress),
            open.Count(t => t.Status == EmployeeTaskStatus.Blocked),
            open.Count(t => t.Status == EmployeeTaskStatus.WaitingForReview),
            open.Count(t => t.DueAt is { } due && due < now));
    }

    // ------------------------------------------------------------- redistribution

    public async Task<RedistributeResultDto> RedistributeAsync(RedistributeTasksRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var scope = await ResolveScopeAsync(userId, cancellationToken);
        if (!scope.MayAssign)
        {
            throw new DomainValidationException("Taken herverdelen vereist toewijzingsrechten.");
        }

        var action = request.Action?.Trim().ToLowerInvariant();
        if (action is not ("reassign" or "cancel"))
        {
            throw new DomainValidationException("action", "Actie moet 'reassign' of 'cancel' zijn.");
        }

        var source = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == request.FromEmployeeId)
            .Select(e => new { e.Id, e.DepartmentId, Name = e.FirstName + " " + e.LastName })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainValidationException("fromEmployeeId", "De medewerker bestaat niet.");
        if (!MayViewEmployee(scope, source.Id, source.DepartmentId))
        {
            throw new DomainValidationException("fromEmployeeId", "De medewerker valt buiten je takenscope.");
        }

        string? targetName = null;
        if (action == "reassign")
        {
            if (request.TargetEmployeeId is not { } targetId)
            {
                throw new DomainValidationException("targetEmployeeId", "Kies een medewerker om de taken aan toe te wijzen.");
            }

            var target = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == targetId && e.IsActive)
                .Select(e => new { e.Id, e.DepartmentId, Name = e.FirstName + " " + e.LastName })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new DomainValidationException("targetEmployeeId", "De doelmedewerker bestaat niet of is inactief.");
            await EnsureMayAssignAsync(scope, target.Id, target.DepartmentId, cancellationToken);
            targetName = target.Name;
        }
        else if (!await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksCancel, cancellationToken))
        {
            throw new DomainValidationException("Taken annuleren bij herverdeling vereist annuleringsrechten.");
        }

        var open = await _dbContext.EmployeeTasks
            .Where(t => t.TenantId == _tenantContext.TenantId && t.AssignedEmployeeId == request.FromEmployeeId
                        && TaskStatusMachine.OpenStatuses.Contains(t.Status))
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var task in open)
        {
            if (action == "reassign")
            {
                var oldAssignee = task.AssignedEmployeeId;
                task.AssignedEmployeeId = request.TargetEmployeeId!.Value;
                if (request.NewDueAt is { } newDue)
                {
                    task.DueAt = newDue;
                    task.OverdueNotifiedAt = null;
                    task.DueSoonNotifiedAt = null;
                }

                task.Version++;
                await _auditService.RecordAsync(EntityType, task.Id.ToString(), "Redistributed",
                    new { From = oldAssignee }, new { To = task.AssignedEmployeeId, task.DueAt, request.Reason },
                    cancellationToken);
            }
            else
            {
                task.Status = EmployeeTaskStatus.Cancelled;
                task.CancelledAt = now;
                task.Version++;
                await _auditService.RecordAsync(EntityType, task.Id.ToString(), "CancelledOnRedistribution",
                    null, new { task.Title, request.Reason }, cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (action == "reassign" && open.Count > 0)
        {
            foreach (var task in open)
            {
                await NotifyAssigneeAsync(task, "task_redistributed", "Taak overgenomen",
                    $"Taak \"{task.Title}\" van {source.Name} is aan jou toegewezen ({targetName}).", cancellationToken);
            }
        }

        return new RedistributeResultDto(open.Count, action);
    }

    // ------------------------------------------------------------- scope & helpers

    internal sealed record TaskScope(Guid UserId, Guid? MyEmployeeId, Guid? MyDepartmentId, bool ViewTeam, bool ViewAll, bool MayAssign);

    private async Task<TaskScope> ResolveScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var link = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId)
            .Select(u => new { u.EmployeeId })
            .FirstOrDefaultAsync(cancellationToken);
        Guid? myDepartment = null;
        if (link?.EmployeeId is { } employeeId)
        {
            myDepartment = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == _tenantContext.TenantId && e.Id == employeeId)
                .Select(e => e.DepartmentId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new TaskScope(
            userId,
            link?.EmployeeId,
            myDepartment,
            await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksViewTeam, cancellationToken),
            await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksViewAll, cancellationToken),
            await _permissionService.UserHasPermissionAsync(userId, PermissionCodes.TasksAssign, cancellationToken));
    }

    private IQueryable<EmployeeTask> VisibleTasks(TaskScope scope)
    {
        var tenantId = _tenantContext.TenantId;
        var rows = _dbContext.EmployeeTasks.AsNoTracking().Where(t => t.TenantId == tenantId);
        if (scope.ViewAll)
        {
            return rows;
        }

        var mine = scope.MyEmployeeId ?? Guid.Empty;
        if (scope.ViewTeam && scope.MyDepartmentId is { } department)
        {
            var teamIds = _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.DepartmentId == department)
                .Select(e => e.Id);
            return rows.Where(t => t.AssignedEmployeeId == mine || t.CreatedByUserId == scope.UserId
                || teamIds.Contains(t.AssignedEmployeeId));
        }

        return rows.Where(t => t.AssignedEmployeeId == mine || t.CreatedByUserId == scope.UserId);
    }

    private static bool MayViewEmployee(TaskScope scope, Guid employeeId, Guid? departmentId) =>
        scope.ViewAll
        || scope.MyEmployeeId == employeeId
        || (scope.ViewTeam && scope.MyDepartmentId is { } department && departmentId == department);

    /// <summary>Assigning to yourself needs tasks.manage_own; anyone else needs tasks.assign within scope.</summary>
    private async Task EnsureMayAssignAsync(TaskScope scope, Guid assigneeId, Guid? assigneeDepartmentId, CancellationToken cancellationToken)
    {
        if (scope.MyEmployeeId == assigneeId)
        {
            var maySelf = scope.MayAssign
                || await _permissionService.UserHasPermissionAsync(scope.UserId, PermissionCodes.TasksManageOwn, cancellationToken);
            if (!maySelf)
            {
                throw new DomainValidationException("Je hebt geen rechten om taken aan te maken.");
            }

            return;
        }

        if (!scope.MayAssign)
        {
            throw new DomainValidationException("Taken toewijzen aan anderen vereist toewijzingsrechten.");
        }

        if (!scope.ViewAll)
        {
            if (scope.MyDepartmentId is not { } department || assigneeDepartmentId != department)
            {
                throw new DomainValidationException("assignedEmployeeIds",
                    "Je kan alleen taken toewijzen aan medewerkers binnen je eigen afdeling.");
            }
        }
    }

    private async Task<EmployeeTask?> FindVisibleAsync(Guid id, TaskScope scope, CancellationToken cancellationToken)
    {
        var task = await _dbContext.EmployeeTasks
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (scope.ViewAll || task.AssignedEmployeeId == scope.MyEmployeeId || task.CreatedByUserId == scope.UserId)
        {
            return task;
        }

        if (scope.ViewTeam && scope.MyDepartmentId is { } department)
        {
            var inTeam = await _dbContext.Employees.AsNoTracking()
                .AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == task.AssignedEmployeeId
                               && e.DepartmentId == department, cancellationToken);
            if (inTeam)
            {
                return task;
            }
        }

        return null; // outside scope: indistinguishable from non-existent (404)
    }

    private static void EnsureVersion(EmployeeTask task, int expectedVersion)
    {
        if (task.Version != expectedVersion)
        {
            throw new DomainValidationException(
                "De taak is intussen gewijzigd door iemand anders. Herlaad en probeer opnieuw.");
        }
    }

    private async Task<TaskCategory?> ResolveCategoryAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return null;
        }

        return await _dbContext.TaskCategories
            .FirstOrDefaultAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == id, cancellationToken)
            ?? throw new DomainValidationException("categoryId", "De categorie bestaat niet.");
    }

    private async Task NotifyAssigneeAsync(EmployeeTask task, string type, string title, string message, CancellationToken cancellationToken)
    {
        var assigneeUserId = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.EmployeeId == task.AssignedEmployeeId && u.IsActive)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        await _notificationService.NotifyAsync(assigneeUserId, type, title, message, $"/tasks?taskId={task.Id}", cancellationToken);
    }

    private async Task NotifyCreatorAsync(EmployeeTask task, string type, string title, string message, CancellationToken cancellationToken)
    {
        if (task.CreatedByUserId is { } creatorId && creatorId != _currentUser.CurrentUserId)
        {
            await _notificationService.NotifyAsync(creatorId, type, title, message, $"/tasks?taskId={task.Id}", cancellationToken);
        }
    }

    private Guid RequireUser() =>
        _currentUser.CurrentUserId ?? throw new DomainValidationException("Geen aangemelde gebruiker.");

    private async Task<EmployeeTaskDto> MapOneAsync(Guid id, CancellationToken cancellationToken) =>
        (await MapManyAsync([id], cancellationToken))[0];

    private async Task<IReadOnlyList<EmployeeTaskDto>> MapManyAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var tasks = await _dbContext.EmployeeTasks.AsNoTracking()
            .Where(t => t.TenantId == tenantId && ids.Contains(t.Id))
            .ToListAsync(cancellationToken);
        var employeeIds = tasks.Select(t => t.AssignedEmployeeId).Distinct().ToList();
        var employeeNames = await _dbContext.Employees.AsNoTracking().IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, cancellationToken);
        var userIds = tasks.Where(t => t.CreatedByUserId.HasValue).Select(t => t.CreatedByUserId!.Value).Distinct().ToList();
        var userNames = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FirstName + " " + u.LastName, cancellationToken);
        var categoryIds = tasks.Where(t => t.CategoryId.HasValue).Select(t => t.CategoryId!.Value).Distinct().ToList();
        var categories = await _dbContext.TaskCategories.AsNoTracking().IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Name, c.Color), cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return ids
            .Select(id => tasks.FirstOrDefault(t => t.Id == id))
            .Where(t => t is not null)
            .Select(t => Map(t!, employeeNames, userNames, categories, now))
            .ToList();
    }

    private static EmployeeTaskDto Map(
        EmployeeTask t, IReadOnlyDictionary<Guid, string> employeeNames, IReadOnlyDictionary<Guid, string> userNames,
        IReadOnlyDictionary<Guid, (string Name, string? Color)> categories, DateTime now)
    {
        var category = t.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var found)
            ? found
            : (Name: t.CategorySnapshot ?? "", Color: (string?)null);
        return new EmployeeTaskDto(
            t.Id, t.Title, t.Description, t.CategoryId,
            string.IsNullOrEmpty(category.Name) ? t.CategorySnapshot : category.Name, category.Color,
            t.Priority, t.Status, t.StartAt, t.DueAt, t.CompletedAt, t.CancelledAt,
            t.AssignedEmployeeId, employeeNames.GetValueOrDefault(t.AssignedEmployeeId, "(onbekend)"),
            t.CreatedByUserId, t.CreatedByUserId is { } creator ? userNames.GetValueOrDefault(creator) : null,
            t.RequiresReview, t.RequiresCompletionNote, t.RequiresEvidence,
            t.RelatedEntityType, t.RelatedEntityId,
            t.BlockedReason, t.CompletionNote, t.ReviewNote,
            t.ReviewedByUserId, t.ReviewedAt, t.ReopenedAt,
            IsOverdue: t.DueAt is { } due && due < now && TaskStatusMachine.OpenStatuses.Contains(t.Status),
            t.Version, t.CreatedAt, t.UpdatedAt, t.BatchId, t.RecurrenceId);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
