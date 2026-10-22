namespace TransportationService.Api.Common.Models;

/// <summary>
/// Response headers of a capped, unpaged list endpoint (PERF-HIGH 3). Sent ONLY when the cap cut
/// the result: <c>X-Result-Truncated: true</c> and <c>X-Result-Limit: &lt;cap&gt;</c>. The CORS policy
/// exposes both so the SPA can read them.
/// </summary>
public static class ResultLimitHeaders
{
    public const string Truncated = "X-Result-Truncated";
    public const string Limit = "X-Result-Limit";
}
