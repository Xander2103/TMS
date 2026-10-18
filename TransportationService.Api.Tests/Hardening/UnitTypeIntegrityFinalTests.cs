using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Integrity;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Reference.Services;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hardening;

/// <summary>
/// MD-H8 final follow-up: the inherited generic <c>PUT /api/unit-types/{id}</c> could still re-code a
/// referenced unit (freeing the old code for reuse); service options and customer unit mappings were
/// not counted as references; pricing endpoints stored inactive / not-for-pricing units. ONE unit
/// invariant (the guard) now serves every mutation path, and ONE usage validator every pricing write.
/// </summary>
public class UnitTypeIntegrityFinalTests
{
    private sealed record Seed(SqliteTestDbContext Db, Guid TenantId, DevTenantContext Tenant, AuditService Audit, Guid UnitId, Guid CustomerId)
        : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Seed> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.UnitTypes.Add(new UnitType { Id = unitId, TenantId = tenantId, Code = "TESTCOREUNIT", Name = "TEST CORE Eenheid", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-0099", Name = "TEST CORE Klant", IsActive = true });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        return new Seed(db, tenantId, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), unitId, customerId);
    }

    private static LookupService<UnitType> GenericUnitLookups(Seed s) =>
        new(s.Db.Context, s.Tenant, s.Audit, [new UnitTypeReferenceGuard(s.Db.Context, s.Tenant)]);

    private static PricingAdminService Admin(Seed s) => new(s.Db.Context, s.Tenant, s.Audit);

    private static SaveServiceOptionRequest PerUnitOption(string code, Guid unitTypeId) =>
        new(code, $"Service {code}", SurchargeKind.PerUnit, 5m, true, 0, UnitTypeId: unitTypeId);

    [Fact]
    public async Task GenericPut_CannotRecodeAReferencedUnit_ButMayRenameIt()
    {
        using var s = await SeedAsync();
        await Admin(s).CreateServiceOptionAsync(PerUnitOption("TCOPT", s.UnitId), CancellationToken.None);
        var lookups = GenericUnitLookups(s);

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() => lookups.UpdateAsync(s.UnitId,
            new UpdateLookupRequest("TESTCOREUNIT2", "TEST CORE Eenheid", null, true, 0), CancellationToken.None));
        Assert.Contains("kan niet meer gewijzigd worden", refused.Message);

        var renamed = await lookups.UpdateAsync(s.UnitId,
            new UpdateLookupRequest("TESTCOREUNIT", "TEST CORE Eenheid HERNOEMD", null, false, 0), CancellationToken.None);
        Assert.Equal(LookupOperationStatus.Success, renamed.Status);
        s.Db.Context.ChangeTracker.Clear();
        var unit = await s.Db.Context.UnitTypes.SingleAsync(u => u.Id == s.UnitId);
        Assert.Equal("TESTCOREUNIT", unit.Code);
        Assert.False(unit.IsActive); // deactivation stays allowed
    }

