using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Notifications;

/// <summary>
/// Wave 8: notification categories/severity, archive, role/tenant targeting, per-category
/// preferences (critical bypasses) and the expiry producer for qualifications + fleet documents.
/// </summary>
public class NotificationExpansionTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, NotificationService Sut, Guid TenantId, Guid Me, Guid Colleague);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var me = Guid.NewGuid();
        var colleague = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.AddRange(
            new User
            {
                Id = me, TenantId = tenantId, Email = "ik@acme.be", PasswordHash = "x",
                FirstName = "Ik", LastName = "Zelf", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = colleague, TenantId = tenantId, Email = "collega@acme.be", PasswordHash = "x",
                FirstName = "Col", LastName = "Lega", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new NotificationService(db.Context, tenant, new DevCurrentUserContext(me), new TestClock(Now));
        return new Harness(db, sut, tenantId, me, colleague);
    }

    [Fact]
    public async Task Notify_ResolvesCategoryAndSeverity_FromTypeCatalog()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.NotifyAsync(h.Me, "exception_reported", "T", "M", null, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "customer_notification_failed", "T", "M", null, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "iets_onbekends", "T", "M", null, CancellationToken.None);

        var rows = h.Db.Context.Notifications.OrderBy(n => n.Type).ToList();
        var exception = rows.Single(n => n.Type == "exception_reported");
        Assert.Equal(NotificationCategory.Execution, exception.Category);
        Assert.Equal(NotificationSeverity.Warning, exception.Severity);
        var failed = rows.Single(n => n.Type == "customer_notification_failed");
        Assert.Equal(NotificationSeverity.Critical, failed.Severity);
        var unknown = rows.Single(n => n.Type == "iets_onbekends");
        Assert.Equal(NotificationCategory.General, unknown.Category);
        Assert.Equal(NotificationSeverity.Info, unknown.Severity);
    }

    [Fact]
    public async Task Preferences_DisabledCategorySkips_CriticalBypasses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.SetMyPreferenceAsync(NotificationCategory.Execution, false, CancellationToken.None);

        // Warning in a disabled category: skipped. Critical (System) in a disabled category: delivered.
        await h.Sut.SetMyPreferenceAsync(NotificationCategory.System, false, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "exception_reported", "T", "M", null, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "customer_notification_failed", "T", "M", null, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "absence_decided", "T", "M", null, CancellationToken.None);

        var mine = h.Db.Context.Notifications.Where(n => n.UserId == h.Me).Select(n => n.Type).ToList();
        Assert.DoesNotContain("exception_reported", mine);
        Assert.Contains("customer_notification_failed", mine);
        Assert.Contains("absence_decided", mine);

        var preferences = await h.Sut.GetMyPreferencesAsync(CancellationToken.None);
        Assert.False(preferences.Single(p => p.Category == NotificationCategory.Execution).Enabled);
        Assert.True(preferences.Single(p => p.Category == NotificationCategory.Hr).Enabled);
    }

    [Fact]
    public async Task Archive_RemovesFromDefaultList_AndCount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.NotifyAsync(h.Me, "absence_decided", "A", "M", null, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "trip_assigned", "B", "M", null, CancellationToken.None);
        var target = h.Db.Context.Notifications.First(n => n.Title == "A");

        Assert.True(await h.Sut.ArchiveAsync(target.Id, CancellationToken.None));

        var list = await h.Sut.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("B", list[0].Title);
        Assert.Equal(1, await h.Sut.UnreadCountAsync(CancellationToken.None));

        var includingArchived = await h.Sut.ListMineAsync(new NotificationQuery(false, null, true, 50), CancellationToken.None);
        Assert.Equal(2, includingArchived.Count);
        Assert.Contains(includingArchived, n => n.IsArchived);
    }

    [Fact]
    public async Task ListMine_FiltersByCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.NotifyAsync(h.Me, "absence_decided", "HR", "M", null, CancellationToken.None);
        await h.Sut.NotifyAsync(h.Me, "trip_assigned", "PL", "M", null, CancellationToken.None);

        var hr = await h.Sut.ListMineAsync(new NotificationQuery(false, NotificationCategory.Hr, false, 50), CancellationToken.None);

        Assert.Single(hr);
        Assert.Equal("HR", hr[0].Title);
    }

    [Fact]
    public async Task RoleAndTenantTargeting_Work()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var roleId = Guid.NewGuid();
        h.Db.Context.Roles.Add(new Role { Id = roleId, TenantId = h.TenantId, Name = "Ploegbaas", IsActive = true });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.Colleague, RoleId = roleId });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.NotifyRoleAsync(roleId, "planning_changed", "Rolbericht", "M", null, CancellationToken.None);
        await h.Sut.NotifyTenantAsync("company_news", "Iedereen", "M", null, CancellationToken.None);

        Assert.Single(h.Db.Context.Notifications.Where(n => n.Title == "Rolbericht"));
        Assert.Equal(h.Colleague, h.Db.Context.Notifications.Single(n => n.Title == "Rolbericht").UserId);
        Assert.Equal(2, h.Db.Context.Notifications.Count(n => n.Title == "Iedereen"));
    }

    [Fact]
    public async Task ExpiryProducer_NotifiesAndDeduplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employeeId = Guid.NewGuid();
        var qualTypeId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        var meUser = h.Db.Context.Users.Single(u => u.Id == h.Me);
        meUser.EmployeeId = employeeId;
        h.Db.Context.QualificationTypes.Add(new QualificationType
        {
            Id = qualTypeId, Code = "adr", Name = "ADR", Category = "Attest", RequiresExpiryDate = true, IsActive = true,
        });
        h.Db.Context.EmployeeQualifications.Add(new EmployeeQualification
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employeeId, QualificationTypeId = qualTypeId,
            ObtainedDate = new(2022, 1, 1), ExpiryDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(10),
            Status = QualificationStatus.Valid, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        // The colleague holds fleet_documents.view, so document expiries reach them.
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        h.Db.Context.Roles.Add(new Role { Id = roleId, TenantId = h.TenantId, Name = "Vloot", IsActive = true });
        h.Db.Context.Permissions.Add(new Permission
        {
            Id = permissionId, Code = "fleet_documents.view", Module = "fleet_documents", Action = "view",
        });
        h.Db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        h.Db.Context.UserRoles.Add(new UserRole { UserId = h.Colleague, RoleId = roleId });

        var vehicleId = Guid.NewGuid();
        h.Db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = h.TenantId, InternalNumber = "VR-1", LicensePlate = "1-ABC-123", IsActive = true,
        });
        h.Db.Context.FleetDocuments.Add(new FleetDocument
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = vehicleId,
            DocumentType = FleetDocumentType.Insurance,
            ExpiryDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(5),
        });
        await h.Db.Context.SaveChangesAsync();

        var producer = new ExpiryNotificationProducer(h.Db.Context, new TestClock(Now));
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Contains(h.Db.Context.Notifications, n => n.Type == "qualification_expiring" && n.UserId == h.Me);
        Assert.Contains(h.Db.Context.Notifications, n => n.Type == "document_expiring");
        var firstRunCount = h.Db.Context.Notifications.Count();

        // Second run inside the dedupe window produces nothing new.
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        Assert.Equal(firstRunCount, h.Db.Context.Notifications.Count());
    }
}
