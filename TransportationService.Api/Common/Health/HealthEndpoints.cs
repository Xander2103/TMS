using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportationService.Api.Common.Jobs;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Common.Health;

/// <summary>One check in a health response: name, status and a short non-secret description.</summary>
public sealed record HealthCheckEntryDto(string Name, string Status, string? Description, long DurationMs);

/// <summary>Body of <c>/health/live</c> and <c>/health/ready</c>.</summary>
public sealed record HealthResponseDto(string Status, long TotalDurationMs, IReadOnlyList<HealthCheckEntryDto> Checks);

public sealed record HealthDetailEntryDto(
    string Name, string Status, string? Description, long DurationMs,
    IReadOnlyList<string> Tags, IReadOnlyDictionary<string, object> Data);

/// <summary>Body of the authenticated <c>/health/details</c> view.</summary>
public sealed record HealthDetailsDto(
    string Status, long TotalDurationMs, IReadOnlyList<HealthDetailEntryDto> Checks, IReadOnlyList<JobHeartbeat> Jobs);

/// <summary>
/// H1: operator health endpoints.
/// <list type="bullet">
/// <item><c>GET /health/live</c>: anonymous; the process answers. No dependency is touched, so a
/// database outage never makes the supervisor restart a healthy process.</item>
/// <item><c>GET /health/ready</c>: anonymous; runs ONLY the checks tagged <see cref="HealthTags.Ready"/>
/// (database, schema, column-encryption key, plus storage from the storage module). 200 when all
/// are Healthy, otherwise 503. This is what the deploy script and external monitoring poll.</item>
/// <item><c>GET /health/details</c>: authenticated + <c>system_info.view</c>; every registered
/// check (peppol, backup, outbox, jobs, integrations, …) with its data, plus the job heartbeats.</item>
/// </list>
/// Responses carry status, check names and short descriptions only: never exception messages,
/// stack traces, host names or configuration values. The anonymous endpoints sit outside the
/// tenant guard (no tenant needed), the MVC account-state filter (not MVC) and every rate-limit
/// policy, and they are exempt from the HTTPS redirect so a loopback probe gets a real answer.
/// </summary>
public static class OperationalHealth
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    public const string DetailsPath = "/health/details";

    private static readonly TimeSpan ReadyCheckTimeout = TimeSpan.FromSeconds(5);
    private const int MaxDescriptionLength = 200;
    private const string CheckFailedDescription = "Controle mislukt.";

    public static IHealthChecksBuilder AddOperationalHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: [HealthTags.Ready], timeout: ReadyCheckTimeout)
            .AddCheck<PendingMigrationsHealthCheck>("migrations", tags: [HealthTags.Ready], timeout: ReadyCheckTimeout)
            .AddCheck<ColumnEncryptionHealthCheck>("column-encryption", tags: [HealthTags.Ready], timeout: ReadyCheckTimeout)
            .AddCheck<OutboxHealthCheck>("outbox", tags: [HealthTags.Outbox], timeout: ReadyCheckTimeout)
            .AddCheck<JobHeartbeatHealthCheck>("jobs", tags: [HealthTags.Jobs])
            .AddCheck<OptionalIntegrationsHealthCheck>("integrations", tags: [HealthTags.Integrations]);

    /// <summary>True for the anonymous probe paths (used to exempt them from the HTTPS redirect).</summary>
    public static bool IsProbePath(PathString path) =>
        path.StartsWithSegments(LivePath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(ReadyPath, StringComparison.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapOperationalHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(LivePath, [HttpMethods.Get, HttpMethods.Head], () =>
                Results.Json(new HealthResponseDto(nameof(HealthStatus.Healthy), 0, [])))
            .AllowAnonymous()
            .DisableRateLimiting()
            .ExcludeFromDescription();

        endpoints.MapMethods(ReadyPath, [HttpMethods.Get, HttpMethods.Head], async (HealthCheckService health, CancellationToken cancellationToken) =>
            {
                var report = await health.CheckHealthAsync(
                    registration => registration.Tags.Contains(HealthTags.Ready), cancellationToken);
                return Results.Json(
                    ToResponse(report),
                    statusCode: report.Status == HealthStatus.Healthy
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status503ServiceUnavailable);
            })
            .AllowAnonymous()
            .DisableRateLimiting()
            .ExcludeFromDescription();

        return endpoints;
    }

    public static HealthResponseDto ToResponse(HealthReport report) =>
        new(
            report.Status.ToString(),
            (long)report.TotalDuration.TotalMilliseconds,
            report.Entries
                .Select(entry => new HealthCheckEntryDto(
                    entry.Key, entry.Value.Status.ToString(), SafeDescription(entry.Value),
                    (long)entry.Value.Duration.TotalMilliseconds))
                .ToList());

    public static HealthDetailsDto ToDetails(HealthReport report, IReadOnlyList<JobHeartbeat> jobs) =>
        new(
            report.Status.ToString(),
            (long)report.TotalDuration.TotalMilliseconds,
            report.Entries
                .Select(entry => new HealthDetailEntryDto(
                    entry.Key, entry.Value.Status.ToString(), SafeDescription(entry.Value),
                    (long)entry.Value.Duration.TotalMilliseconds,
                    entry.Value.Tags.ToList(),
                    SafeData(entry.Value.Data)))
                .ToList(),
            jobs);

    /// <summary>
    /// A check that THREW gets its exception message as description from the framework; that text
    /// can carry connection details, so it is replaced by a neutral sentence.
    /// </summary>
    private static string? SafeDescription(HealthReportEntry entry)
    {
        if (entry.Exception is not null)
        {
            return CheckFailedDescription;
        }

        var description = entry.Description;
        return description is { Length: > MaxDescriptionLength } ? description[..MaxDescriptionLength] : description;
    }

    /// <summary>Only scalar values pass (counts, flags, timestamps, short strings).</summary>
    private static IReadOnlyDictionary<string, object> SafeData(IReadOnlyDictionary<string, object> data) =>
        data
            .Where(pair => pair.Value is string or bool or int or long or double or decimal or DateTime or DateTimeOffset)
            .ToDictionary(pair => pair.Key, pair => pair.Value is string text && text.Length > MaxDescriptionLength
                ? text[..MaxDescriptionLength]
                : pair.Value);
}

/// <summary>
/// The detailed health view for operators inside the application (system_info.view), including
/// the informational checks registered by other modules (peppol, backup, …) and the per-job
/// heartbeats. Always 200: the overall status is in the body; readiness is <c>/health/ready</c>.
/// </summary>
[ApiController]
public class HealthDetailsController : ControllerBase
{
    private readonly HealthCheckService _health;
    private readonly JobHeartbeatRegistry? _heartbeats;

    public HealthDetailsController(HealthCheckService health, JobHeartbeatRegistry? heartbeats = null)
    {
        _health = health;
        _heartbeats = heartbeats;
    }

    [HttpGet("health/details")]
    [RequirePermission(PermissionCodes.SystemInfoView)]
    public async Task<ActionResult<HealthDetailsDto>> Details(CancellationToken cancellationToken)
    {
        var report = await _health.CheckHealthAsync(cancellationToken);
        return Ok(OperationalHealth.ToDetails(report, _heartbeats?.Snapshot() ?? []));
    }
}
