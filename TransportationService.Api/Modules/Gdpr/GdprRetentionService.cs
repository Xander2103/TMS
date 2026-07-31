using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Messaging.Entities;

namespace TransportationService.Api.Modules.Gdpr;

public sealed record RetentionSweepResult(int OutboxRowsDeleted, int SecurityTokenRowsDeleted, int SinkFilesDeleted)
{
    public static readonly RetentionSweepResult Skipped = new(0, 0, 0);
}

/// <summary>
/// The data-minimisation half of H13: personal data that has served its purpose is actively
/// removed on a schedule instead of accumulating forever. Complements the existing
/// refresh-token retention sweep. Deliberately NOT covered here: audit logs (append-only by
/// construction — maintenance-role operation, checklist #29), invoices/financial records
/// (statutory retention) and uploaded dossier documents (removed via anonymisation/DSR, not by
/// a timer).
/// </summary>
public static class GdprRetentionService
{
    public static async Task<RetentionSweepResult> SweepAsync(
        TransportationDbContext db, TimeProvider clock, RetentionOptions options, string? sinkDirectory,
        CancellationToken cancellationToken)
    {
        if (options.LegalHold)
        {
            return RetentionSweepResult.Skipped;
        }

        var nowUtc = clock.GetUtcNow().UtcDateTime;

        // Delivered (or definitively failed/suppressed) mails: the row itself is transport
        // bookkeeping; recipient address and rendered body are personal data with no further use.
        var outboxCutoff = nowUtc.AddDays(-Math.Max(options.OutboxRetentionDays, 1));
        var outboxDeleted = await db.OutboxMessages
            .IgnoreQueryFilters()
            .Where(m => m.Status != OutboxStatus.Pending && m.CreatedAt < outboxCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Consumed/expired activation- and reset-token rows past the forensic window.
        var tokenCutoff = nowUtc.AddDays(-Math.Max(options.SecurityTokenRetentionDays, 1));
        var tokensDeleted = await db.UserSecurityTokens
            .Where(t => (t.UsedAt != null || t.RevokedAt != null || t.ExpiresAt < nowUtc)
                        && t.CreatedAt < tokenCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Development message-sink files (contain rendered mails, incl. reset links).
        var sinkDeleted = 0;
        if (sinkDirectory is not null && Directory.Exists(sinkDirectory))
        {
            var sinkCutoff = nowUtc.AddDays(-Math.Max(options.SinkFileRetentionDays, 1));
            foreach (var file in Directory.EnumerateFiles(sinkDirectory, "*", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < sinkCutoff)
                {
                    try
                    {
                        File.Delete(file);
                        sinkDeleted++;
                    }
                    catch (IOException)
                    {
                        // In use/locked: next sweep gets it.
                    }
                }
            }
        }

        return new RetentionSweepResult(outboxDeleted, tokensDeleted, sinkDeleted);
    }
}

/// <summary>Runs the retention sweep on an interval, across all tenants (system scope).</summary>
public sealed class GdprRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GdprRetentionHostedService> _logger;

    public GdprRetentionHostedService(
        IServiceScopeFactory scopeFactory, IHostEnvironment environment, ILogger<GdprRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TransportationDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<RetentionOptions>>().Value;
                var sinkDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "message-sink");

                var result = await GdprRetentionService.SweepAsync(db, clock, options, sinkDirectory, stoppingToken);
                if (options.LegalHold)
                {
                    _logger.LogWarning("Retention sweep skipped: legal hold is active.");
                }
                else if (result != RetentionSweepResult.Skipped)
                {
                    _logger.LogInformation(
                        "Retention sweep: {Outbox} outbox rows, {Tokens} security-token rows, {Sink} sink files removed.",
                        result.OutboxRowsDeleted, result.SecurityTokenRowsDeleted, result.SinkFilesDeleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Retention sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
