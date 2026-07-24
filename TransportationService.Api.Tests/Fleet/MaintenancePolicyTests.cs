using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class MaintenancePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, MaintenancePolicyService Sut, Guid TenantId, Guid VehicleId, Guid CategoryId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.VehicleCategories.Add(new VehicleCategory
        {
            Id = categoryId, TenantId = tenantId, Code = "TREK", Name = "Trekker", IsActive = true,
        });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-ABC-123",
            CategoryId = categoryId, OdometerKm = 100000, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new MaintenancePolicyService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId, categoryId);
    }

    private static SaveMaintenancePolicyRequest Policy(
        MaintenancePolicyKind kind = MaintenancePolicyKind.Maintenance,
        FleetAssetKind assetKind = FleetAssetKind.Vehicle,
        Guid? categoryId = null, Guid? vehicleId = null,
        int? months = 6, int? km = null, int warningDays = 30) =>
        new(kind, assetKind, categoryId, vehicleId, null, months, km, warningDays, null);

    [Fact]
    public async Task Resolve_PrecedenceIsAssetThenCategoryThenCompanyDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Policy(months: 12), CancellationToken.None);                          // company default
        await h.Sut.CreateAsync(Policy(months: 6, categoryId: h.CategoryId), CancellationToken.None); // category
        await h.Sut.CreateAsync(Policy(months: 3, vehicleId: h.VehicleId), CancellationToken.None);   // asset override

        var resolved = await h.Sut.ResolveAsync(MaintenancePolicyKind.Maintenance, FleetAssetKind.Vehicle, h.VehicleId, h.CategoryId, CancellationToken.None);
        Assert.Equal(MaintenancePolicyLevel.Asset, resolved!.Level);
        Assert.Equal(3, resolved.IntervalMonths);

        // Another vehicle in the same category → category rule wins.
        var other = await h.Sut.ResolveAsync(MaintenancePolicyKind.Maintenance, FleetAssetKind.Vehicle, Guid.NewGuid(), h.CategoryId, CancellationToken.None);
        Assert.Equal(MaintenancePolicyLevel.Category, other!.Level);
        Assert.Equal(6, other.IntervalMonths);

        // No category → company default.
        var fallback = await h.Sut.ResolveAsync(MaintenancePolicyKind.Maintenance, FleetAssetKind.Vehicle, Guid.NewGuid(), null, CancellationToken.None);
        Assert.Equal(MaintenancePolicyLevel.CompanyDefault, fallback!.Level);
        Assert.Equal(12, fallback.IntervalMonths);
    }

    [Fact]
    public async Task ApplyDefaults_PlansMaintenanceAndInspection_Idempotently()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Policy(months: 6, km: 30000), CancellationToken.None);
        await h.Sut.CreateAsync(Policy(kind: MaintenancePolicyKind.Inspection, months: 12, warningDays: 60), CancellationToken.None);

        await h.Sut.ApplyDefaultsAsync(FleetAssetKind.Vehicle, h.VehicleId, h.CategoryId, currentOdometerKm: 100000, CancellationToken.None);

        var job = Assert.Single(h.Db.Context.MaintenanceRecords);
        Assert.Equal(new DateOnly(2027, 1, 20), job.ScheduledDate);
        Assert.Equal(130000, job.OdometerTriggerKm);
        Assert.Equal(6, job.IntervalMonths);

        var inspection = Assert.Single(h.Db.Context.Inspections);
        Assert.Equal(new DateOnly(2027, 7, 20), inspection.DueDate);
        Assert.Equal(60, inspection.WarningDays);

        // Second run (retry) creates no duplicates.
        await h.Sut.ApplyDefaultsAsync(FleetAssetKind.Vehicle, h.VehicleId, h.CategoryId, currentOdometerKm: 100000, CancellationToken.None);
        Assert.Single(h.Db.Context.MaintenanceRecords);
        Assert.Single(h.Db.Context.Inspections);
    }

    [Fact]
    public async Task ApplyDefaults_WithoutApplicablePolicy_CreatesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Sut.ApplyDefaultsAsync(FleetAssetKind.Vehicle, h.VehicleId, h.CategoryId, 100000, CancellationToken.None);

        Assert.Empty(h.Db.Context.MaintenanceRecords);
        Assert.Empty(h.Db.Context.Inspections);
    }

    [Fact]
    public async Task Validation_RejectsMissingInterval_KmForTrailers_AndDoubleLevels()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noInterval = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(Policy(months: null, km: null), CancellationToken.None));
        Assert.Contains("intervalMonths", noInterval.FieldErrors!.Keys);

        var kmOnTrailer = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(Policy(assetKind: FleetAssetKind.Trailer, months: null, km: 30000), CancellationToken.None));
        Assert.Contains("intervalKm", kmOnTrailer.FieldErrors!.Keys);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(Policy(categoryId: h.CategoryId, vehicleId: h.VehicleId), CancellationToken.None));
    }

    [Fact]
    public async Task GetEffective_LabelsSource_AndFallsBackAfterOverrideRemoval()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Policy(months: 12), CancellationToken.None);
        await h.Sut.CreateAsync(Policy(months: 6, categoryId: h.CategoryId), CancellationToken.None);
        await h.Sut.CreateAsync(Policy(kind: MaintenancePolicyKind.Inspection, months: 24), CancellationToken.None);
        var overridePolicy = await h.Sut.CreateAsync(Policy(months: 3, vehicleId: h.VehicleId), CancellationToken.None);

        var effective = await h.Sut.GetEffectiveAsync(FleetAssetKind.Vehicle, h.VehicleId, CancellationToken.None);
        Assert.NotNull(effective);
        Assert.Equal(MaintenancePolicyLevel.Asset, effective!.Maintenance!.Level);
        Assert.Equal("Specifieke regel voor voertuig", effective.Maintenance.SourceLabel);
        Assert.Equal(3, effective.Maintenance.IntervalMonths);
        Assert.Equal(MaintenancePolicyLevel.CompanyDefault, effective.Inspection!.Level);
        Assert.Equal("Bedrijfsstandaard", effective.Inspection.SourceLabel);

        // "Gebruik opnieuw categorie-/bedrijfsstandaard": removing the override re-inherits.
        await h.Sut.DeleteAsync(overridePolicy.Id, CancellationToken.None);
        var inherited = await h.Sut.GetEffectiveAsync(FleetAssetKind.Vehicle, h.VehicleId, CancellationToken.None);
        Assert.Equal(MaintenancePolicyLevel.Category, inherited!.Maintenance!.Level);
        Assert.Equal("Overgenomen van categorie Trekker", inherited.Maintenance.SourceLabel);
        Assert.Equal(6, inherited.Maintenance.IntervalMonths);
    }

    [Fact]
    public async Task GetEffective_UnknownOrForeignAsset_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Null(await h.Sut.GetEffectiveAsync(FleetAssetKind.Vehicle, Guid.NewGuid(), CancellationToken.None))
;
        // Same asset id queried from another tenant's context is invisible.
        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var otherSut = new MaintenancePolicyService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)), new TestClock(Now));
        Assert.Null(await otherSut.GetEffectiveAsync(FleetAssetKind.Vehicle, h.VehicleId, CancellationToken.None));
    }
}
