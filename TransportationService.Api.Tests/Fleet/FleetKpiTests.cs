using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class FleetKpiTests
{
    private sealed record Harness(SqliteTestDbContext Db, FleetKpiService Sut, Guid TenantId, Guid VehicleId, Guid TrailerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1", IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-1", LicensePlate = "1-T-1", IsActive = true });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new FleetKpiService(db.Context, new DevTenantContext(tenantId)), tenantId, vehicleId, trailerId);
    }

    [Fact]
    public async Task Vehicle_WithFuelData_ComputesActualAndEstimatedKpis()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.FuelTransactions.AddRange(
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new(2026, 1, 5), Litres = 100m, TotalAmount = 180m, OdometerKm = 10000 },
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new(2026, 2, 5), Litres = 120m, TotalAmount = 216m, OdometerKm = 10800 });
        h.Db.Context.Trips.Add(new Trip { Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1", VehicleId = h.VehicleId, TripDate = new(2026, 1, 10), Status = TripStatus.Completed });
        await h.Db.Context.SaveChangesAsync();

        var kpi = await h.Sut.GetVehicleKpiAsync(h.VehicleId, new(2026, 1, 1), new(2026, 3, 1), CancellationToken.None);

        Assert.NotNull(kpi);
        Assert.Equal(1, kpi!.Values.Single(v => v.Key == "trips").Value);
        Assert.Equal(220m, kpi.Values.Single(v => v.Key == "fuel_litres").Value);
        var km = kpi.Values.Single(v => v.Key == "km");
        Assert.Equal(800, km.Value);
        Assert.Equal(KpiQuality.Estimated, km.Quality);
        Assert.Equal(KpiQuality.Estimated, kpi.Values.Single(v => v.Key == "consumption").Quality);
    }

    [Fact]
    public async Task Vehicle_WithoutFuel_ReportsUnavailable_NotFakeZero()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var kpi = await h.Sut.GetVehicleKpiAsync(h.VehicleId, new(2026, 1, 1), new(2026, 12, 31), CancellationToken.None);

        var litres = kpi!.Values.Single(v => v.Key == "fuel_litres");
        Assert.Null(litres.Value);
        Assert.Equal(KpiQuality.Unavailable, litres.Quality);
    }

    [Fact]
    public async Task Trailer_HasNoFuelKpis()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Trips.Add(new Trip { Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-1", TrailerId = h.TrailerId, TripDate = new(2026, 1, 10), Status = TripStatus.Completed });
        await h.Db.Context.SaveChangesAsync();

        var kpi = await h.Sut.GetTrailerKpiAsync(h.TrailerId, new(2026, 1, 1), new(2026, 12, 31), CancellationToken.None);

        Assert.NotNull(kpi);
        Assert.DoesNotContain(kpi!.Values, v => v.Key == "fuel_litres" || v.Key == "consumption");
        Assert.Equal(1, kpi.Values.Single(v => v.Key == "assigned_days").Value);
    }

    [Fact]
    public async Task Maintenance_AndDamage_AreAggregated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, Description = "Service",
            Status = MaintenanceStatus.Completed, CompletedDate = new(2026, 2, 1), Cost = 500m,
        });
        h.Db.Context.DamageReports.Add(new DamageReport
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, IncidentDate = new(2026, 3, 1),
            Description = "Bumper", RepairCost = 350m, DowntimeDays = 2,
        });
        await h.Db.Context.SaveChangesAsync();

        var kpi = await h.Sut.GetVehicleKpiAsync(h.VehicleId, new(2026, 1, 1), new(2026, 12, 31), CancellationToken.None);

        Assert.Equal(500m, kpi!.Values.Single(v => v.Key == "maintenance_cost").Value);
        Assert.Equal(350m, kpi.Values.Single(v => v.Key == "damage_cost").Value);
        Assert.Equal(2, kpi.Values.Single(v => v.Key == "downtime").Value);
    }

    [Fact]
    public async Task UnknownVehicle_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Null(await h.Sut.GetVehicleKpiAsync(Guid.NewGuid(), new(2026, 1, 1), new(2026, 12, 31), CancellationToken.None));
    }
}
