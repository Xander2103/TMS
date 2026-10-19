using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Npgsql;
using TransportationService.Api.Common.Diagnostics;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// DB outage → 503 (narrowly: connection-level failures only) and LOG-01: the correlation id
/// returned to the client is the TraceId that every log line of that request carries.
/// </summary>
public sealed class ErrorHandlingAndCorrelationTests : IAsyncLifetime
{
    private readonly CapturingLoggerProvider _logs = new();
    private SqliteConnection _connection = null!;
    private IHost _host = null!;
    private HttpClient _client = null!;

    private static readonly Dictionary<string, Func<Exception>> Throwers = new()
    {
        ["npgsql-socket"] = () => new NpgsqlException("Failed to connect to 10.0.0.5:5432", new SocketException(111)),
        ["npgsql-timeout"] = () => new NpgsqlException("Exception while connecting", new TimeoutException()),
        ["pg-admin-shutdown"] = () => new PostgresException("terminating connection due to administrator command", "FATAL", "FATAL", "57P01"),
        ["pg-connection-failure"] = () => new PostgresException("connection failure", "FATAL", "FATAL", "08006"),
        ["pg-too-many"] = () => new PostgresException("sorry, too many clients already", "FATAL", "FATAL", "53300"),
        ["ef-wrapped"] = () => new DbUpdateException("An error occurred while saving the entity changes.",
            new NpgsqlException("Exception while reading from stream", new IOException("connection reset"))),
        ["retry-limit"] = () => new RetryLimitExceededException("Maximum number of retries (6) exceeded",
            new InvalidOperationException("transient")),
        ["pg-unique"] = () => new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505"),
        ["pg-statement-cancelled"] = () => new PostgresException("canceling statement due to statement timeout", "ERROR", "ERROR", "57014"),
        ["plain"] = () => new InvalidOperationException("kapot: Password=hunter2"),
    };

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _host = await OperationalPipelineHost.StartAsync(
            options => options.UseSqlite(_connection),
            _logs,
            mapExtra: endpoints =>
            {
                foreach (var (name, thrower) in Throwers)
                {
                    endpoints.MapGet($"/test/throw/{name}", (RequestDelegate)(_ => throw thrower())).AllowAnonymous();
                }
            });
        _client = _host.GetTestServer().CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        _connection.Dispose();
    }

    [Theory]
    [InlineData("npgsql-socket")]
    [InlineData("npgsql-timeout")]
    [InlineData("pg-admin-shutdown")]
    [InlineData("pg-connection-failure")]
    [InlineData("pg-too-many")]
    [InlineData("ef-wrapped")]
    [InlineData("retry-limit")]
    public async Task ConnectionLevelDatabaseFailures_Answer503ServiceUnavailable_WithCorrelationId(string scenario)
    {
        using var response = await _client.GetAsync($"/test/throw/{scenario}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("Service unavailable", body.GetProperty("title").GetString());
        Assert.Equal(503, body.GetProperty("status").GetInt32());
        Assert.Equal("common.service_unavailable", body.GetProperty("code").GetString());
        var correlationId = body.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationId.HeaderName).Single());
        Assert.True(response.Headers.Contains("Retry-After"));
        // No provider/host details leak to the client.
        Assert.DoesNotContain("10.0.0.5", raw);
        Assert.DoesNotContain("Npgsql", raw);
        Assert.DoesNotContain("Exception", raw);
    }

    [Theory]
    [InlineData("pg-unique")]
    [InlineData("pg-statement-cancelled")]
    [InlineData("plain")]
    public async Task OtherFailures_StayAGeneric500(string scenario)
    {
        using var response = await _client.GetAsync($"/test/throw/{scenario}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Er is een onverwachte fout opgetreden.", JsonDocument.Parse(raw).RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("duplicate key", raw);
    }

    [Fact]
    public async Task UnhandledError_CorrelationIdFromTheBody_AppearsInTheServerLog()
    {
        using var response = await _client.GetAsync("/test/throw/plain");
        var correlationId = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("correlationId").GetString()!;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(32, correlationId.Length); // the W3C trace id, not a connection-local counter

        var requestLines = _logs.Entries.Where(e => e.Scopes.Contains(correlationId, StringComparison.Ordinal)).ToList();
        // The exception itself (framework handler, Error) and the request log line (Warning).
        Assert.Contains(requestLines, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
        Assert.Contains(requestLines, e => e.Category == typeof(RequestLoggingMiddleware).FullName
                                           && e.Level == LogLevel.Warning
                                           && e.Message.Contains("500")
                                           && e.Message.Contains(correlationId));
    }

    [Fact]
    public async Task DatabaseOutage_IsLoggedWithTheCorrelationId_AndNoQueryString()
    {
        using var response = await _client.GetAsync("/test/throw/npgsql-socket?token=geheim-token");
        var correlationId = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("correlationId").GetString()!;

        var outage = Assert.Single(_logs.Entries, e => e.Category == typeof(DatabaseUnavailableExceptionHandler).FullName);
        Assert.Equal(LogLevel.Error, outage.Level);
        Assert.Contains(correlationId, outage.Scopes);
        // Our own lines never carry the query string (framework Information-level request lines
        // are filtered out in Production by "Microsoft.AspNetCore": "Warning").
        Assert.DoesNotContain(_logs.Entries, e =>
            (e.Category == typeof(RequestLoggingMiddleware).FullName
             || e.Category == typeof(DatabaseUnavailableExceptionHandler).FullName)
            && e.Message.Contains("geheim-token"));
        Assert.Contains(_logs.Entries, e => e.Category == typeof(RequestLoggingMiddleware).FullName
                                            && e.Message.Contains("/test/throw/npgsql-socket responded 503"));
    }

    [Fact]
    public async Task EveryResponse_CarriesTheCorrelationIdHeader()
    {
        using var response = await _client.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(32, response.Headers.GetValues(CorrelationId.HeaderName).Single().Length);
    }

    [Fact]
    public void ConsoleLogging_IsConfiguredWithScopes_SingleLineOutsideDevelopment()
    {
        var options = _host.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue;
        Assert.True(options.IncludeScopes);
        Assert.True(options.SingleLine);
        Assert.True(_host.Services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue.IncludeScopes);
    }

    [Fact]
    public void ClassificationIsNarrow()
    {
        Assert.True(DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(Throwers["npgsql-socket"]()));
        Assert.False(DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(Throwers["pg-unique"]()));
        Assert.False(DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(new TimeoutException()));
        Assert.False(DatabaseUnavailableExceptionHandler.IsDatabaseUnavailable(new DbUpdateConcurrencyException("stale")));
    }
}
