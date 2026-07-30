using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication.Entities;

namespace TransportationService.Api.Modules.Authentication.Services;

/// <summary>
/// Periodically removes refresh tokens that are past their usefulness: expired ones, and revoked
/// ones older than the retention window (kept briefly so reuse detection and forensics still have
/// the lineage). Runs across all tenants; the rows carry their own TenantId.
/// </summary>
public sealed class TokenRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenRetentionHostedService> _logger;

    public TokenRetentionHostedService(IServiceScopeFactory scopeFactory, ILogger<TokenRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TransportationDbContext>();
                var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<AuthenticationSecurityOptions>>().Value;

                var removed = await PurgeAsync(db, clock, options.RefreshTokenRetentionDays, stoppingToken);
                if (removed > 0)
                {
                    _logger.LogInformation("Refresh-token retention sweep removed {Count} rows.", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Refresh-token retention sweep failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes expired tokens and revoked tokens older than the retention window.</summary>
    public static async Task<int> PurgeAsync(
        TransportationDbContext db, TimeProvider clock, int retentionDays, CancellationToken cancellationToken)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var cutoff = nowUtc.AddDays(-Math.Max(retentionDays, 1));

        return await db.Set<RefreshToken>()
            .Where(t => t.ExpiresAt < nowUtc || (t.RevokedAt != null && t.RevokedAt < cutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