    [Fact]
    public async Task GenericPut_NormalizesUnitCodes_LikeTheMasterService_AndAnUnusedUnitMayBeRecoded()
    {
        using var s = await SeedAsync();
        var lookups = GenericUnitLookups(s);

        var recoded = await lookups.UpdateAsync(s.UnitId, new UpdateLookupRequest("testcore2", "TEST CORE Eenheid", null, true, 0), CancellationToken.None);
        Assert.Equal(LookupOperationStatus.Success, recoded.Status);
        Assert.Equal("TESTCORE2", recoded.Item!.Code);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            lookups.UpdateAsync(s.UnitId, new UpdateLookupRequest("met spatie!", "x", null, true, 0), CancellationToken.None));
    }

    [Fact]
    public async Task ServiceOption_CustomerUnit_AndPriceAdjustment_CountAsReferences()
    {
        using var s = await SeedAsync();
        var otherUnit = Guid.NewGuid();
        s.Db.Context.UnitTypes.Add(new UnitType { Id = otherUnit, TenantId = s.TenantId, Code = "TCKLANT", Name = "Klanteenheid", IsActive = true });
        await s.Db.Context.SaveChangesAsync();

        await Admin(s).CreateServiceOptionAsync(PerUnitOption("TCOPT", s.UnitId), CancellationToken.None);
        await Admin(s).SaveCustomerConfigAsync(s.CustomerId,
            new SaveCustomerPricingConfigRequest([new SaveCustomerUnitRequest(otherUnit, 0, "Kist", null, null, false)], []),
            CancellationToken.None);

        var lookups = GenericUnitLookups(s);
        var optionRef = await Assert.ThrowsAsync<ReferencedRecordException>(() => lookups.DeleteAsync(s.UnitId, CancellationToken.None));
        Assert.Contains("1 service-optie", optionRef.Message);
        var customerRef = await Assert.ThrowsAsync<ReferencedRecordException>(() => lookups.DeleteAsync(otherUnit, CancellationToken.None));
        Assert.Contains("1 klanteenheid", customerRef.Message);

        var master = new UnitTypeMasterService(s.Db.Context, s.Tenant, s.Audit);
        await Assert.ThrowsAsync<DomainValidationException>(() => master.UpdateAsync(otherUnit, new SaveUnitTypeMasterRequest(
            "TCKLANT2", "Klanteenheid", null, true, 0, true, true, false, UnitCategory.Other, 0, null, UnitDimensionBehavior.Variable,
            null, null, null, null, null, null, null, null), CancellationToken.None));

        var adjustmentUnit = Guid.NewGuid();
        s.Db.Context.UnitTypes.Add(new UnitType { Id = adjustmentUnit, TenantId = s.TenantId, Code = "TCADJ", Name = "Filter", IsActive = true });
        s.Db.Context.ScheduledPriceAdjustments.Add(new ScheduledPriceAdjustment
        {
            Id = Guid.NewGuid(), TenantId = s.TenantId, CustomerId = s.CustomerId, EffectiveDate = new DateOnly(2027, 1, 1), Percent = 3,
            UnitTypeIdFilter = adjustmentUnit, Status = ScheduledAdjustmentStatus.Scheduled,
        });
        await s.Db.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<ReferencedRecordException>(() => lookups.DeleteAsync(adjustmentUnit, CancellationToken.None));
    }

    [Fact]
    public async Task PricingWrites_RefuseUnknown_Inactive_AndNotForPricingUnits_ButKeepUnitsAlreadyInUse()
    {
        using var s = await SeedAsync();
        var inactive = Guid.NewGuid();
        var notForPricing = Guid.NewGuid();
        s.Db.Context.UnitTypes.AddRange(
            new UnitType { Id = inactive, TenantId = s.TenantId, Code = "TCOUD", Name = "Oud", IsActive = false },
            new UnitType { Id = notForPricing, TenantId = s.TenantId, Code = "TCNOPRICE", Name = "Geen prijs", IsActive = true, AllowForPricing = false });
        await s.Db.Context.SaveChangesAsync();
        var admin = Admin(s);

        // Service option.
        await Assert.ThrowsAsync<DomainValidationException>(() => admin.CreateServiceOptionAsync(PerUnitOption("TC1", inactive), CancellationToken.None));
        var noPrice = await Assert.ThrowsAsync<DomainValidationException>(() =>
            admin.CreateServiceOptionAsync(PerUnitOption("TC2", notForPricing), CancellationToken.None));
        Assert.Contains("niet toegelaten voor prijzen", noPrice.Message);
        await Assert.ThrowsAsync<InvalidTenantReferenceException>(() =>
            admin.CreateServiceOptionAsync(PerUnitOption("TC3", Guid.NewGuid()), CancellationToken.None));

        // Customer unit mapping.
        var mapped = await Assert.ThrowsAsync<DomainValidationException>(() => admin.SaveCustomerConfigAsync(s.CustomerId,
            new SaveCustomerPricingConfigRequest([new SaveCustomerUnitRequest(inactive, 0, null, null, null, false)], []), CancellationToken.None));
        Assert.Contains("inactief", mapped.Message);

        // A unit already in use stays valid after it is deactivated: editing the option / mapping still works.
        var option = await admin.CreateServiceOptionAsync(PerUnitOption("TC4", s.UnitId), CancellationToken.None);
        await admin.SaveCustomerConfigAsync(s.CustomerId,
            new SaveCustomerPricingConfigRequest([new SaveCustomerUnitRequest(s.UnitId, 0, "Kist", null, null, false)], []), CancellationToken.None);
        await s.Db.Context.UnitTypes.Where(u => u.Id == s.UnitId).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        s.Db.Context.ChangeTracker.Clear();
        Assert.NotNull(await admin.UpdateServiceOptionAsync(option.Id, PerUnitOption("TC4", s.UnitId) with { Name = "Hernoemd" }, CancellationToken.None));
        Assert.NotNull(await admin.SaveCustomerConfigAsync(s.CustomerId,
            new SaveCustomerPricingConfigRequest([new SaveCustomerUnitRequest(s.UnitId, 1, "Kist 2", null, null, true)], []), CancellationToken.None));
    }

    [Fact]
    public async Task UsageValidator_ChecksCodesPerContext()
    {
        using var s = await SeedAsync();
        s.Db.Context.UnitTypes.Add(new UnitType
        {
            Id = Guid.NewGuid(), TenantId = s.TenantId, Code = "TCORDERONLY", Name = "Alleen order", IsActive = true, AllowForPricing = false,
        });
        await s.Db.Context.SaveChangesAsync();

        await UnitTypeUsage.EnsureCodesAsync(s.Db.Context, s.TenantId, [("TCORDERONLY", "unit")], UnitUsage.OrderEntry, new HashSet<string>(), CancellationToken.None);
        await Assert.ThrowsAsync<DomainValidationException>(() => UnitTypeUsage.EnsureCodesAsync(
            s.Db.Context, s.TenantId, [("TCORDERONLY", "unit")], UnitUsage.Pricing, new HashSet<string>(), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() => UnitTypeUsage.EnsureCodesAsync(
            s.Db.Context, s.TenantId, [("NIETBESTAAND", "unit")], UnitUsage.Pricing, new HashSet<string>(), CancellationToken.None));
        // Already on the record: accepted whatever its state.
        await UnitTypeUsage.EnsureCodesAsync(s.Db.Context, s.TenantId, [("TCORDERONLY", "unit")], UnitUsage.Pricing,
            new HashSet<string> { "TCORDERONLY" }, CancellationToken.None);
    }
}
