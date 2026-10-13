using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Notifications.Entities;

namespace TransportationService.Api.Modules.Notifications.Services;

/// <summary>
/// Six-hourly notification housekeeping, row-driven and tenant-agnostic (every row carries
/// its TenantId): expired notifications are archived, and archived/read notifications older
/// than the retention window are soft-deleted in bounded batches.
/// </summary>
public sealed class NotificationMaintenanceHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationMaintenanceHostedService> _logger;

    public NotificationMaintenanceHostedService(
        IServiceScopeFactory scopeFactory, ILogger<NotificationMaintenanceHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<NotificationMaintenanceWorker>();
                await worker.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Notification maintenance failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class NotificationMaintenanceWorker
{
    public static readonly TimeSpan RetentionAfterArchive = TimeSpan.FromDays(180);
    private const int BatchSize = 500;

    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public NotificationMaintenanceWorker(TransportationDbContext dbContext, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<(int Archived, int Purged)> RunAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var expired = await _dbContext.Notifications
            .Where(n => !n.IsArchived && n.ExpiresAt != null && n.ExpiresAt <= now)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var notification in expired)
        {
            notification.IsArchived = true;
            notification.ArchivedAt = now;
        }

        var purgeCutoff = now - RetentionAfterArchive;
        var purgeable = await _dbContext.Notifications
            .Where(n => n.IsArchived && n.ArchivedAt != null && n.ArchivedAt <= purgeCutoff)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        _dbContext.RemoveRange(purgeable); // soft delete via the interceptor

        if (expired.Count > 0 || purgeable.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return (expired.Count, purgeable.Count);
    }
}
