using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Security;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 5: security headers (H10), CORS from configuration (M15), enum validation (M11) and the
/// documented middleware ordering.
/// </summary>
public class Phase5ApiHardeningTests
{
    private static async Task<HttpClient> ServerWithSecurityHeadersAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services => services.AddRouting());
                webHost.Configure(app =>
                {
                    app.UseMiddleware<SecurityHeadersMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapGet("/probe", () => Results.Ok(new { ok = true })));
                });
            })
            .StartAsync();
        return host.GetTestServer().CreateClient();
    }

    // ===================== H10 — security headers =====================

    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Referrer-Policy", "no-referrer")]
    public async Task Responses_CarryTheBaselineSecurityHeaders(string header, string expected)
    {
        var client = await ServerWithSecurityHeadersAsync();

        using var response = await client.GetAsync("/probe");

        Assert.True(response.Headers.TryGetValues(header, out var values) || response.Content.Headers.TryGetValues(header, out values),
            $"missing header {header}");
        Assert.Contains(expected, values!);
    }

    [Fact]
    public async Task Responses_CarryARestrictiveContentSecurityPolicy()
    {
        var client = await ServerWithSecurityHeadersAsync();

        using var response = await client.GetAsync("/probe");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        // The API serves no HTML/JS, so everything is denied and framing is impossible.
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
    }

    [Fact]
    public async Task SensitiveResponses_AreNotCacheable()
    {
        var client = await ServerWithSecurityHeadersAsync();

        using var response = await client.GetAsync("/probe");

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task ServerIdentificationHeaders_AreNotAdvertised()
    {
        var client = await ServerWithSecurityHeadersAsync();

        using var response = await client.GetAsync("/probe");

        Assert.False(response.Headers.Contains("X-Powered-By"));
    }

    // ===================== M15 — CORS from configuration =====================

    private static async Task<HttpClient> ServerWithCorsAsync(string[] allowedOrigins)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCors(options => options.AddPolicy("Frontend", policy =>
                        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseCors("Frontend");
                    app.UseEndpoints(endpoints => endpoints.MapGet("/probe", () => Results.Ok()));
                });
            })
            .StartAsync();
        return host.GetTestServer().CreateClient();
    }

    [Fact]
    public async Task Cors_AllowsAConfiguredOrigin()
    {
        var client = await ServerWithCorsAsync(["https://app.example.com"]);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("Origin", "https://app.example.com");

        using var response = await client.SendAsync(request);

        Assert.Contains("https://app.example.com",
            response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_RejectsAnUnknownOrigin()
    {
        var client = await ServerWithCorsAsync(["https://app.example.com"]);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("Origin", "https://evil.example.com");

        using var response = await client.SendAsync(request);

        // No allow-origin header => the browser blocks the response.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_WithNoConfiguredOrigins_AllowsNothing()
    {
        // Fail-closed: an unconfigured non-Development deployment must not become open.
        var client = await ServerWithCorsAsync([]);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
        request.Headers.Add("Origin", "https://app.example.com");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ===================== M11 — enum validation =====================

    [Theory]
    [InlineData("7")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("NotAStatus")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseDefined_RejectsNumericAndUnknownValues(string? value)
        => Assert.False(EnumParsing.TryParseDefined<InvoiceStatus>(value, out _));

    [Theory]
    [InlineData("Draft", InvoiceStatus.Draft)]
    [InlineData("sent", InvoiceStatus.Sent)]
    [InlineData("  PAID  ", InvoiceStatus.Paid)]
    public void TryParseDefined_AcceptsDefinedNamesCaseInsensitively(string value, InvoiceStatus expected)
    {
        Assert.True(EnumParsing.TryParseDefined<InvoiceStatus>(value, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void ParseDefinedOrThrow_ThrowsDomainValidationForUndefinedValue()
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            EnumParsing.ParseDefinedOrThrow<InvoiceStatus>("42", "status", "factuurstatus"));

        Assert.Contains("status", exception.FieldErrors!.Keys);
    }
}
