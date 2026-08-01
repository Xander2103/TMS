using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tasks.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Notifications;

/// <summary>Sprint fasen 16+22: sweeps zijn idempotent (dedupe keys/stamps) en escaleren volgens beleid.</summary>
public class SweepAndEscalationTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 01, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, TestClock Clock, Guid TenantId, Guid ManagerUserId, Guid EmployeeId, Guid EmployeeUserId)
    {
        public InventorySweepWorker InventorySweep() =>
            new(Db.Context, Clock, NullLogger<InventorySweepWorker>.Instance);

        public TaskSweepWorker TaskSweep() =>
            new(Db.Context, Clock, NullLogger<TaskSweepWorker>.Instance);

        public Task<List<Notification>> Notifications(string type) =>
            Db.Context.Notifications.Where(n => n.TenantId == TenantId && n.Type == type).ToListAsync();
    }

    /// <summary>Manager holds every relevant recipient permission via one role.</summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen", IsActive = true,
        });
        db.Context.Users.AddRange(
            new User { Id = managerUserId, TenantId = tenantId, Email = "mgr@acme.be", FirstName = "Mark", LastName = "Manager", IsActive = true },
            new User { Id = employeeUserId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Janssen", EmployeeId = employeeId, IsActive = true });

        var roleId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Beheer", IsActive = true });
        foreach (var code in new[]
                 {
                     PermissionCodes.IssuedItemsManage, PermissionCodes.InventoryManageThresholds,
                     PermissionCodes.InventoryLowStockAlerts, PermissionCodes.TasksViewAll,
                     PermissionCodes.MessagesViewDeliveryStatus,
                 })
        {
            var permissionId = Guid.NewGuid();
            db.Context.Permissions.Add(new Permission
            {
                Id = permissionId, Code = code, Module = code.Split('.')[0], Action = code.Split('.')[1],
            });
            db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        }

        db.Context.UserRoles.Add(new UserRole { UserId = managerUserId, RoleId = roleId });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new TestClock(Now), tenantId, managerUserId, employeeId, employeeUserId);
    }

    [Fact]
    public async Task InventorySweep_AnnouncesOverdueReturn_OnceAndEscalatesAfterDelay()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        h.Db.Context.EmployeeIssuedItems.Add(new EmployeeIssuedItem
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            NameSnapshot = "Boormachine", Status = IssuedItemStatus.Issued, Quantity = 1,
            ExpectedReturnDate = today.AddDays(-4),
        });
        // ReturnOverdue-escalatie actief maken (default inactief).
        h.Db.Context.Add(new EscalationPolicy
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Kind = EscalationKind.ReturnOverdue,
            DelayHours = 72, TargetPermissionCode = PermissionCodes.IssuedItemsManage, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.InventorySweep().SweepTenantAsync(h.TenantId, CancellationToken.None);
        await h.InventorySweep().SweepTenantAsync(h.TenantId, CancellationToken.None); // idempotent

        Assert.Single(await h.Notifications("inventory_return_overdue"));
        var escalations = await h.Notifications("escalation_raised");
        Assert.Single(escalations); // 4 dagen te laat > 72u escalatietermijn
        Assert.Equal(h.ManagerUserId, escalations[0].UserId);
    }

    [Fact]
    public async Task InventorySweep_FlagsInactiveEmployeeHoldingMaterial()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = await h.Db.Context.Employees.SingleAsync();
        employee.IsActive = false;
        h.Db.Context.EmployeeIssuedItems.Add(new EmployeeIssuedItem
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            NameSnapshot = "Laptop", Status = IssuedItemStatus.Issued, Quantity = 1,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.InventorySweep().SweepTenantAsync(h.TenantId, CancellationToken.None);
        await h.InventorySweep().SweepTenantAsync(h.TenantId, CancellationToken.None);

        var flagged = await h.Notifications("inventory_return_overdue");
        Assert.Single(flagged);
        Assert.Contains("niet langer actief", flagged[0].Message);
    }

    [Fact]
    public async Task InventorySweep_EscalatesUnresolvedNegativeStock_PerEpisode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var templateId = Guid.NewGuid();
        h.Db.Context.IssuedItemTemplates.Add(new IssuedItemTemplate
        {
            Id = templateId, TenantId = h.TenantId, Name = "Tape", StockTrackingEnabled = true,
            AllowNegativeStock = true, CurrentStock = -2,
        });
        h.Db.Context.InventoryAlerts.Add(new InventoryAlert
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TemplateId = templateId,
            Kind = InventoryStatus.NegativeStock, Status = InventoryAlertStatus.Active,
            StockSnapshot = -2, ActivatedAt = Now.UtcDateTime.AddHours(-30), LastSeenAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.InventorySweep().SweepTenantAsync(h.TenantId, CancellationToken.None);
        await h.InventorySweep().SweepTenantAsync(h.TenantId, CancellationToken.None);

        var escalations = (await h.Notifications("escalation_raised"))
            .Where(n => n.DedupeKey != null && n.DedupeKey.StartsWith("escalation:negative_stock"))
            .ToList();
        Assert.Single(escalations); // default 24u, alert is 30u oud → één escalatie, gededupet
    }

    [Fact]
    public async Task TaskSweep_DueSoonAndOverdue_AreOneShot_AndOverdueEscalates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.EmployeeTasks.AddRange(
            new EmployeeTask
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Bijna", AssignedEmployeeId = h.EmployeeId,
                Status = EmployeeTaskStatus.Todo, DueAt = Now.UtcDateTime.AddHours(4),
            },
            new EmployeeTask
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, Title = "Te laat", AssignedEmployeeId = h.EmployeeId,
                Status = EmployeeTaskStatus.InProgress, DueAt = Now.UtcDateTime.AddHours(-72),
                CreatedByUserId = h.ManagerUserId,
            });
        await h.Db.Context.SaveChangesAsync();

        await h.TaskSweep().SweepTenantAsync(h.TenantId, CancellationToken.None);
        await h.TaskSweep().SweepTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Single(await h.Notifications("task_due_soon"));
        var overdue = await h.Notifications("task_overdue");
        Assert.Equal(2, overdue.Count); // assignee + opdrachtgever, elk exact één keer
        Assert.Contains(overdue, n => n.UserId == h.EmployeeUserId);
        Assert.Contains(overdue, n => n.UserId == h.ManagerUserId);

        // Default TaskOverdue-escalatie (48u) vuurt voor de 72u oude taak.
        Assert.Single((await h.Notifications("escalation_raised"))
            .Where(n => n.DedupeKey != null && n.DedupeKey.StartsWith("escalation:task_overdue")));
    }

    [Fact]
    public async Task TaskSweep_ReleasesScheduledMessage_Once()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var message = new InternalMessage
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, SenderUserId = h.ManagerUserId,
            Subject = "Gepland", Body = "B", VisibleFrom = Now.UtcDateTime.AddHours(-1),
        };
        h.Db.Context.Add(message);
        h.Db.Context.Add(new InternalMessageRecipient
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, MessageId = message.Id, UserId = h.EmployeeUserId,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.TaskSweep().SweepTenantAsync(h.TenantId, CancellationToken.None);
        await h.TaskSweep().SweepTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Single(await h.Notifications("internal_message"));
        Assert.NotNull((await h.Db.Context.Set<InternalMessage>().SingleAsync()).NotifiedAt);
    }

    [Fact]
    public async Task NotificationMaintenance_ArchivesExpired_AndPurgesOldArchive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Notifications.AddRange(
            new Notification
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.ManagerUserId, Type = "t",
                Title = "Verlopen", Message = "x", ExpiresAt = Now.UtcDateTime.AddHours(-1),
            },
            new Notification
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.ManagerUserId, Type = "t",
                Title = "Oud archief", Message = "x", IsArchived = true,
                ArchivedAt = Now.UtcDateTime.AddDays(-200),
            },
            new Notification
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.ManagerUserId, Type = "t",
                Title = "Actueel", Message = "x",
            });
        await h.Db.Context.SaveChangesAsync();

        var worker = new NotificationMaintenanceWorker(h.Db.Context, h.Clock);
        var (archived, purged) = await worker.RunAsync(CancellationToken.None);

        Assert.Equal(1, archived);
        Assert.Equal(1, purged);
        var remaining = await h.Db.Context.Notifications.ToListAsync();
        Assert.Equal(2, remaining.Count); // gepurgde rij is soft-deleted en dus uit de queryfilter
        Assert.Contains(remaining, n => n.Title == "Verlopen" && n.IsArchived);
    }

    [Fact]
    public async Task EscalationPolicies_MaterialiseDefaults_AndValidateUpdates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var user = new DevCurrentUserContext(h.ManagerUserId);
        var service = new EscalationPolicyService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, user));

        var policies = await service.ListAsync(CancellationToken.None);
        Assert.Equal(Enum.GetValues<EscalationKind>().Length, policies.Count);
        Assert.True(policies.Single(p => p.Kind == EscalationKind.TaskOverdue).IsActive);
        Assert.False(policies.Single(p => p.Kind == EscalationKind.ReturnOverdue).IsActive);

        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(() =>
            service.UpdateAsync(EscalationKind.TaskOverdue,
                new SaveEscalationPolicyRequest(24, "bestaat.niet", true), CancellationToken.None));

        var updated = await service.UpdateAsync(EscalationKind.TaskOverdue,
            new SaveEscalationPolicyRequest(24, PermissionCodes.TasksViewAll, false), CancellationToken.None);
        Assert.False(updated!.IsActive);
        Assert.Equal(24, updated.DelayHours);
    }
}
