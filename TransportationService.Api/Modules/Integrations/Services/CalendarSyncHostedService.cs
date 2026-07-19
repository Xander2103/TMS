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
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<CalendarSyncProcessor>();
                await processor.ProcessPendingAsync(50, stoppingToken);
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
