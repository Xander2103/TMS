using Microsoft.Extensions.Options;
using TransportationService.Api.Modules.SystemAdmin.Services;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

/// <summary>
/// Daily automatic database backup + retention sweep (settings/system wave 2026-08).
/// Fires once per day at the configured UTC hour; disabled via Backups:AutomaticEnabled.
/// Failures are logged and retried the next day — an unattended backup problem must
/// surface in the Systeeminformatie screen, never crash the host.
/// </summary>
public class AutomaticBackupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutomaticBackupHostedService> _logger;
    private readonly BackupOptions _options;

    public AutomaticBackupHostedService(
        IServiceScopeFactory scopeFactory, ILogger<AutomaticBackupHostedService> logger,
        IOptions<BackupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutomaticEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddHours(_options.AutomaticHourUtc);
            if (next <= now)
            {
                next = next.AddDays(1);
            }

            try
            {
                await Task.Delay(next - now, stoppingToken);
                using var scope = _scopeFactory.CreateScope();
                var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
                await backups.CreateAsync("Automatic", "Dagelijkse automatische back-up", stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatische back-up mislukt; volgende poging morgen.");
            }
        }
    }
}
