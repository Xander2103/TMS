using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tasks.Services;

/// <summary>
/// Materialises due recurrences into concrete tasks for ONE tenant. Constructed by the task
/// sweep with a DevTenantContext (fail-closed request accessors don't exist in a job scope).
/// Idempotent: every generated task carries a per-(recurrence, period, item) dedupe key that
/// is unique per tenant — re-running a period, retries and races can never duplicate tasks.
/// Generated tasks snapshot the template content; later template edits leave history alone.
/// </summary>
public class TaskRecurrenceGenerator
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationService _notificationService;

    public TaskRecurrenceGenerator(TransportationDbContext dbContext, ITenantContext tenantContext,
        INotificationService notificationService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notificationService = notificationService;
    }

    public async Task<int> GenerateDueAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var recurrences = await _dbContext.TaskRecurrences
            .Where(r => r.TenantId == tenantId && r.IsActive && r.StartDate <= today
                        && (r.EndDate == null || r.EndDate >= today))
            .ToListAsync(cancellationToken);
        if (recurrences.Count == 0)
        {
            return 0;
        }

        var created = 0;
        foreach (var recurrence in recurrences)
        {
            var periodStart = PeriodStartFor(recurrence, today);
            if (periodStart is not { } period || period < recurrence.StartDate)
            {
                continue;
            }

            if (recurrence.LastGeneratedPeriod == period)
            {
                continue; // fast path; the dedupe key below stays authoritative
            }

            var items = await _dbContext.TaskTemplateItems.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.TemplateId == recurrence.TemplateId)
                .OrderBy(i => i.SortOrder)
                .ToListAsync(cancellationToken);
            if (items.Count == 0)
            {
                continue;
            }

            var templateActive = await _dbContext.TaskTemplates.AsNoTracking()
                .AnyAsync(t => t.TenantId == tenantId && t.Id == recurrence.TemplateId && t.IsActive, cancellationToken);
            if (!templateActive)
            {
                continue;
            }

            var employeeActive = await _dbContext.Employees.AsNoTracking()
                .AnyAsync(e => e.TenantId == tenantId && e.Id == recurrence.AssignedEmployeeId && e.IsActive, cancellationToken);
            if (!employeeActive)
            {
                continue; // inactive employees never receive generated work (redistribution handles the backlog)
            }

            var keys = items.Select(i => DedupeKey(recurrence.Id, period, i.Id)).ToList();
            var existing = (await _dbContext.EmployeeTasks.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.RecurrenceDedupeKey != null && keys.Contains(t.RecurrenceDedupeKey))
                    .Select(t => t.RecurrenceDedupeKey!)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var categoryNames = await _dbContext.TaskCategories.AsNoTracking()
                .Where(c => c.TenantId == tenantId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
            var generatedNow = new List<EmployeeTask>();
            foreach (var item in items)
            {
                var key = DedupeKey(recurrence.Id, period, item.Id);
                if (existing.Contains(key))
                {
                    continue;
                }

                var periodMidnight = period.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var task = new EmployeeTask
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Title = item.Title,
                    Description = item.Description,
                    CategoryId = item.CategoryId,
                    CategorySnapshot = item.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
                    Priority = item.Priority,
                    StartAt = periodMidnight,
                    DueAt = item.DueInDays is { } days ? periodMidnight.AddDays(days) : null,
                    AssignedEmployeeId = recurrence.AssignedEmployeeId,
                    RequiresReview = item.RequiresReview,
                    RequiresCompletionNote = item.RequiresCompletionNote,
                    RequiresEvidence = item.RequiresEvidence,
                    RelatedEntityType = "task_recurrence",
                    RelatedEntityId = recurrence.Id.ToString(),
                    RecurrenceId = recurrence.Id,
                    RecurrenceDedupeKey = key,
                };
                _dbContext.Add(task);
                generatedNow.Add(task);
            }

            recurrence.LastGeneratedPeriod = period;
            await _dbContext.SaveChangesAsync(cancellationToken);
            created += generatedNow.Count;

            if (generatedNow.Count > 0)
            {
                var assigneeUserId = await _dbContext.Users.AsNoTracking()
                    .Where(u => u.TenantId == tenantId && u.EmployeeId == recurrence.AssignedEmployeeId && u.IsActive)
                    .Select(u => (Guid?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                foreach (var task in generatedNow)
                {
                    await _notificationService.NotifyAsync(assigneeUserId, "task_assigned", "Nieuwe taak",
                        $"Terugkerende taak \"{task.Title}\" staat klaar{(task.DueAt is { } due ? $" (deadline {due:dd/MM/yyyy})" : "")}.",
                        $"/tasks?taskId={task.Id}", cancellationToken);
                }
            }
        }

        return created;
    }

    public static string DedupeKey(Guid recurrenceId, DateOnly period, Guid itemId) =>
        $"recurrence:{recurrenceId}:{period:yyyyMMdd}:{itemId}";

    /// <summary>Current period start for the given day (Weekly = ISO Monday, Monthly = day 1,
    /// Yearly = 1 January, CustomDays = StartDate + ⌊days/interval⌋·interval).</summary>
    public static DateOnly? PeriodStartFor(TaskRecurrence recurrence, DateOnly today) => recurrence.Interval switch
    {
        TaskRecurrenceInterval.Daily => today,
        TaskRecurrenceInterval.Weekly => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
        TaskRecurrenceInterval.Monthly => new DateOnly(today.Year, today.Month, 1),
        TaskRecurrenceInterval.Yearly => new DateOnly(today.Year, 1, 1),
        TaskRecurrenceInterval.CustomDays when recurrence.CustomIntervalDays is { } interval and > 0 =>
            recurrence.StartDate.AddDays((today.DayNumber - recurrence.StartDate.DayNumber) / interval * interval),
        _ => null,
    };
}
