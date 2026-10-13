using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

/// <summary>
/// Periodieke attendance-sweep (kwartier): detecteert "mogelijk vergeten uit te punten"
/// en voert — uitsluitend wanneer de tenant dat expliciet inschakelt — auto-close uit.
/// Default is waarschuwen, nooit stilletjes afsluiten (spec §22/§23).
/// </summary>
public sealed class AttendanceSweepHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AttendanceSweepHostedService> _logger;

    public AttendanceSweepHostedService(IServiceScopeFactory scopeFactory, ILogger<AttendanceSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<AttendanceSweepWorker>();
                await worker.SweepAllTenantsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Attendance sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Tenant-safe sweep-worker. Per tenant bounded queries; één melding per sessie via de
/// one-shot-stempel ForgottenClockOutNotifiedAt plus notificatie-dedupekeys. Logging
/// zonder persoonsgegevens (alleen id's/aantallen). Auto-close laat altijd een
/// AutoClosed-event, status en auditrij achter — geen verborgen correcties.
/// </summary>
public sealed class AttendanceSweepWorker
{
    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AttendanceSweepWorker> _logger;

    public AttendanceSweepWorker(TransportationDbContext dbContext, TimeProvider timeProvider, ILogger<AttendanceSweepWorker> logger)
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
                _logger.LogError(exception, "Attendance sweep failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    public async Task SweepTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings.AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .Select(s => new { s.ForgottenClockOutAfterHours, s.AutoCloseEnabled, s.AutoCloseAfterHours })
            .FirstOrDefaultAsync(cancellationToken);
        var forgottenAfter = TimeSpan.FromHours(settings?.ForgottenClockOutAfterHours ?? 16);
        var autoCloseEnabled = settings?.AutoCloseEnabled ?? false;
        var autoCloseAfter = TimeSpan.FromHours(Math.Max(settings?.AutoCloseAfterHours ?? 18,
            settings?.ForgottenClockOutAfterHours ?? 16));

        var tenant = new DevTenantContext(tenantId);
        var notifications = new NotificationService(_dbContext, tenant, new DevCurrentUserContext(null), _timeProvider);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await WarnForgottenClockOutsAsync(tenantId, notifications, now, forgottenAfter, cancellationToken);
        if (autoCloseEnabled)
        {
            await AutoCloseAsync(tenantId, tenant, notifications, now, autoCloseAfter, cancellationToken);
        }
    }

    private async Task WarnForgottenClockOutsAsync(
        Guid tenantId, NotificationService notifications, DateTime now, TimeSpan forgottenAfter,
        CancellationToken cancellationToken)
    {
        var threshold = now - forgottenAfter;
        var stale = await _dbContext.AttendanceSessions
            .Where(s => s.TenantId == tenantId
                        && s.ClockOutAt == null
                        && (s.Status == AttendanceSessionStatus.Working || s.Status == AttendanceSessionStatus.OnBreak)
                        && s.ClockInAt <= threshold
                        && s.ForgottenClockOutNotifiedAt == null)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        var employeeIds = stale.Select(s => s.EmployeeId).Distinct().ToList();
        var employeeNames = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => $"{e.FirstName} {e.LastName}".Trim(), cancellationToken);
        var userByEmployee = await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.EmployeeId != null
                        && employeeIds.Contains(u.EmployeeId.Value) && u.IsActive)
            .ToDictionaryAsync(u => u.EmployeeId!.Value, u => u.Id, cancellationToken);

        foreach (var session in stale)
        {
            var name = employeeNames.TryGetValue(session.EmployeeId, out var n) ? n : "Onbekende medewerker";
            var since = session.ClockInAt.ToString("dd/MM/yyyy HH:mm");

            if (userByEmployee.TryGetValue(session.EmployeeId, out var employeeUserId))
            {
                await notifications.NotifyAsync(employeeUserId, "attendance_forgotten_clockout",
                    "Vergeten uit te punten?",
                    $"Je staat nog ingepunt sinds {since} (UTC). Punt uit of vraag HR om een correctie.",
                    "/portal/time", cancellationToken,
                    new NotificationOptions(DedupeKey: $"attendance_forgotten_clockout:{session.Id}:self"));
            }

            await notifications.NotifyPermissionHoldersAsync(PermissionCodes.AttendanceCorrect,
                "attendance_forgotten_clockout",
                "Mogelijk vergeten uit te punten",
                $"{name} staat nog ingepunt sinds {since} (UTC).",
                $"/attendance?employeeId={session.EmployeeId}", cancellationToken,
                new NotificationOptions(DedupeKey: $"attendance_forgotten_clockout:{session.Id}"));

            session.ForgottenClockOutNotifiedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Attendance sweep: {Count} mogelijk vergeten uitpunt(en) gemeld voor tenant {TenantId}.",
            stale.Count, tenantId);
    }

    private async Task AutoCloseAsync(
        Guid tenantId, DevTenantContext tenant, NotificationService notifications, DateTime now,
        TimeSpan autoCloseAfter, CancellationToken cancellationToken)
    {
        var threshold = now - autoCloseAfter;
        var toClose = await _dbContext.AttendanceSessions
            .Where(s => s.TenantId == tenantId
                        && s.ClockOutAt == null
                        && (s.Status == AttendanceSessionStatus.Working || s.Status == AttendanceSessionStatus.OnBreak)
                        && s.ClockInAt <= threshold)
            .ToListAsync(cancellationToken);
        if (toClose.Count == 0)
        {
            return;
        }

        var sessionIds = toClose.Select(s => s.Id).ToList();
        var openBreaks = await _dbContext.AttendanceBreaks
            .Where(b => b.TenantId == tenantId && sessionIds.Contains(b.SessionId) && b.EndedAt == null)
            .ToListAsync(cancellationToken);
        var auditService = new AuditService(_dbContext, tenant, new DevCurrentUserContext(null));

        foreach (var session in toClose)
        {
            var openBreak = openBreaks.FirstOrDefault(b => b.SessionId == session.Id);
            if (openBreak is not null)
            {
                openBreak.EndedAt = now;
            }

            session.ClockOutAt = now;
            session.ClockOutSource = AttendanceSource.Api;
            session.Status = AttendanceSessionStatus.AutoClosed;
            _dbContext.AttendanceEvents.Add(new AttendanceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                SessionId = session.Id,
                EmployeeId = session.EmployeeId,
                EventType = AttendanceEventType.AutoClosed,
                OccurredAt = now,
                Source = AttendanceSource.Api,
                Note = "Automatisch afgesloten door het systeem (auto-close-instelling).",
            });

            await auditService.RecordAsync("AttendanceSession", session.Id.ToString(), "AutoClosed",
                new { session.ClockInAt, ClockOutAt = (DateTime?)null },
                new { session.ClockOutAt, session.Status }, cancellationToken);

            await notifications.ResolveByDedupeKeyAsync($"attendance_forgotten_clockout:{session.Id}", cancellationToken);
            await notifications.NotifyPermissionHoldersAsync(PermissionCodes.AttendanceCorrect,
                "attendance_auto_closed",
                "Registratie automatisch afgesloten",
                "Een openstaande registratie is automatisch afgesloten en vraagt om controle/correctie.",
                $"/attendance?employeeId={session.EmployeeId}", cancellationToken,
                new NotificationOptions(DedupeKey: $"attendance_auto_closed:{session.Id}"));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Attendance sweep: {Count} sessie(s) automatisch afgesloten voor tenant {TenantId}.",
            toClose.Count, tenantId);
    }
}
