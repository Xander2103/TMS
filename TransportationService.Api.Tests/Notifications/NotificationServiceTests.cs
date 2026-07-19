using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Notifications;

public class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Notify_ListMine_MarkRead_IsUserScoped()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "A", Slug = "a", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.AddRange(
            new User { Id = me, TenantId = tenantId, Email = "me@a.be", PasswordHash = "x", FirstName = "Ik", LastName = "Zelf", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime },
            new User { Id = other, TenantId = tenantId, Email = "ander@a.be", PasswordHash = "x", FirstName = "An", LastName = "Der", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new NotificationService(db.Context, tenant, new DevCurrentUserContext(me), new TestClock(Now));

        await sut.NotifyAsync(me, "test", "Voor mij", "Bericht", "/dashboard", CancellationToken.None);
        await sut.NotifyAsync(other, "test", "Voor ander", "Bericht", null, CancellationToken.None);
        await sut.NotifyAsync(null, "test", "Niemand", "Genegeerd", null, CancellationToken.None);

        var mine = await sut.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        var single = Assert.Single(mine);
        Assert.Equal("Voor mij", single.Title);
        Assert.Equal(1, await sut.UnreadCountAsync(CancellationToken.None));

        Assert.True(await sut.MarkReadAsync(single.Id, CancellationToken.None));
        Assert.Equal(0, await sut.UnreadCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AbsenceDecision_NotifiesEmployeeUser()
    {
        var db = new SqliteTestDbContext();
        using var _ = db;
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var deciderId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "A", Slug = "a", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User { Id = employeeUserId, TenantId = tenantId, Email = "jan@a.be", PasswordHash = "x", FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var notifications = new NotificationService(db.Context, tenant, new DevCurrentUserContext(deciderId), clock);
        var absences = new AbsenceService(db.Context, tenant, new DevCurrentUserContext(deciderId),
            new AuditService(db.Context, tenant, new DevCurrentUserContext(deciderId)), notifications,
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(
                Path.Combine(Path.GetTempPath(), "ts-notif-tests", Guid.NewGuid().ToString("N"))),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            clock);

        var created = await absences.CreateForEmployeeAsync(employeeId,
            new CreateAbsenceRequest(AbsenceType.Vacation, new(2026, 8, 3), new(2026, 8, 14), null),
            CancellationToken.None);
        await absences.DecideAsync(created.Absence!.Id, new DecideAbsenceRequest(true, null), CancellationToken.None);

        var employeeView = new NotificationService(db.Context, tenant, new DevCurrentUserContext(employeeUserId), clock);
        var mine = await employeeView.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        var decision = Assert.Single(mine, n => n.Type == "absence_decided");
        Assert.Equal("Afwezigheid goedgekeurd", decision.Title);
    }
}
