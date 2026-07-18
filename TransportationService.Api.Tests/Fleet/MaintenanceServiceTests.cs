using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class MaintenanceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    private sealed record Harness(SqliteTestDbContext Db, MaintenanceService Sut, Guid TenantId, Guid VehicleId, Guid TrailerId);

    private static async Task<Harness> SeedAsync(int vehicleOdometerKm = 100_000)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", OdometerKm = vehicleOdometerKm, IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new MaintenanceService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId, trailerId);
    }

    private static CreateMaintenanceRequest Request(
        DateOnly? scheduled = null, int? odometerTrigger = null, int? intervalMonths = null, int? intervalKm = null) =>
        new(MaintenanceType.PeriodicService, null, "Groot onderhoud", scheduled, odometerTrigger, "Garage Janssens", intervalMonths, intervalKm, null);

    [Fact]
    public async Task Create_PlansJob_ForVehicle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(scheduled: new DateOnly(2026, 9, 1)), CancellationToken.None);

        Assert.Equal(MaintenanceOperationOutcome.Success, result.Outcome);
        Assert.Equal(MaintenanceStatus.Planned, result.Record!.Status);
        Assert.False(result.Record.IsOverdue);
    }

    [Fact]
    public async Task Create_OdometerTriggerOnTrailer_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForTrailerAsync(h.TrailerId, Request(odometerTrigger: 50_000), CancellationToken.None);

        Assert.Equal(MaintenanceOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Overdue_WhenScheduledDatePassed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(scheduled: new DateOnly(2026, 7, 1)), CancellationToken.None);

        Assert.True(result.Record!.IsOverdue);
    }

    [Fact]
    public async Task Overdue_WhenOdometerTriggerReached()
    {
        var h = await SeedAsync(vehicleOdometerKm: 120_000);
        using var _ = h.Db;

        var reached = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(odometerTrigger: 110_000), CancellationToken.None);
        var notReached = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(odometerTrigger: 150_000), CancellationToken.None);

        Assert.True(reached.Record!.IsOverdue);
        Assert.False(notReached.Record!.IsOverdue);
    }

    [Fact]
    public async Task Complete_WithInterval_SpawnsFollowUp_AndAdvancesVehicleOdometer()
    {
        var h = await SeedAsync(vehicleOdometerKm: 100_000);
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId,
            Request(scheduled: new DateOnly(2026, 7, 10), intervalMonths: 6, intervalKm: 30_000), CancellationToken.None);

        var completed = await h.Sut.CompleteAsync(created.Record!.Id, new CompleteMaintenanceRequest(
            new DateOnly(2026, 7, 15), CompletedOdometerKm: 101_500, "Olie ververst", null, 450.50m, null), CancellationToken.None);

        Assert.Equal(MaintenanceOperationOutcome.Success, completed.Outcome);
        Assert.Equal(MaintenanceStatus.Completed, completed.Record!.Status);
        Assert.Equal(new DateOnly(2027, 1, 15), completed.Record.NextServiceDate);
        Assert.Equal(131_500, completed.Record.NextServiceOdometerKm);

        Assert.NotNull(completed.FollowUp);
        Assert.Equal(MaintenanceStatus.Planned, completed.FollowUp!.Status);
        Assert.Equal(new DateOnly(2027, 1, 15), completed.FollowUp.ScheduledDate);
        Assert.Equal(131_500, completed.FollowUp.OdometerTriggerKm);

        var vehicleOdo = h.Db.Context.Vehicles.Single(v => v.Id == h.VehicleId).OdometerKm;
        Assert.Equal(101_500, vehicleOdo);
    }

    [Fact]
    public async Task Complete_WithoutInterval_NoFollowUp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(scheduled: new DateOnly(2026, 7, 10)), CancellationToken.None);

        var completed = await h.Sut.CompleteAsync(created.Record!.Id, new CompleteMaintenanceRequest(
            new DateOnly(2026, 7, 15), null, null, null, null, null), CancellationToken.None);

        Assert.Equal(MaintenanceOperationOutcome.Success, completed.Outcome);
        Assert.Null(completed.FollowUp);
    }

    [Fact]
    public async Task Complete_Twice_ReturnsAlreadyCompleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(), CancellationToken.None);
        var request = new CompleteMaintenanceRequest(new DateOnly(2026, 7, 15), null, null, null, null, null);
        await h.Sut.CompleteAsync(created.Record!.Id, request, CancellationToken.None);

        var second = await h.Sut.CompleteAsync(created.Record.Id, request, CancellationToken.None);

        Assert.Equal(MaintenanceOperationOutcome.AlreadyCompleted, second.Outcome);
    }

    [Fact]
    public async Task ListDue_ReturnsOverdueFirst_AndSkipsOtherTenants()
    {
        var h = await SeedAsync(vehicleOdometerKm: 120_000);
        using var _ = h.Db;

        await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(scheduled: new DateOnly(2026, 8, 1)), CancellationToken.None);   // upcoming
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(odometerTrigger: 110_000), CancellationToken.None);              // overdue by km
        await h.Sut.CreateForTrailerAsync(h.TrailerId, Request(scheduled: new DateOnly(2027, 6, 1)), CancellationToken.None);   // far future

        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        h.Db.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle,
            MaintenanceType = MaintenanceType.Repair, Description = "geheim", Status = MaintenanceStatus.Planned,
            ScheduledDate = new DateOnly(2026, 7, 1),
        });
        await h.Db.Context.SaveChangesAsync();

        var due = await h.Sut.ListDueAsync(30, CancellationToken.None);

        Assert.Equal(2, due.Count);
        Assert.True(due[0].IsOverdue);
        Assert.Equal(110_000, due[0].OdometerTriggerKm);
        Assert.False(due[1].IsOverdue);
        Assert.All(due, d => Assert.Equal("VRT-0001", d.OwnerNumber));
    }

    [Fact]
    public async Task Create_ForForeignVehicle_ReturnsOwnerNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(Guid.NewGuid(), Request(), CancellationToken.None);

        Assert.Equal(MaintenanceOperationOutcome.OwnerNotFound, result.Outcome);
    }

    [Fact]
    public async Task Delete_SoftDeletes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(), CancellationToken.None);

        Assert.True(await h.Sut.DeleteAsync(created.Record!.Id, CancellationToken.None));
        var records = await h.Sut.ListForVehicleAsync(h.VehicleId, CancellationToken.None);
        Assert.Empty(records!);
    }
}
