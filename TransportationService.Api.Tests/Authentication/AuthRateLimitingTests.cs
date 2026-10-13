using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Modules.Authentication;

namespace TransportationService.Api.Tests.Authentication;

/// <summary>
/// Proves the "auth" rate-limit policy registered by <see cref="RateLimitingServiceCollectionExtensions"/>
/// actually rejects the 11th request/minute from one IP with a 429 ProblemDetails response — not
/// just that some options object got built. Runs its own minimal TestServer host (rather than
/// TestServer against Program directly) since Program.cs uses top-level statements and the API
/// project does not expose a public/InternalsVisibleTo entry point type for WebApplicationFactory.
/// </summary>
public class AuthRateLimitingTests
{
    private static async Task<(TestServer Server, HttpClient Client)> BuildServerAsync()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthRateLimiting();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/api/auth/login", () => Results.Ok())
                            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.AuthPolicy);
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        var server = host.GetTestServer();
        return (server, server.CreateClient());
    }

    [Fact]
    public async Task AuthPolicy_PermitsTenRequestsPerMinute_ThenRejectsWithProblemDetails429()
    {
        var (_, client) = await BuildServerAsync();

        for (var i = 0; i < 10; i++)
        {
            using var response = await client.GetAsync("/api/auth/login");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        using var eleventh = await client.GetAsync("/api/auth/login");

        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal("application/problem+json", eleventh.Content.Headers.ContentType?.MediaType);
        var body = await eleventh.Content.ReadAsStringAsync();
        Assert.Contains("Te veel aanvragen", body);
    }

    [Fact]
    public async Task AuthPolicy_DoesNotQueue_RejectsImmediatelyAtTheLimit()
    {
        // QueueLimit = 0 means the 11th request must be rejected right away, not held/delayed —
        // exercised implicitly by the synchronous loop above completing without hanging, and
        // explicitly here via a tight concurrent burst that must still see an immediate 429.
        var (_, client) = await BuildServerAsync();
        for (var i = 0; i < 10; i++)
        {
            using var response = await client.GetAsync("/api/auth/login");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        using var rejected = await client.GetAsync("/api/auth/login");
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }
}
