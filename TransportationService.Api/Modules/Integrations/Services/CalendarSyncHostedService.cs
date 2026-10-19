using TransportationService.Api.Common.Jobs;

namespace TransportationService.Api.Modules.Integrations.Services;

/// <summary>Sweeps the calendar sync queue every 60 seconds; the processor owns retry timing.</summary>
public sealed class CalendarSyncHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CalendarSyncHostedService> _logger;

    public CalendarSyncHostedService(
        IServiceScopeFactory scopeFactory, ILogger<CalendarSyncHostedService> logger)
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
                await _scopeFactory.RunJobAsync(JobNames.CalendarSync, (services, token) =>
                    services.GetRequiredService<CalendarSyncProcessor>().ProcessPendingAsync(50, token), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Calendar sync run failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
