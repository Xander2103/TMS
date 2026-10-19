using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

/// <summary>
/// H4/BG-03 monitoring signal (tag "backup", deliberately NOT "ready": a stale backup must page an
/// operator, not take the API out of rotation). Unhealthy when no verified in-app backup set exists
/// within <see cref="BackupOptions.MaxAgeHours"/>; Degraded when the newest attempt failed after the
/// last success. Data carries the timestamps only — no paths, no file names.
/// </summary>
public sealed class BackupFreshnessHealthCheck : IHealthCheck
{
    private readonly TransportationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly BackupOptions _options;

    public BackupFreshnessHealthCheck(
        TransportationDbContext dbContext, TimeProvider timeProvider, IOptions<BackupOptions> options)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public static bool IsStale(DateTime? lastSuccessfulUtc, DateTime nowUtc, BackupOptions options) =>
        lastSuccessfulUtc is not { } last || nowUtc - last > TimeSpan.FromHours(Math.Max(1, options.MaxAgeHours));

    // Stored timestamps are UTC by convention; providers may hand them back as Unspecified.
    private static string AsUtcText(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("O") : "none";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        DateTime? lastSuccess;
        DateTime? lastFailure;
        try
        {
            lastSuccess = await _dbContext.DatabaseBackups.AsNoTracking()
                .Where(b => b.Status == "Completed" || b.Status == "Restored")
                .MaxAsync(b => (DateTime?)b.CreatedAtUtc, cancellationToken);
            lastFailure = await _dbContext.DatabaseBackups.AsNoTracking()
                .Where(b => b.Status == "Failed")
                .MaxAsync(b => (DateTime?)b.CreatedAtUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"Backup status unavailable ({ex.GetType().Name}).");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var data = new Dictionary<string, object>
        {
            ["lastSuccessfulBackupUtc"] = AsUtcText(lastSuccess),
            ["lastFailedBackupUtc"] = AsUtcText(lastFailure),
            ["maxAgeHours"] = _options.MaxAgeHours,
        };
        if (lastSuccess is { } success)
        {
            data["ageHours"] = Math.Round((now - success).TotalHours, 1);
        }

        if (lastSuccess is null)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                "No successful in-app backup has been recorded.", data: data);
        }

        if (IsStale(lastSuccess, now, _options))
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"Last successful backup is older than {_options.MaxAgeHours} h.", data: data);
        }

        return lastFailure > lastSuccess
            ? HealthCheckResult.Degraded("The most recent backup attempt failed.", data: data)
            : HealthCheckResult.Healthy("Recent verified backup present.", data);
    }
}
