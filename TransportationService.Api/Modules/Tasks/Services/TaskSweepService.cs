using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Tasks.Services;

/// <summary>
/// 15-minute tenant-aware task sweep: generates due recurrences, announces due-soon/overdue
/// tasks (one-shot stamps), releases future-visible internal messages, and raises the task/
/// acknowledgement escalations. Dedupe keys + stamps make every step idempotent.
/// </summary>
public sealed class TaskSweepHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TaskSweepHostedService> _logger;

    public TaskSweepHostedService(IServiceScopeFactory scopeFactory, ILogger<TaskSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<TaskSweepWorker>();
                await worker.SweepAllTenantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Task sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class TaskSweepWorker
{
    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TaskSweepWorker> _logger;

    public TaskSweepWorker(TransportationDbContext dbContext, TimeProvider timeProvider, ILogger<TaskSweepWorker> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task SweepAllTenantsAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
        {
            try
            {
                await SweepTenantAsync(tenantId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Task sweep failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    public async Task SweepTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = new DevTenantContext(tenantId);
        var notifications = new NotificationService(_dbContext, tenant, new DevCurrentUserContext(null), _timeProvider);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var generated = await new TaskRecurrenceGenerator(_dbContext, tenant, notifications)
            .GenerateDueAsync(DateOnly.FromDateTime(now), cancellationToken);
        if (generated > 0)
        {
            _logger.LogInformation("Generated {Count} recurring task(s) for tenant {TenantId}.", generated, tenantId);
        }

        await AnnounceDueTasksAsync(tenantId, notifications, now, cancellationToken);
        await ReleaseScheduledMessagesAsync(tenantId, notifications, now, cancellationToken);
        await EscalateAsync(tenantId, notifications, now, cancellationToken);
    }

    private async Task AnnounceDueTasksAsync(
        Guid tenantId, NotificationService notifications, DateTime now, CancellationToken cancellationToken)
    {
        var soon = now.AddHours(24);
        var open = await _dbContext.EmployeeTasks
            .Where(t => t.TenantId == tenantId && t.DueAt != null
                        && TaskStatusMachine.OpenStatuses.Contains(t.Status)
                        && ((t.DueAt <= soon && t.DueAt > now && t.DueSoonNotifiedAt == null)
                            || (t.DueAt <= now && t.OverdueNotifiedAt == null)))
            .ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return;
        }

        var employeeIds = open.Select(t => t.AssignedEmployeeId).Distinct().ToList();
        var userByEmployee = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.EmployeeId != null && employeeIds.Contains(u.EmployeeId.Value) && u.IsActive)
            .ToDictionaryAsync(u => u.EmployeeId!.Value, u => u.Id, cancellationToken);

        foreach (var task in open)
        {
            var assigneeUserId = userByEmployee.TryGetValue(task.AssignedEmployeeId, out var userId) ? userId : (Guid?)null;
            if (task.DueAt <= now)
            {
                await notifications.NotifyAsync(assigneeUserId, "task_overdue", "Taak achterstallig",
                    $"\"{task.Title}\" is over de deadline ({task.DueAt:dd/MM/yyyy HH:mm}).",
                    $"/tasks?taskId={task.Id}", cancellationToken,
                    new NotificationOptions(DedupeKey: $"task_overdue:{task.Id}"));
                if (task.CreatedByUserId is { } creatorId && creatorId != assigneeUserId)
                {
                    await notifications.NotifyAsync(creatorId, "task_overdue", "Taak achterstallig",
                        $"\"{task.Title}\" ({task.CategorySnapshot ?? "taak"}) is over de deadline.",
                        $"/tasks?taskId={task.Id}", cancellationToken,
                        new NotificationOptions(DedupeKey: $"task_overdue:{task.Id}"));
                }

                task.OverdueNotifiedAt = now;
            }
            else
            {
                await notifications.NotifyAsync(assigneeUserId, "task_due_soon", "Taak bijna vervallen",
                    $"\"{task.Title}\" vervalt op {task.DueAt:dd/MM/yyyy HH:mm}.",
                    $"/tasks?taskId={task.Id}", cancellationToken,
                    new NotificationOptions(DedupeKey: $"task_due_soon:{task.Id}"));
                task.DueSoonNotifiedAt = now;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Future-visible internal messages become visible: announce them exactly once.</summary>
    private async Task ReleaseScheduledMessagesAsync(
        Guid tenantId, NotificationService notifications, DateTime now, CancellationToken cancellationToken)
    {
        var due = await _dbContext.Set<InternalMessage>()
            .Where(m => m.TenantId == tenantId && m.NotifiedAt == null && m.CancelledAt == null
                        && m.VisibleFrom != null && m.VisibleFrom <= now
                        && (m.ExpiresAt == null || m.ExpiresAt > now))
            .ToListAsync(cancellationToken);
        foreach (var message in due)
        {
            var recipients = await _dbContext.Set<InternalMessageRecipient>().AsNoTracking()
                .Where(r => r.TenantId == tenantId && r.MessageId == message.Id)
                .Select(r => r.UserId)
                .ToListAsync(cancellationToken);
            foreach (var userId in recipients)
            {
                await notifications.NotifyAsync(userId, "internal_message",
                    $"Nieuw bericht: {message.Subject}",
                    message.RequiresAcknowledgement
                        ? "Je hebt een nieuw intern bericht dat bevestiging vereist."
                        : "Je hebt een nieuw intern bericht ontvangen.",
                    "/inbox", cancellationToken,
                    new NotificationOptions(DedupeKey: $"internal_message_release:{message.Id}:{userId}"));
            }

            message.NotifiedAt = now;
        }

        if (due.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EscalateAsync(
        Guid tenantId, NotificationService notifications, DateTime now, CancellationToken cancellationToken)
    {
        var policies = await _dbContext.Set<EscalationPolicy>().AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToDictionaryAsync(p => p.Kind, cancellationToken);
        foreach (var (kind, defaults) in EscalationPolicyService.Defaults)
        {
            policies.TryAdd(kind, new EscalationPolicy
            {
                TenantId = tenantId, Kind = kind, DelayHours = defaults.DelayHours,
                TargetPermissionCode = defaults.Target, IsActive = defaults.Active,
            });
        }

        if (policies[EscalationKind.TaskOverdue] is { IsActive: true } taskPolicy)
        {
            var cutoff = now.AddHours(-taskPolicy.DelayHours);
            var stale = await _dbContext.EmployeeTasks.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.DueAt != null && t.DueAt <= cutoff
                            && TaskStatusMachine.OpenStatuses.Contains(t.Status))
                .ToListAsync(cancellationToken);
            var employeeIds = stale.Select(t => t.AssignedEmployeeId).Distinct().ToList();
            var names = await _dbContext.Employees.AsNoTracking()
                .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, cancellationToken);
            foreach (var task in stale)
            {
                await notifications.NotifyPermissionHoldersAsync(
                    taskPolicy.TargetPermissionCode, "escalation_raised", "Escalatie: taak achterstallig",
                    $"\"{task.Title}\" van {names.GetValueOrDefault(task.AssignedEmployeeId, "?")} is al langer dan {taskPolicy.DelayHours} uur over de deadline.",
                    $"/tasks?taskId={task.Id}", cancellationToken,
                    new NotificationOptions(DedupeKey: $"escalation:task_overdue:{task.Id}"));
            }
        }

        if (policies[EscalationKind.AcknowledgementMissing] is { IsActive: true } ackPolicy)
        {
            var cutoff = now.AddHours(-ackPolicy.DelayHours);
            var pending = await _dbContext.Set<InternalMessage>().AsNoTracking()
                .Where(m => m.TenantId == tenantId && m.RequiresAcknowledgement && m.CancelledAt == null
                            && m.CreatedAt <= cutoff)
                .Select(m => new
                {
                    m.Id, m.Subject,
                    Missing = _dbContext.Set<InternalMessageRecipient>()
                        .Count(r => r.MessageId == m.Id && r.AcknowledgedAt == null),
                })
                .Where(m => m.Missing > 0)
                .ToListAsync(cancellationToken);
            foreach (var message in pending)
            {
                await notifications.NotifyPermissionHoldersAsync(
                    ackPolicy.TargetPermissionCode, "escalation_raised", "Escalatie: bevestiging ontbreekt",
                    $"Bericht \"{message.Subject}\" wacht al langer dan {ackPolicy.DelayHours} uur op {message.Missing} bevestiging(en).",
                    "/inbox", cancellationToken,
                    new NotificationOptions(DedupeKey: $"escalation:ack:{message.Id}"));
            }
        }
    }
}
