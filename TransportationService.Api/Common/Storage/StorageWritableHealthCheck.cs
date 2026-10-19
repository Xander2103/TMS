using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TransportationService.Api.Common.Storage;

/// <summary>
/// Readiness (tag "ready"): the durable storage root accepts a write/read/delete probe. An
/// unwritable or vanished volume otherwise only shows up as generic 500s on uploads (live/22).
/// The description is deliberately generic — the endpoint is anonymous, so no path is exposed.
/// </summary>
public sealed class StorageWritableHealthCheck : IHealthCheck
{
    private readonly StorageRoot _root;

    public StorageWritableHealthCheck(StorageRoot root)
    {
        _root = root;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var failure = await StorageProbe.TryProbeAsync(_root.RootPath, cancellationToken);
        return failure is null
            ? HealthCheckResult.Healthy("Storage root is writable.")
            : new HealthCheckResult(context.Registration.FailureStatus, $"Storage root is not writable: {failure}.");
    }
}
