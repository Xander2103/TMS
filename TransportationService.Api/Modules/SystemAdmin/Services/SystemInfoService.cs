using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;

namespace TransportationService.Api.Modules.SystemAdmin.Services;

public record SystemInfoDto(
    string Version,
    string? BuildCommit,
    string Environment,
    /// <summary>From the deployment metadata of the LIVE release when present; else the
    /// binary's write time. Never blindly "git HEAD" — a checked-out candidate commit is
    /// not what runs until it is actually published.</summary>
    DateTime? LastDeployedAtUtc,
    string ApiStatus,
    string DatabaseStatus,
    long? DatabaseLatencyMs,
    string? SchemaVersion,
    int PendingMigrations,
    string? DeploymentRef);

/// <summary>
/// Deployment metadata written by scripts/deploy-transportationservice.sh next to the
/// published binaries (deployment.json). Its presence means "this is what is LIVE"; a repo
/// checkout on a newer commit is merely a candidate.
/// </summary>
public sealed record DeploymentMetadata(
    string? Version, string? Commit, string? Ref, DateTime? DeployedAtUtc, string? Environment);

public interface ISystemInfoService
{
    Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken);
}

public class SystemInfoService : ISystemInfoService
{
    private readonly TransportationDbContext _dbContext;
    private readonly IHostEnvironment _environment;

    public SystemInfoService(TransportationDbContext dbContext, IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    public async Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken)
    {
        var (version, commit) = ResolveVersion();
        var metadata = ReadDeploymentMetadata();

        string databaseStatus;
        long? latency = null;
        string? schemaVersion = null;
        var pending = 0;
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _ = await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            latency = stopwatch.ElapsedMilliseconds;
            var applied = (await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            schemaVersion = applied.LastOrDefault();
            pending = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
            databaseStatus = pending == 0 ? "Healthy" : $"Healthy — {pending} migratie(s) openstaand";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leak connection details; the status is enough for the admin screen.
            databaseStatus = "Unavailable";
        }

        return new SystemInfoDto(
            metadata?.Version ?? version,
            metadata?.Commit ?? commit,
            _environment.EnvironmentName,
            metadata?.DeployedAtUtc ?? TryGetBinaryTimestamp(),
            "Healthy",
            databaseStatus,
            latency,
            schemaVersion,
            pending,
            metadata?.Ref);
    }

    /// <summary>Semver from Directory.Build.props; commit from the SourceRevisionId the
    /// deploy script stamps into the InformationalVersion ("0.2.0+abc12345").</summary>
    private static (string Version, string? Commit) ResolveVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = informational.IndexOf('+');
        return plus < 0
            ? (informational, null)
            : (informational[..plus], informational[(plus + 1)..]);
    }

    private static DeploymentMetadata? ReadDeploymentMetadata()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "deployment.json");
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<DeploymentMetadata>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null; // corrupt metadata must never break the info screen
        }
    }

    private static DateTime? TryGetBinaryTimestamp()
    {
        try
        {
            var location = Assembly.GetExecutingAssembly().Location;
            return string.IsNullOrEmpty(location) ? null : File.GetLastWriteTimeUtc(location);
        }
        catch
        {
            return null;
        }
    }
}
