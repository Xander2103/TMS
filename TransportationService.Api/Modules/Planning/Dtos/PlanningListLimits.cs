namespace TransportationService.Api.Modules.Planning.Dtos;

/// <summary>
/// Caps of the unpaged planning read models (PERF-HIGH 3). A list that hit its cap SAYS so instead
/// of silently dropping rows: the trips endpoint through the <c>X-Result-Truncated</c> /
/// <c>X-Result-Limit</c> response headers, the board and the resources panel through their
/// <c>Truncated</c> / <c>Limit</c> fields.
/// </summary>
public static class PlanningListLimits
{
    /// <summary>GET /api/trips.</summary>
    public const int Trips = 500;

    /// <summary>Trips of one planning-board window (max 31 days).</summary>
    public const int BoardTrips = 500;

    /// <summary>Drivers, vehicles and trailers of the resources panel (each list separately).</summary>
    public const int Resources = 500;
}
