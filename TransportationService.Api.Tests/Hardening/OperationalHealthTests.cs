using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Common.Health;
using TransportationService.Api.Common.Jobs;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Security;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// H1/H5: the operator health endpoints through the real pipeline. Live never touches a
/// dependency; ready runs only "ready"-tagged checks (database, schema, column-encryption key)
/// and answers 503 otherwise; neither needs authentication, a tenant or a rate-limit budget; no
/// body ever carries exception text, connection details or key material. Serial because the
/// column-encryption key ring is process-global.
/// </summary>
[Collection("ColumnEncryptionSerial")]
public sealed class OperationalHealthTests
{
    private const string UnreachableDatabase = "Data Source=/nonexistent-tms-health-dir/missing.db;Mode=ReadOnly";

    private sealed class AlwaysFailingCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Host=db.internal;Password=hunter2 connection refused");
    }

    private static async Task<(IHost Host, HttpClient Client, SqliteConnection Connection)> StartHealthyAsync(
        Action<IServiceCollection>? configure = null)
    {
        // Readiness runs its checks in parallel. One shared SqliteConnection cannot serve that (the
        // database check then flakes to Unhealthy); every context gets its own connection, as with
        // pooled Npgsql. This one only keeps the shared in-memory database alive.
        var database = $"DataSource=file:health-{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connection = new SqliteConnection(database);
        connection.Open();
        var host = await OperationalPipelineHost.StartAsync(options => options.UseSqlite(database), configureServices: configure);
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TransportationDbContext>().Database.EnsureCreatedAsync();
        }

        return (host, host.GetTestServer().CreateClient(), connection);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Live_Is200_EvenWithoutADatabase()
    {
        using var host = await OperationalPipelineHost.StartAsync(options => options.UseSqlite(UnreachableDatabase));
        using var client = host.GetTestServer().CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await JsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_Is503_WhenTheDatabaseIsDown_WithoutLeakingDetails()
    {
        using var host = await OperationalPipelineHost.StartAsync(options => options.UseSqlite(UnreachableDatabase));
        using var client = host.GetTestServer().CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("Unhealthy", body.GetProperty("status").GetString());
        var database = body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "database");
        Assert.Equal("Unhealthy", database.GetProperty("status").GetString());
        Assert.DoesNotContain("nonexistent", raw);
        Assert.DoesNotContain("Data Source", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", raw);
        Assert.DoesNotContain(" at ", raw);
    }

    [Fact]
    public async Task Ready_Is200_WhenAllReadyChecksPass_AndListsOnlyReadyChecks()
    {
        var (host, client, connection) = await StartHealthyAsync(services => services.AddHealthChecks()
            // An informational check from another module (e.g. Peppol) never gates readiness.
            .AddCheck<AlwaysFailingCheck>("peppol-provider", tags: [HealthTags.Peppol]));
        using var _ = host;
        using var __ = client;
        using var ___ = connection;

        using var response = await client.GetAsync("/health/ready");
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        var names = body.GetProperty("checks").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Equal(new[] { "database", "migrations", "column-encryption" }, names);
        Assert.All(body.GetProperty("checks").EnumerateArray(),
            check => Assert.False(string.IsNullOrWhiteSpace(check.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task Ready_Is503_WhenAThrowingReadyCheckFails_AndHidesTheExceptionMessage()
    {
        var (host, client, connection) = await StartHealthyAsync(services => services.AddHealthChecks()
            .AddCheck<AlwaysFailingCheck>("storage", tags: [HealthTags.Ready]));
        using var _ = host;
        using var __ = client;
        using var ___ = connection;

        using var response = await client.GetAsync("/health/ready");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("db.internal", raw);
        Assert.Contains("Controle mislukt.", raw);
    }

    [Fact]
    public async Task Ready_Is503_WhenTheColumnEncryptionKeyDoesNotMatchTheDatabase_WithoutKeyMaterial()
    {
        var writtenWith = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var wrongKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var (host, client, connection) = await StartHealthyAsync();
        using var _ = host;
        using var __ = client;
        using var ___ = connection;
        try
        {
            ColumnEncryption.Initialize(writtenWith);
            using (var scope = host.Services.CreateScope())
            {
                await ColumnEncryptionVerifier.EnsureKeyMatchesDatabaseAsync(
                    scope.ServiceProvider.GetRequiredService<TransportationDbContext>(), TimeProvider.System, CancellationToken.None);
            }

            using (var healthy = await client.GetAsync("/health/ready"))
            {
                Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
            }

            // Restored next to the wrong key.
            ColumnEncryption.Initialize(wrongKey);
            using var response = await client.GetAsync("/health/ready");
            var raw = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var check = JsonDocument.Parse(raw).RootElement.GetProperty("checks").EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == "column-encryption");
            Assert.Equal("Unhealthy", check.GetProperty("status").GetString());
            Assert.DoesNotContain(writtenWith, raw);
            Assert.DoesNotContain(wrongKey, raw);
        }
        finally
        {
            ColumnEncryption.Initialize(null);
        }
    }

    [Fact]
    public async Task ProbeEndpoints_NeedNoToken_NoTenant_AndAreNotRateLimited()
    {
        var (host, client, connection) = await StartHealthyAsync();
        using var _ = host;
        using var __ = client;
        using var ___ = connection;

        // Far more than the auth policy's 10/min: probes never hit a limiter.
        for (var i = 0; i < 25; i++)
        {
            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        // Security headers still apply.
        Assert.Equal("nosniff", live.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Details_RequiresAuthentication()
    {
        var (host, client, connection) = await StartHealthyAsync();
        using var _ = host;
        using var __ = client;
        using var ___ = connection;

        using var response = await client.GetAsync("/health/details");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DetailsView_ReportsEveryCheckWithItsTags_AndTheJobHeartbeats()
    {
        var (host, client, connection) = await StartHealthyAsync(services => services.AddHealthChecks()
            .AddCheck("backup-freshness", () => HealthCheckResult.Degraded("Laatste back-up ouder dan 26 uur.",
                data: new Dictionary<string, object> { ["ageHours"] = 30, ["blob"] = new object() }), tags: [HealthTags.Backup]));
        using var _ = host;
        using var __ = client;
        using var ___ = connection;

        var heartbeats = host.Services.GetRequiredService<JobHeartbeatRegistry>();
        heartbeats.RecordSuccess(JobNames.TaskSweep);
        heartbeats.RecordFailure(JobNames.ExpirySweep, new TimeoutException("secret detail"));

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        var details = OperationalHealth.ToDetails(report, heartbeats.Snapshot());

        Assert.Contains(details.Checks, c => c.Name == "outbox" && c.Tags.Contains(HealthTags.Outbox));
        var backup = Assert.Single(details.Checks, c => c.Name == "backup-freshness");
        Assert.Equal("Degraded", backup.Status);
        Assert.Equal(30, backup.Data["ageHours"]);
        Assert.False(backup.Data.ContainsKey("blob")); // only scalar data passes
        var jobs = Assert.Single(details.Checks, c => c.Name == "jobs");
        Assert.Equal("Degraded", jobs.Status);
        var integrations = Assert.Single(details.Checks, c => c.Name == "integrations");
        Assert.Equal("NotConfigured", integrations.Data["calendar"]);
        Assert.Equal("NotConfigured", integrations.Data["sms"]);

        var expiry = Assert.Single(details.Jobs, j => j.Name == JobNames.ExpirySweep);
        Assert.Equal(JobRunOutcome.Failed, expiry.LastOutcome);
        Assert.Equal(nameof(TimeoutException), expiry.LastError); // type only, never the message
        Assert.NotNull(Assert.Single(details.Jobs, j => j.Name == JobNames.TaskSweep).LastSuccessUtc);
    }
}
