using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

/// <summary>
/// Master-data remediation 2026-10-18 — fleet master integrity:
/// MD-M11: a maintenance rule bound to a DELETED vehicle was listed as "Bedrijfsstandaard", and a
/// second active rule for the same category was accepted (resolution then picked either).
/// MD-M16: a trailer could carry a vehicle's plate; a crane vehicle without any crane inspection
/// read "Inzetbaar" while its own page warned "Geen Kraankeuring".
/// </summary>
public class MasterFleetIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, DevTenantContext Tenant, AuditService Audit, Guid CategoryId, Guid VehicleId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, TrailerNumberPrefix = "OPL-", TrailerNumberNextValue = 1 });
        db.Context.VehicleCategories.Add(new VehicleCategory { Id = categoryId, TenantId = tenantId, Code = "BAKWAGEN", Name = "Bakwagen", IsActive = true });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0018", LicensePlate = "1-TMK-001", HasCrane = true, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        return new Harness(db, tenantId, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), categoryId, vehicleId);
    }

    private static SaveMaintenancePolicyRequest Rule(Guid? categoryId = null, Guid? vehicleId = null, int months = 6) =>
        new(MaintenancePolicyKind.Maintenance, FleetAssetKind.Vehicle, categoryId, vehicleId, null, months, null, 30, null);

    [Fact]
    public async Task ADuplicateRuleAtTheSameLevel_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new MaintenancePolicyService(h.Db.Context, h.Tenant, h.Audit, new TestClock(Now));
        await sut.CreateAsync(Rule(categoryId: h.CategoryId), CancellationToken.None);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(Rule(categoryId: h.CategoryId, months: 3), CancellationToken.None));
        Assert.Contains("deze categorie", refused.Message);
        // A company default and a category rule are different levels: both allowed.
        await sut.CreateAsync(Rule(), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(Rule(months: 24), CancellationToken.None));
    }

    [Fact]
    public async Task ARuleOfADeletedVehicle_IsNeverListedAsTheCompanyDefault_NorAppliedToOthers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = new MaintenancePolicyService(h.Db.Context, h.Tenant, h.Audit, new TestClock(Now));
        var deletedVehicle = Guid.NewGuid();
        h.Db.Context.Vehicles.Add(new Vehicle { Id = deletedVehicle, TenantId = h.TenantId, InternalNumber = "VRT-0007", LicensePlate = "1-OLD-007", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        await sut.CreateAsync(Rule(vehicleId: deletedVehicle, months: 12), CancellationToken.None);
        h.Db.Context.Vehicles.Remove(await h.Db.Context.Vehicles.SingleAsync(v => v.Id == deletedVehicle));
        await h.Db.Context.SaveChangesAsync();

        var rule = Assert.Single(await sut.ListAsync(CancellationToken.None));
        Assert.Equal(("Vehicle", true, "VRT-0007"), (rule.Scope, rule.AssetDeleted, rule.VehicleNumber));
        Assert.Null(await sut.ResolveAsync(MaintenancePolicyKind.Maintenance, FleetAssetKind.Vehicle, h.VehicleId, null, CancellationToken.None));
    }

    [Fact]
    public async Task ATrailer_CannotTakeAVehiclesPlate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var trailers = new TrailerService(h.Db.Context, h.Tenant, h.Audit, TimeProvider.System);

        var result = await trailers.CreateAsync(new CreateTrailerRequest(
            "1-tmk-001", null, null, null, null, null, null, 27_000m, null, null, null, null, false, false,
            VehicleOwnershipType.Owned, null), CancellationToken.None);

        Assert.Equal(TrailerOperationOutcome.DuplicateLicensePlate, result.Outcome);
        Assert.Contains("voertuig VRT-0018", result.Message);
    }

    [Fact]
    public async Task ACraneVehicleWithoutAnyCraneInspection_IsNotDeployableForCraneWork()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var readiness = (await new FleetReadinessService(h.Db.Context, h.Tenant, new TestClock(Now))
            .EvaluateVehicleAsync(h.VehicleId, new DateOnly(2026, 10, 18), CancellationToken.None))!;

        var issue = Assert.Single(readiness.Issues, i => i.Code == "crane_inspection.missing");
        Assert.Equal(("crane", ReadinessSeverity.Blocking), (issue.Conditional, issue.Severity));
    }
}
