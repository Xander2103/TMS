using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.Notifications.Services;

/// <summary>
/// Periodic sweep producing expiry notifications for every active tenant. The producer itself
/// deduplicates, so the interval only affects freshness, never volume.
/// </summary>
public sealed class ExpiryNotificationHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiryNotificationHostedService> _logger;

    public ExpiryNotificationHostedService(
        IServiceScopeFactory scopeFactory, ILogger<ExpiryNotificationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First run shortly after startup so a dev environment sees results without waiting.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Expiry notification sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TransportationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var producer = new ExpiryNotificationProducer(dbContext, timeProvider, _logger);
        var hrProducer = new Modules.Hr.Services.HrReminderProducer(dbContext, timeProvider);

        var tenantIds = await dbContext.Tenants.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenantIds)
        {
            await producer.ProduceForTenantAsync(tenantId, cancellationToken);
            await hrProducer.ProduceForTenantAsync(tenantId, cancellationToken);
        }
    }
}
