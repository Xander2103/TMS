using TransportationService.Api.Common.Jobs;
using Microsoft.Extensions.Options;
using TransportationService.Api.Modules.SystemAdmin.Services;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

/// <summary>
/// Daily automatic database backup + retention sweep (settings/system wave 2026-08).
/// Fires once per day at the configured UTC hour; disabled via Backups:AutomaticEnabled.
/// Failures are logged (failing part + tool stderr), recorded as a Failed row and retried the
/// next day — an unattended backup problem must surface in the Systeeminformatie screen and the
/// "backup" health check, never crash the host. Only a real shutdown ends the loop (BG-03).
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
                // H7: a second instance skips the 02:00 dump instead of running pg_dump twice.
                await _scopeFactory.RunJobAsync(JobNames.AutomaticBackup, (services, token) =>
                    services.GetRequiredService<IBackupService>().CreateAsync("Automatic", "Dagelijkse automatische back-up", token),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
