using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class FleetDashboardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, FleetDashboardService Sut, Guid TenantId, Guid VehicleId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.AddRange(
            new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true },
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantId, InternalNumber = "VRT-0002", LicensePlate = "1-A-2", OperationalStatus = VehicleOperationalStatus.InMaintenance, IsActive = true },
            new Vehicle { Id = Guid.NewGuid(), TenantId = tenantId, InternalNumber = "VRT-0003", LicensePlate = "1-A-3", OperationalStatus = VehicleOperationalStatus.OutOfService, IsActive = false });
        db.Context.Trailers.Add(new Trailer { Id = Guid.NewGuid(), TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(Now);
        var sut = new FleetDashboardService(
            db.Context,
            tenant,
            new MaintenanceService(db.Context, tenant, audit, clock),
            new InspectionService(db.Context, tenant, audit, clock),
            new FleetDocumentService(db.Context, tenant, audit, clock, new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ts-tests", System.Guid.NewGuid().ToString("N")))),
            new DamageReportService(db.Context, tenant, audit),
            new FuelService(db.Context, tenant, audit));
        return new Harness(db, sut, tenantId, vehicleId);
    }

    [Fact]
    public async Task Get_CountsAssetsByStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var dashboard = await h.Sut.GetAsync(CancellationToken.None);

        Assert.Equal(3, dashboard.Vehicles.Total);
        Assert.Equal(1, dashboard.Vehicles.Available);
        Assert.Equal(1, dashboard.Vehicles.InMaintenance);
        Assert.Equal(1, dashboard.Vehicles.OutOfService);
        Assert.Equal(1, dashboard.Vehicles.Inactive);
        Assert.Equal(1, dashboard.Trailers.Total);
        Assert.Equal(1, dashboard.Trailers.Available);
    }

    [Fact]
    public async Task Get_ComposesDueAndWarningFeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId,
            Description = "Grote beurt", ScheduledDate = new DateOnly(2026, 7, 25),
        });
        h.Db.Context.Inspections.Add(new Inspection
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId,
            InspectionType = InspectionType.VehicleInspection, DueDate = new DateOnly(2026, 7, 30),
        });
        h.Db.Context.FleetDocuments.Add(new FleetDocument
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId,
            DocumentType = FleetDocumentType.Insurance, ExpiryDate = new DateOnly(2026, 8, 1),
        });
        h.Db.Context.DamageReports.Add(new DamageReport
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId,
            IncidentDate = new DateOnly(2026, 7, 15), Description = "Spiegel", Status = DamageStatus.Reported,
        });
        h.Db.Context.FuelTransactions.AddRange(
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new(2026, 7, 1), Litres = 300m, TotalAmount = 450m, OdometerKm = 81_000 },
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new(2026, 7, 8), Litres = 300m, TotalAmount = 450m, OdometerKm = 80_000 });
        await h.Db.Context.SaveChangesAsync();

        var dashboard = await h.Sut.GetAsync(CancellationToken.None);

        Assert.Equal(1, dashboard.MaintenanceDueCount);
        Assert.Equal("VRT-0001", dashboard.MaintenanceDue[0].OwnerNumber);
        Assert.Equal(1, dashboard.InspectionsDueCount);
        Assert.Equal(1, dashboard.DocumentsExpiringCount);
        Assert.Equal(1, dashboard.OpenDamageCount);
        Assert.Single(dashboard.RecentDamage);
        Assert.Single(dashboard.FuelWarnings);
    }

    [Fact]
    public async Task Get_OpenDamage_ExcludesRepairedAndClosed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.DamageReports.AddRange(
            new DamageReport { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, IncidentDate = new(2026, 7, 1), Description = "a", Status = DamageStatus.InRepair },
            new DamageReport { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, IncidentDate = new(2026, 7, 2), Description = "b", Status = DamageStatus.Repaired },
            new DamageReport { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, IncidentDate = new(2026, 7, 3), Description = "c", Status = DamageStatus.Closed });
        await h.Db.Context.SaveChangesAsync();

        var dashboard = await h.Sut.GetAsync(CancellationToken.None);

        Assert.Equal(1, dashboard.OpenDamageCount);
    }

    [Fact]
    public async Task Get_IgnoresForeignTenantData()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        h.Db.Context.DamageReports.Add(new DamageReport
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle,
            IncidentDate = new(2026, 7, 15), Description = "geheim", Status = DamageStatus.Reported,
        });
        await h.Db.Context.SaveChangesAsync();

        var dashboard = await h.Sut.GetAsync(CancellationToken.None);

        Assert.Equal(3, dashboard.Vehicles.Total);
        Assert.Equal(0, dashboard.OpenDamageCount);
        Assert.Empty(dashboard.RecentDamage);
    }
}
