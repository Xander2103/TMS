using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Planning;

public class VehicleStatusConflictTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, PlanningConflictService Sut, Guid TenantId, Guid VehicleId);

    private static async Task<Harness> SeedAsync(VehicleOperationalStatus status)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1",
            OperationalStatus = status, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var sut = new PlanningConflictService(db.Context, new DevTenantContext(tenantId),
            new QualificationStatusCalculator(), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId);
    }

    private static Trip TripFor(Harness h) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1",
        TripDate = new DateOnly(2026, 7, 20), VehicleId = h.VehicleId, Status = TripStatus.Draft,
    };

    [Fact]
    public async Task AvailableVehicle_ProducesNoStatusConflict()
    {
        var h = await SeedAsync(VehicleOperationalStatus.Available);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.VehicleNotOperational);
    }

    [Fact]
    public async Task VehicleInMaintenance_IsABlockingConflict()
    {
        var h = await SeedAsync(VehicleOperationalStatus.InMaintenance);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.VehicleNotOperational);
        Assert.True(conflict.Blocking);
    }

    [Fact]
    public async Task VehicleOutOfService_IsABlockingConflict()
    {
        var h = await SeedAsync(VehicleOperationalStatus.OutOfService);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.VehicleNotOperational);
        Assert.True(conflict.Blocking);
    }

    [Fact]
    public async Task VehicleInUse_IsOnlyAWarning()
    {
        var h = await SeedAsync(VehicleOperationalStatus.InUse);
        using var _ = h.Db;

        var conflicts = await h.Sut.EvaluateAsync(TripFor(h), CancellationToken.None);

        var conflict = Assert.Single(conflicts, c => c.Code == PlanningConflictCode.VehicleNotOperational);
        Assert.False(conflict.Blocking);
    }
}
