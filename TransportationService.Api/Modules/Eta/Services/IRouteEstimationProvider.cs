namespace TransportationService.Api.Modules.Eta.Services;

public record RouteEstimationStop(Guid StopId, decimal? Latitude, decimal? Longitude, string? City);

public record RouteEstimationRequest(IReadOnlyList<RouteEstimationStop> Stops);

/// <summary>
/// Seam for a real route/traffic provider (e.g. PTV). Returns the travel minutes towards
/// each stop in order, or null when no estimate is available — the internal sequential
/// heuristic then applies. The internal ETA is explicitly NOT route optimisation.
/// </summary>
public interface IRouteEstimationProvider
{
    Task<IReadOnlyList<int>?> EstimateTravelMinutesAsync(RouteEstimationRequest request, CancellationToken cancellationToken);
}

/// <summary>Default: no provider configured; every request answers null.</summary>
public sealed class NoRouteEstimationProvider : IRouteEstimationProvider
{
    public Task<IReadOnlyList<int>?> EstimateTravelMinutesAsync(
        RouteEstimationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>?>(null);
}
