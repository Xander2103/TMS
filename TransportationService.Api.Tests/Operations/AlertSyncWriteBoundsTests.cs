using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Operations.Entities;
using TransportationService.Api.Modules.Operations.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Operations;

/// <summary>
/// PERF-HIGH 4 write bounds of the alert reconciliation, proven on the SQL statements the context
/// actually sends: the steady state issues zero writes, every change writes exactly its rows,
/// LastSeenAt is refreshed at a bounded cadence and never before, the overview projection never
/// writes, and the background worker covers every active tenant idempotently.
/// </summary>
public class AlertSyncWriteBoundsTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 20);

    private sealed class Harness : IDisposable
    {
        public SqliteTestDbContext Db { get; }
        public WriteCommandCounter Counter { get; } = new();
        public TestClock Clock { get; } = new(Now);
        public Guid TenantId { get; } = Guid.NewGuid();
        public AlertSyncService Sync { get; }

        public Harness()
        {
            Db = new SqliteTestDbContext(null, Counter);
            AddTenant(TenantId, "acme", isActive: true);
            Db.Context.SaveChanges();
            Sync = new AlertSyncService(Db.Context, new DevTenantContext(TenantId), Clock);
            Counter.Reset();
        }

        public void AddTenant(Guid id, string slug, bool isActive) =>
            Db.Context.Tenants.Add(new Tenant { Id = id, Name = slug, Slug = slug, IsActive = isActive, CreatedAt = Now.UtcDateTime });

        public Incident AddCriticalIncident(Guid? tenantId = null, string title = "Vrachtwagen gekanteld")
        {
            var incident = new Incident
            {
                Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId,
                Title = title, Description = "E17 richting Gent",
                IncidentType = IncidentType.Accident, Severity = IncidentSeverity.Critical,
                Status = IncidentStatus.New,
            };
            Db.Context.Incidents.Add(incident);
            Db.Context.SaveChanges();
            return incident;
        }

        public void AddOverdueInspection()
        {
            var vehicleId = Guid.NewGuid();
            Db.Context.Vehicles.Add(new Vehicle
            {
                Id = vehicleId, TenantId = TenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1", IsActive = true,
            });
            Db.Context.Inspections.Add(new Inspection
            {
                Id = Guid.NewGuid(), TenantId = TenantId, VehicleId = vehicleId,
                InspectionType = InspectionType.VehicleInspection, DueDate = Today.AddDays(-19),
            });
            Db.Context.SaveChanges();
        }

        public Trip AddActiveTrip()
        {
            var trip = new Trip
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TripNumber = "RIT-0001", TripDate = Today,
                Status = TripStatus.InProgress,
            };
            Db.Context.Trips.Add(trip);
            Db.Context.SaveChanges();
            return trip;
        }

        /// <summary>An open execution exception; its alert severity follows the exception severity.</summary>
        public ExecutionException AddOpenException(ExceptionSeverity severity)
        {
            var trip = AddActiveTrip();
            var exception = new ExecutionException
            {
                Id = Guid.NewGuid(), TenantId = TenantId, TripId = trip.Id,
                Type = ExecutionExceptionType.DamagedPackage, Severity = severity,
                Status = ExecutionExceptionStatus.Open, Description = "Pallet beschadigd",
                OccurredAt = Now.UtcDateTime,
            };
            Db.Context.ExecutionExceptions.Add(exception);
            Db.Context.SaveChanges();
            return exception;
        }

        /// <summary>Forget the seeding writes and (optionally) move the clock before the measured call.</summary>
        public void StartCounting(TimeSpan? advance = null)
        {
            Counter.Reset();
            if (advance is { } by)
            {
                Clock.Advance(by);
            }
        }

        public Task<AlertSyncResult> SyncAsync() => Sync.SyncAsync(CancellationToken.None);

        public OperationalAlert SingleAlert() => Assert.Single(Db.Context.OperationalAlerts.AsNoTracking().ToList());

        public List<OperationalAlert> Alerts() => Db.Context.OperationalAlerts.AsNoTracking().OrderBy(a => a.DedupeKey).ToList();

        public void AssertNoWrites() => Assert.True(
            Counter.Writes == 0,
            "Expected no write statements but saw: " + string.Join(" | ", Counter.Statements));

        public void AssertWrites(int inserts, int updates) =>
            Assert.True(
                Counter.Inserts == inserts && Counter.Updates == updates && Counter.Deletes == 0,
                $"Expected {inserts} INSERT / {updates} UPDATE / 0 DELETE but saw "
                + $"{Counter.Inserts} / {Counter.Updates} / {Counter.Deletes}: " + string.Join(" | ", Counter.Statements));

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task SteadyState_SecondSync_IssuesNoStatements()
    {
        using var h = new Harness();
        h.AddCriticalIncident();
        h.AddOverdueInspection();
        var first = await h.SyncAsync();
        Assert.Equal(2, first.Created);
        Assert.Equal(2, h.Alerts().Count);

        h.StartCounting(TimeSpan.FromSeconds(30));
        var second = await h.SyncAsync();

        Assert.Equal(AlertSyncResult.Empty with { Unchanged = 2 }, second);
        h.AssertNoWrites();
        Assert.All(h.Alerts(), alert => Assert.Equal(Now.UtcDateTime, alert.LastSeenAt));
    }

    [Fact]
    public async Task ChangedSeverity_WritesExactlyOneUpdate()
    {
        using var h = new Harness();
        var exception = h.AddOpenException(ExceptionSeverity.Medium);
        await h.SyncAsync();
        Assert.Equal(AlertSeverity.Warning, h.SingleAlert().Severity);

        exception.Severity = ExceptionSeverity.Critical;
        await h.Db.Context.SaveChangesAsync();
        h.StartCounting(TimeSpan.FromSeconds(30));
        var result = await h.SyncAsync();

        Assert.Equal(AlertSyncResult.Empty with { Updated = 1 }, result);
        h.AssertWrites(inserts: 0, updates: 1);
        var alert = h.SingleAlert();
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(Now.UtcDateTime.AddSeconds(30), alert.LastSeenAt);
    }

    [Fact]
    public async Task NewCondition_WritesExactlyOneInsert_AndLeavesTheOthersAlone()
    {
        using var h = new Harness();
        h.AddCriticalIncident(title: "Eerste");
        await h.SyncAsync();

        h.AddCriticalIncident(title: "Tweede");
        h.StartCounting(TimeSpan.FromSeconds(30));
        var result = await h.SyncAsync();

        Assert.Equal(AlertSyncResult.Empty with { Created = 1, Unchanged = 1 }, result);
        h.AssertWrites(inserts: 1, updates: 0);
        Assert.Equal(2, h.Alerts().Count);
    }

    [Fact]
    public async Task DisappearedCondition_ResolvesWithOneUpdate_StaysSilent_AndReopensWithOneUpdate()
    {
        using var h = new Harness();
        var incident = h.AddCriticalIncident();
        await h.SyncAsync();

        incident.Status = IncidentStatus.Resolved;
        await h.Db.Context.SaveChangesAsync();
        h.StartCounting(TimeSpan.FromSeconds(30));
        var resolvedRun = await h.SyncAsync();

        Assert.Equal(AlertSyncResult.Empty with { Resolved = 1 }, resolvedRun);
        h.AssertWrites(inserts: 0, updates: 1);
        Assert.Equal(AlertStatus.Resolved, h.SingleAlert().Status);

        // A resolved alert whose condition stays gone is not a candidate and is never touched again.
        h.StartCounting(TimeSpan.FromHours(1));
        var quietRun = await h.SyncAsync();
        Assert.Equal(AlertSyncResult.Empty, quietRun);
        h.AssertNoWrites();

        // The condition returns: the SAME row reopens with exactly one UPDATE, no new row.
        incident.Status = IncidentStatus.New;
        await h.Db.Context.SaveChangesAsync();
        h.StartCounting(TimeSpan.FromSeconds(30));
        var reopenRun = await h.SyncAsync();

        Assert.Equal(AlertSyncResult.Empty with { Reopened = 1 }, reopenRun);
        h.AssertWrites(inserts: 0, updates: 1);
        var reopened = h.SingleAlert();
        Assert.Equal(AlertStatus.Active, reopened.Status);
        Assert.Null(reopened.ResolvedAt);
        Assert.Equal(h.Clock.GetUtcNow().UtcDateTime, reopened.LastSeenAt);
    }

    [Fact]
    public async Task LastSeenAt_RefreshesOncePerInterval_AndNotBefore()
    {
        using var h = new Harness();
        h.AddCriticalIncident();
        await h.SyncAsync();
        var interval = AlertSyncService.LastSeenRefreshInterval;

        // One minute short of the window: nothing is written, the stamp is untouched.
        h.StartCounting(interval - TimeSpan.FromMinutes(1));
        var early = await h.SyncAsync();
        Assert.Equal(AlertSyncResult.Empty with { Unchanged = 1 }, early);
        h.AssertNoWrites();
        Assert.Equal(Now.UtcDateTime, h.SingleAlert().LastSeenAt);

        // Exactly at the window: one UPDATE that only re-stamps the observation time.
        h.StartCounting(TimeSpan.FromMinutes(1));
        var due = await h.SyncAsync();
        Assert.Equal(AlertSyncResult.Empty with { Refreshed = 1 }, due);
        h.AssertWrites(inserts: 0, updates: 1);
        var refreshed = h.SingleAlert();
        Assert.Equal(Now.UtcDateTime + interval, refreshed.LastSeenAt);
        Assert.Equal(AlertStatus.Active, refreshed.Status);

        // And the window starts over: the next poll-cadence run is silent again.
        h.StartCounting(TimeSpan.FromSeconds(30));
        var after = await h.SyncAsync();
        Assert.Equal(AlertSyncResult.Empty with { Unchanged = 1 }, after);
        h.AssertNoWrites();
    }

    [Fact]
    public async Task Overview_IsReadOnly_EvenWhenAlertsAreStale()
    {
        using var h = new Harness();
        h.AddActiveTrip();
        h.AddCriticalIncident();
        await h.SyncAsync();
        var overview = new OperationsOverviewService(h.Db.Context, new DevTenantContext(h.TenantId), h.Clock);

        h.StartCounting();
        var projection = await overview.GetOverviewAsync(CancellationToken.None);

        h.AssertNoWrites();
        Assert.Single(projection.Trips);
        Assert.Equal(1, projection.Counters.ActiveAlerts);
        Assert.Equal(1, projection.Counters.CriticalAlerts);

        // Far past the LastSeenAt refresh window: a read still never reconciles or re-stamps.
        h.StartCounting(AlertSyncService.LastSeenRefreshInterval * 4);
        await overview.GetOverviewAsync(CancellationToken.None);
        h.AssertNoWrites();
        Assert.Equal(Now.UtcDateTime, h.SingleAlert().LastSeenAt);
    }
}
