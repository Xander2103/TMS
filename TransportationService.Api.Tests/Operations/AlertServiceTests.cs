using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Operations.Dtos;
using TransportationService.Api.Modules.Operations.Entities;
using TransportationService.Api.Modules.Operations.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Operations;

/// <summary>
/// The operational alert projection: dedupe on every sync, auto-resolve when the condition
/// disappears, reopen of the same row when it comes back, the acknowledge lifecycle and
/// tenant isolation.
/// </summary>
public class AlertServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, AlertSyncService Sync, AlertService Alerts, TestClock Clock,
        Guid TenantId, Guid UserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "ops@acme.be", PasswordHash = "x",
            FirstName = "Olga", LastName = "Ops", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var clock = new TestClock(Now);
        var sync = new AlertSyncService(db.Context, tenant, clock);
        var alerts = new AlertService(db.Context, tenant, user,
            new AuditService(db.Context, tenant, user), clock);
        return new Harness(db, sync, alerts, clock, tenantId, userId);
    }

    private static Incident CriticalIncident(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId,
        Title = "Vrachtwagen gekanteld", Description = "E17 richting Gent",
        IncidentType = IncidentType.Accident, Severity = IncidentSeverity.Critical,
        Status = IncidentStatus.New,
    };

    [Fact]
    public async Task Sync_Twice_NeverDuplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Incidents.Add(CriticalIncident(h.TenantId));
        await h.Db.Context.SaveChangesAsync();

        await h.Sync.SyncAsync(CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        await h.Sync.SyncAsync(CancellationToken.None);

        var alert = Assert.Single(h.Db.Context.OperationalAlerts.AsNoTracking());
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(AlertStatus.Active, alert.Status);
        // The second run only bumped the observation time.
        Assert.Equal(Now.UtcDateTime.AddSeconds(30), alert.LastSeenAt);
    }

    [Fact]
    public async Task Sync_ResolvedCondition_AutoResolves_AndReopensSameRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var incident = CriticalIncident(h.TenantId);
        h.Db.Context.Incidents.Add(incident);
        await h.Db.Context.SaveChangesAsync();
        await h.Sync.SyncAsync(CancellationToken.None);

        incident.Status = IncidentStatus.Resolved;
        await h.Db.Context.SaveChangesAsync();
        await h.Sync.SyncAsync(CancellationToken.None);

        var alert = Assert.Single(h.Db.Context.OperationalAlerts.AsNoTracking());
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
        Assert.Null(alert.ResolvedByUserId); // system resolution, no user

        // The same condition comes back: the SAME row reopens (unique dedupe key).
        incident.Status = IncidentStatus.New;
        await h.Db.Context.SaveChangesAsync();
        await h.Sync.SyncAsync(CancellationToken.None);

        var reopened = Assert.Single(h.Db.Context.OperationalAlerts.AsNoTracking());
        Assert.Equal(alert.Id, reopened.Id);
        Assert.Equal(AlertStatus.Active, reopened.Status);
        Assert.Null(reopened.ResolvedAt);
    }

    [Fact]
    public async Task Acknowledge_SetsActorAndTime_AndIsIdempotent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Incidents.Add(CriticalIncident(h.TenantId));
        await h.Db.Context.SaveChangesAsync();
        await h.Sync.SyncAsync(CancellationToken.None);
        var alert = Assert.Single(await h.Alerts.ListAsync(new AlertQuery(), CancellationToken.None));

        var acked = await h.Alerts.AcknowledgeAsync(alert.Id, CancellationToken.None);

        Assert.Equal(AlertStatus.Acknowledged, acked!.Status);
        Assert.Equal(h.UserId, acked.AcknowledgedByUserId);
        Assert.Equal("Olga Ops", acked.AcknowledgedByName);
        Assert.Equal(Now.UtcDateTime, acked.AcknowledgedAt);

        // Re-acknowledging changes nothing (no double audit, same actor kept).
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var again = await h.Alerts.AcknowledgeAsync(alert.Id, CancellationToken.None);
        Assert.Equal(Now.UtcDateTime, again!.AcknowledgedAt);

        var resolved = await h.Alerts.ResolveAsync(alert.Id, CancellationToken.None);
        Assert.Equal(AlertStatus.Resolved, resolved!.Status);
    }

    [Fact]
    public async Task Alerts_AreTenantScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Incidents.Add(CriticalIncident(h.TenantId));
        await h.Db.Context.SaveChangesAsync();
        await h.Sync.SyncAsync(CancellationToken.None);

        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var otherAlerts = new AlertService(h.Db.Context, otherTenant, new DevCurrentUserContext(null),
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)), h.Clock);

        Assert.Empty(await otherAlerts.ListAsync(new AlertQuery(), CancellationToken.None));
        Assert.Null(await otherAlerts.AcknowledgeAsync(
            h.Db.Context.OperationalAlerts.AsNoTracking().Single().Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_OrdersCriticalFirst_AndFilters()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Incidents.Add(CriticalIncident(h.TenantId));
        // A date-overdue inspection produces a Warning alert alongside the Critical incident.
        var vehicleId = Guid.NewGuid();
        h.Db.Context.Vehicles.Add(new Modules.Fleet.Entities.Vehicle
        {
            Id = vehicleId, TenantId = h.TenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1", IsActive = true,
        });
        h.Db.Context.Inspections.Add(new Modules.Fleet.Entities.Inspection
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId,
            VehicleId = vehicleId,
            InspectionType = Modules.Fleet.Entities.InspectionType.VehicleInspection,
            DueDate = new DateOnly(2026, 7, 1),
        });
        await h.Db.Context.SaveChangesAsync();
        await h.Sync.SyncAsync(CancellationToken.None);

        var all = await h.Alerts.ListAsync(new AlertQuery(), CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Equal(AlertSeverity.Critical, all[0].Severity);

        var onlyIncidents = await h.Alerts.ListAsync(new AlertQuery(Category: "Incident"), CancellationToken.None);
        Assert.Single(onlyIncidents);
    }
}
