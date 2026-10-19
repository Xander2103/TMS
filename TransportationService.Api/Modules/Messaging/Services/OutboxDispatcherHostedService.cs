using TransportationService.Api.Common.Jobs;

namespace TransportationService.Api.Modules.Messaging.Services;

/// <summary>Drains the message outbox every 30 seconds; the dispatcher owns retry timing.</summary>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherHostedService> _logger;

    public OutboxDispatcherHostedService(
        IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                // H2: rows are claimed one by one (multi-instance safe without a global lock);
                // the run is only tracked for the /health/details heartbeat.
                await _scopeFactory.RunTrackedJobAsync(JobNames.OutboxDispatch, (services, token) =>
                    services.GetRequiredService<MessageDispatcher>().DispatchPendingAsync(50, token), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox dispatch run failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
