using TransportationService.Api.Common.Jobs;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Employees.Services;

/// <summary>
/// MD-H6 follow-up: a future employment end date ends access once the last working day has
/// passed. State-driven and restart-safe — every tick re-reads the database (no in-memory timer
/// or schedule): an employee whose end date lies behind the tenant-local today is offboarded
/// through the ONE <see cref="EmployeeOffboardingService"/>. Login, refresh and every request
/// already refuse such a user in the meantime (<see cref="EmploymentAccess"/>), so the interval
/// only bounds how long the cascade (driver, vehicle slots, attendance) lags behind.
/// </summary>
public sealed class EmploymentEndSweepHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmploymentEndSweepHostedService> _logger;

    public EmploymentEndSweepHostedService(IServiceScopeFactory scopeFactory, ILogger<EmploymentEndSweepHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                // H7: one instance per run (advisory lock); heartbeat in /health/details.
                await _scopeFactory.RunJobAsync(JobNames.EmploymentEndSweep, (services, token) =>
                    services.GetRequiredService<EmploymentEndSweepWorker>().SweepAllTenantsAsync(token), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Employment-end sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class EmploymentEndSweepWorker
{
    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmploymentEndSweepWorker> _logger;

    public EmploymentEndSweepWorker(TransportationDbContext dbContext, TimeProvider timeProvider, ILogger<EmploymentEndSweepWorker> logger)
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
                _logger.LogError(exception, "Employment-end sweep failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    /// <summary>Offboards every employment of the tenant that has ended but still holds access. Returns how many.</summary>
    public async Task<int> SweepTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var today = await TenantTimeZone.TodayAsync(_dbContext, tenantId, _timeProvider, cancellationToken);
        var db = _dbContext;
        var candidates = await db.Employees
            .Where(e => e.TenantId == tenantId)
            .Where(e => (e.IsActive && (e.EmploymentStatus == EmploymentStatus.Terminated
                                         || (e.EmploymentEndDate != null && e.EmploymentEndDate < today)))
                        // Legacy rows (before MD-H6): employment ended, account / driver left on.
                        || (!e.IsActive && (db.Users.Any(u => u.TenantId == tenantId && u.EmployeeId == e.Id && u.IsActive)
                                            || db.Drivers.Any(d => d.TenantId == tenantId && d.EmployeeId == e.Id && d.IsActive))))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var tenant = new DevTenantContext(tenantId);
        var offboarding = new EmployeeOffboardingService(_dbContext, tenant,
            new AuditService(_dbContext, tenant, new DevCurrentUserContext(null)), _timeProvider);
        var done = 0;
        foreach (var employeeId in candidates)
        {
            _dbContext.ChangeTracker.Clear();
            var employee = await db.Employees.FirstAsync(e => e.Id == employeeId && e.TenantId == tenantId, cancellationToken);
            var trigger = employee.IsActive ? OffboardingTrigger.EmploymentEndDatePassed : OffboardingTrigger.ResidualAccess;
            try
            {
                await offboarding.OffboardAsync(employee, trigger, cancellationToken);
                done++;
            }
            catch (Common.DomainValidationException refused)
            {
                // The tenant's last administrator: never locked out automatically. The request-time
                // check still refuses the ended employment; HR / admin resolves it explicitly.
                _logger.LogWarning("Employment-end sweep skipped employee {EmployeeId} in tenant {TenantId}: {Reason}",
                    employeeId, tenantId, refused.Message);
            }
        }

        if (done > 0)
        {
            _logger.LogInformation("Employment-end sweep offboarded {Count} employee(s) for tenant {TenantId}.", done, tenantId);
        }

        _dbContext.ChangeTracker.Clear();
        return done;
    }
}
