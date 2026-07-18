using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class DamageReportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, DamageReportService Sut, Guid TenantId, Guid VehicleId, Guid TrailerId, Guid DriverId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1", IsActive = true });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new DamageReportService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, sut, tenantId, vehicleId, trailerId, driverId);
    }

    private static CreateDamageReportRequest Request(Guid? driverId = null) => new(
        driverId, new DateOnly(2026, 7, 15), "E313 Antwerpen-Oost", "Spiegel afgereden bij laden",
        DamageSeverity.Minor, "VERZ-2026-001", null);

    [Fact]
    public async Task Create_ForVehicle_ResolvesDriverName_AndStartsReported()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(h.DriverId), CancellationToken.None);

        Assert.Equal(DamageOperationOutcome.Success, result.Outcome);
        Assert.Equal(DamageStatus.Reported, result.Report!.Status);
        Assert.Equal("Jan Jansen", result.Report.DriverName);
    }

    [Fact]
    public async Task Create_WithForeignDriver_ReturnsInvalidReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var foreignTenant = Guid.NewGuid();
        var foreignEmployee = Guid.NewGuid();
        var foreignDriver = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Employees.Add(new Employee { Id = foreignEmployee, TenantId = foreignTenant, EmployeeNumber = "X", FirstName = "F", LastName = "D", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Drivers.Add(new Driver { Id = foreignDriver, TenantId = foreignTenant, DriverNumber = "CH-X", EmployeeId = foreignEmployee, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(foreignDriver), CancellationToken.None);

        Assert.Equal(DamageOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task Create_EmptyDescription_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId,
            Request() with { Description = "  " }, CancellationToken.None);

        Assert.Equal(DamageOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Update_ProgressesStatusAndRecordsCost()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForTrailerAsync(h.TrailerId, Request(), CancellationToken.None);

        var updated = await h.Sut.UpdateAsync(created.Report!.Id, new UpdateDamageReportRequest(
            null, created.Report.IncidentDate, created.Report.Location, created.Report.Description,
            DamageSeverity.Moderate, DamageStatus.InRepair, "VERZ-2026-001", 1250.75m, 3, "Hersteller ingepland"), CancellationToken.None);

        Assert.Equal(DamageOperationOutcome.Success, updated.Outcome);
        Assert.Equal(DamageStatus.InRepair, updated.Report!.Status);
        Assert.Equal(1250.75m, updated.Report.RepairCost);
        Assert.Equal(3, updated.Report.DowntimeDays);
    }

    [Fact]
    public async Task ListRecent_NewestFirst_WithOwnerInfo_AndTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.CreateForVehicleAsync(h.VehicleId, Request() with { IncidentDate = new DateOnly(2026, 7, 1) }, CancellationToken.None);
        await h.Sut.CreateForTrailerAsync(h.TrailerId, Request() with { IncidentDate = new DateOnly(2026, 7, 10) }, CancellationToken.None);

        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        h.Db.Context.DamageReports.Add(new DamageReport
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle,
            IncidentDate = new DateOnly(2026, 7, 17), Description = "geheim",
        });
        await h.Db.Context.SaveChangesAsync();

        var recent = await h.Sut.ListRecentAsync(10, CancellationToken.None);

        Assert.Equal(2, recent.Count);
        Assert.Equal(new DateOnly(2026, 7, 10), recent[0].IncidentDate);
        Assert.Equal("OPL-0001", recent[0].OwnerNumber);
        Assert.Equal("VRT-0001", recent[1].OwnerNumber);
    }

    [Fact]
    public async Task ListForVehicle_ForeignVehicle_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Null(await h.Sut.ListForVehicleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_SoftDeletes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, Request(), CancellationToken.None);

        Assert.True(await h.Sut.DeleteAsync(created.Report!.Id, CancellationToken.None));
        var reports = await h.Sut.ListForVehicleAsync(h.VehicleId, CancellationToken.None);
        Assert.Empty(reports!);
    }
}
