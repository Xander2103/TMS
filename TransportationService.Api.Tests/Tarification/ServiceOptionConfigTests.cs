using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tarification;

/// <summary>Global services & surcharges configuration (spec §5/6): explicit pricing methods.</summary>
public class ServiceOptionConfigTests
{
    private sealed record Harness(SqliteTestDbContext Db, PricingAdminService Admin, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var admin = new PricingAdminService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, admin, tenantId, customerId);
    }

    [Fact]
    public async Task Create_PersistsMethodDescriptionsAndSelectability()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var wachttijd = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "WACHT", "Wachttijd", SurchargeKind.PerHour, 45m, true, 0,
            Description: "Wachttijd bij laden/lossen", InvoiceDescription: "Wachturen chauffeur",
            SelectableInOrders: true), CancellationToken.None);
        var brandstof = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "FUEL", "Brandstoftoeslag", SurchargeKind.Percent, 8m, true, 1,
            SelectableInOrders: false), CancellationToken.None);
        var extraStop = await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "STOP", "Extra stop", SurchargeKind.PerStop, 20m, true, 2), CancellationToken.None);

        Assert.Equal(SurchargeKind.PerHour, wachttijd.Kind);
        Assert.Equal("Wachturen chauffeur", wachttijd.InvoiceDescription);
        Assert.False(brandstof.SelectableInOrders);
        Assert.Equal(SurchargeKind.PerStop, extraStop.Kind);
    }

    [Fact]
    public async Task OrderEntryList_ExcludesInactiveAndNotSelectable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "VOOR10", "Levering vóór 10:00", SurchargeKind.Fixed, 15m, true, 0), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "FUEL", "Brandstoftoeslag", SurchargeKind.Percent, 8m, true, 1, SelectableInOrders: false), CancellationToken.None);
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "OUD", "Verouderde dienst", SurchargeKind.Fixed, 5m, false, 2), CancellationToken.None);

        var orderEntry = await h.Admin.ListServiceOptionsAsync(includeInactive: false, forOrderEntry: true, CancellationToken.None);
        var admin = await h.Admin.ListServiceOptionsAsync(includeInactive: true, forOrderEntry: false, CancellationToken.None);

        Assert.Single(orderEntry);
        Assert.Equal("VOOR10", orderEntry[0].Code);
        Assert.Equal(3, admin.Count);
    }

    [Fact]
    public async Task Validation_RejectsNegativeDefault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateServiceOptionAsync(
            new SaveServiceOptionRequest("NEG", "Negatief", SurchargeKind.Fixed, -5m, true, 0), CancellationToken.None));
    }

    [Fact]
    public async Task AgreementSurcharges_OnlyAllowPercentOrFixed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Admin.CreateAgreementAsync(
            new SavePricingAgreementRequest(h.CustomerId, "Afspraak", new DateOnly(2026, 1, 1), null, true, null, null,
                [new SavePricingAgreementSurchargeRequest("Wachttijd", SurchargeKind.PerHour, 45m)]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Options_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateServiceOptionAsync(new SaveServiceOptionRequest(
            "VOOR10", "Levering vóór 10:00", SurchargeKind.Fixed, 15m, true, 0), CancellationToken.None);

        var foreignTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenant = new DevTenantContext(foreignTenantId);
        var foreignAdmin = new PricingAdminService(h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)));

        Assert.Empty(await foreignAdmin.ListServiceOptionsAsync(true, false, CancellationToken.None));
    }
}
