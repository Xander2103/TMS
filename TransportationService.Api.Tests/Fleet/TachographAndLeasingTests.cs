using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class TachographAndLeasingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 6, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, TachographService Tacho, LeasingContractService Leasing, Guid TenantId, Guid VehicleId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-ABC-123", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var storage = new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N")));
        return new Harness(db,
            new TachographService(db.Context, tenant, audit, new TestClock(Now), storage),
            new LeasingContractService(db.Context, tenant, audit, storage),
            tenantId, vehicleId);
    }

    [Fact]
    public async Task Tachograph_Status_ReflectsDueDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = DateOnly.FromDateTime(Now.UtcDateTime);

        var valid = await h.Tacho.CreateAsync(h.VehicleId, Request(today, today.AddDays(200)), CancellationToken.None);
        Assert.Equal(TachographStatus.Valid, valid!.Status);

        var soon = await h.Tacho.CreateAsync(h.VehicleId, Request(today, today.AddDays(30)), CancellationToken.None);
        Assert.Equal(TachographStatus.ExpiringSoon, soon!.Status);

        var overdue = await h.Tacho.CreateAsync(h.VehicleId, Request(today.AddDays(-800), today.AddDays(-10)), CancellationToken.None);
        Assert.Equal(TachographStatus.Overdue, overdue!.Status);
    }

    [Fact]
    public async Task Tachograph_OverdueVehicles_UsesLatestCalibration()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        await h.Tacho.CreateAsync(h.VehicleId, Request(today.AddDays(-800), today.AddDays(-10)), CancellationToken.None); // old, overdue
        await h.Tacho.CreateAsync(h.VehicleId, Request(today, today.AddDays(300)), CancellationToken.None); // fresh

        var overdue = await h.Tacho.OverdueVehicleIdsAsync(CancellationToken.None);
        Assert.DoesNotContain(h.VehicleId, overdue); // latest calibration is valid
    }

    [Fact]
    public async Task Tachograph_InvalidDueDate_Throws()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Tacho.CreateAsync(h.VehicleId, Request(today, today.AddDays(-1)), CancellationToken.None));
    }

    [Fact]
    public async Task Leasing_FinancialFields_RedactedWithoutPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Leasing.CreateForVehicleAsync(h.VehicleId, new SaveLeasingContractRequest(
            "LeasePlan", "C-123", new DateOnly(2025, 1, 1), new DateOnly(2028, 1, 1),
            1250.50m, "EUR", 120000, 360000, "An", null, true), includeFinance: true, CancellationToken.None);
        Assert.Equal(1250.50m, created.MonthlyAmount);

        var redacted = (await h.Leasing.ListForVehicleAsync(h.VehicleId, includeFinance: false, CancellationToken.None))!.Single();
        Assert.Null(redacted.MonthlyAmount);
        Assert.Null(redacted.KilometerAllowancePerYear);
        Assert.Equal("LeasePlan", redacted.LeasingCompany); // metadata still visible

        var withFinance = (await h.Leasing.ListForVehicleAsync(h.VehicleId, includeFinance: true, CancellationToken.None))!.Single();
        Assert.Equal(1250.50m, withFinance.MonthlyAmount);
    }

    [Fact]
    public async Task Leasing_RequiresCompany()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Leasing.CreateForVehicleAsync(h.VehicleId, new SaveLeasingContractRequest(
                " ", null, null, null, null, null, null, null, null, null, true), includeFinance: true, CancellationToken.None));
    }

    private static SaveTachographCalibrationRequest Request(DateOnly calibration, DateOnly nextDue) =>
        new("Digitaal", "VDO", "DTCO", "SN-1", calibration, nextDue, "Keurstation", "CERT-1", "SEAL-1", 100000, 3120, null);
}
