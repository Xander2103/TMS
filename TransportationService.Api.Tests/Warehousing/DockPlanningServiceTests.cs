using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Dtos;
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Modules.Warehousing.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Warehousing;

/// <summary>
/// Dock planning: backend-enforced conflicts (overlap, opening hours, dock compatibility),
/// the controlled appointment status machine with physical timestamps, override-with-reason,
/// queue ordering, utilization and tenant isolation.
/// </summary>
public class DockPlanningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 20);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid WarehouseId, Guid DockId, Guid SecondDockId)
    {
        public DockPlanningService Sut(Guid? tenantOverride = null)
        {
            var tenant = new DevTenantContext(tenantOverride ?? TenantId);
            return new DockPlanningService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var dockId = Guid.NewGuid();
        var secondDockId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Depot Antwerpen", City = "Antwerpen", IsActive = true,
        });
        db.Context.Warehouses.Add(new Warehouse
        {
            Id = warehouseId, TenantId = tenantId, Name = "Magazijn Noord", LocationId = locationId,
            IsActive = true, OpensAt = new TimeOnly(6, 0), ClosesAt = new TimeOnly(20, 0),
        });
        db.Context.Docks.AddRange(
            new Dock
            {
                Id = dockId, TenantId = tenantId, WarehouseId = warehouseId, Code = "D1",
                AllowsLoading = true, AllowsUnloading = false, IsActive = true,
            },
            new Dock
            {
                Id = secondDockId, TenantId = tenantId, WarehouseId = warehouseId, Code = "D2",
                AllowsLoading = true, AllowsUnloading = true, IsActive = true,
            });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, warehouseId, dockId, secondDockId);
    }

    private static SaveDockAppointmentRequest Request(
        Harness h, Guid? dockId, int startHour, int endHour,
        DockOperationType operation = DockOperationType.Loading,
        Guid? version = null, bool @override = false, string? reason = null) =>
        new(h.WarehouseId, dockId, operation,
            Today.ToDateTime(new TimeOnly(startHour, 0), DateTimeKind.Utc),
            Today.ToDateTime(new TimeOnly(endHour, 0), DateTimeKind.Utc),
            Version: version, Override: @override, OverrideReason: reason);

    [Fact]
    public async Task Create_Succeeds_AndOverlapIsBlocked_UntilOverriddenWithReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var first = await sut.CreateAsync(Request(h, h.DockId, 8, 10), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.Success, first.Outcome);

        var overlapping = await sut.CreateAsync(Request(h, h.DockId, 9, 11), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.ConflictsBlock, overlapping.Outcome);
        Assert.Contains(overlapping.Conflicts!, c => c.Code == "DockOverlap");

        var noReason = await sut.CreateAsync(Request(h, h.DockId, 9, 11, @override: true), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.ValidationFailed, noReason.Outcome);

        var overridden = await sut.CreateAsync(
            Request(h, h.DockId, 9, 11, @override: true, reason: "Spoedlading klant X."), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.Success, overridden.Outcome);
        Assert.Contains(h.Db.Context.ConflictOverrides, o => o.EntityType == "DockAppointment");
    }

    [Fact]
    public async Task Conflicts_CoverOpeningHours_DockType_InactiveDock_AndDuration()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        var outside = await sut.CreateAsync(Request(h, h.DockId, 4, 5), CancellationToken.None);
        Assert.Contains(outside.Conflicts!, c => c.Code == "OutsideOpeningHours");

        var wrongType = await sut.CreateAsync(
            Request(h, h.DockId, 8, 9, DockOperationType.Unloading), CancellationToken.None);
        Assert.Contains(wrongType.Conflicts!, c => c.Code == "DockTypeMismatch");

        var dock = h.Db.Context.Docks.Single(d => d.Id == h.DockId);
        dock.IsActive = false;
        await h.Db.Context.SaveChangesAsync();
        var inactive = await sut.CreateAsync(Request(h, h.DockId, 8, 9), CancellationToken.None);
        Assert.Contains(inactive.Conflicts!, c => c.Code == "DockInactive");

        var tooShort = await sut.CreateAsync(
            new SaveDockAppointmentRequest(h.WarehouseId, h.SecondDockId, DockOperationType.Loading,
                Today.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc),
                Today.ToDateTime(new TimeOnly(8, 10), DateTimeKind.Utc)), CancellationToken.None);
        Assert.Contains(tooShort.Conflicts!, c => c.Code == "DurationTooShort" && !c.OverrideAllowed);
    }

    [Fact]
    public async Task StatusMachine_EnforcesTransitions_AndStampsTimestamps()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var appointment = (await sut.CreateAsync(Request(h, h.SecondDockId, 8, 10), CancellationToken.None)).Appointment!;

        // Planned → InProgress is a forbidden jump.
        var jump = await sut.ChangeStatusAsync(appointment.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.InProgress), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.InvalidState, jump.Outcome);

        var arrived = await sut.ChangeStatusAsync(appointment.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.Arrived), CancellationToken.None);
        Assert.Equal(Now.UtcDateTime, arrived.Appointment!.ArrivedAt);

        await sut.ChangeStatusAsync(appointment.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.AssignedToDock), CancellationToken.None);
        var started = await sut.ChangeStatusAsync(appointment.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.InProgress), CancellationToken.None);
        Assert.Equal(Now.UtcDateTime, started.Appointment!.StartedAt);

        var completed = await sut.ChangeStatusAsync(appointment.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.Completed), CancellationToken.None);
        Assert.Equal(Now.UtcDateTime, completed.Appointment!.CompletedAt);
        Assert.Empty(completed.Appointment.AllowedTransitions);

        // Terminal appointments refuse edits.
        var editAfter = await sut.UpdateAsync(appointment.Id, Request(h, h.SecondDockId, 9, 11), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.InvalidState, editAfter.Outcome);
    }

    [Fact]
    public async Task Queue_OrdersByPriorityThenArrival_AndNoShowWorks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();

        // Two queue entries without a dock; one urgent.
        var normal = (await sut.CreateAsync(
            Request(h, null, 8, 9) with { Priority = Modules.Orders.Entities.OrderPriority.Normal },
            CancellationToken.None)).Appointment!;
        var urgent = (await sut.CreateAsync(
            Request(h, null, 8, 9) with { Priority = Modules.Orders.Entities.OrderPriority.Urgent },
            CancellationToken.None)).Appointment!;
        await sut.ChangeStatusAsync(normal.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.Arrived), CancellationToken.None);
        await sut.ChangeStatusAsync(urgent.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.Arrived), CancellationToken.None);

        // A third appointment that never shows up.
        var ghost = (await sut.CreateAsync(Request(h, h.SecondDockId, 10, 11), CancellationToken.None)).Appointment!;
        await sut.ChangeStatusAsync(ghost.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.Expected), CancellationToken.None);
        var noShow = await sut.ChangeStatusAsync(ghost.Id,
            new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.NoShow), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.Success, noShow.Outcome);

        var board = await sut.GetBoardAsync(h.WarehouseId, Today, CancellationToken.None);
        Assert.Equal([urgent.Id, normal.Id], board!.Queue.Select(q => q.Id).ToArray());

        var dashboard = await sut.GetDashboardAsync(h.WarehouseId, Today, CancellationToken.None);
        Assert.Equal(1, dashboard!.NoShows);
        Assert.Equal(2, dashboard.Waiting);
    }

    [Fact]
    public async Task Utilization_ReflectsBookedMinutes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        await sut.CreateAsync(Request(h, h.DockId, 8, 15), CancellationToken.None); // 7h op 14h venster

        var dashboard = await sut.GetDashboardAsync(h.WarehouseId, Today, CancellationToken.None);

        var d1 = dashboard!.Utilization.Single(u => u.DockCode == "D1");
        Assert.Equal(420, d1.BookedMinutes);
        Assert.Equal(840, d1.OpenMinutes);
        Assert.Equal(50.0m, d1.UtilizationPct);
    }

    [Fact]
    public async Task StaleVersion_And_TenantIsolation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.Sut();
        var appointment = (await sut.CreateAsync(Request(h, h.SecondDockId, 8, 10), CancellationToken.None)).Appointment!;

        var moved = await sut.UpdateAsync(appointment.Id,
            Request(h, h.SecondDockId, 11, 13, version: appointment.Version), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.Success, moved.Outcome);

        var stale = await sut.UpdateAsync(appointment.Id,
            Request(h, h.SecondDockId, 14, 16, version: appointment.Version), CancellationToken.None);
        Assert.Equal(DockOperationOutcome.StaleVersion, stale.Outcome);

        // Foreign tenant: board 404s and the appointment is unreachable.
        var foreign = h.Sut(Guid.NewGuid());
        Assert.Null(await foreign.GetBoardAsync(h.WarehouseId, Today, CancellationToken.None));
        Assert.Equal(DockOperationOutcome.NotFound,
            (await foreign.ChangeStatusAsync(appointment.Id,
                new ChangeDockAppointmentStatusRequest(DockAppointmentStatus.Arrived), CancellationToken.None)).Outcome);
    }
}
