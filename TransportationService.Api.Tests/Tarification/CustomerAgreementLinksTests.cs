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

/// <summary>
/// Aggregated tariff base per customer (GET api/customers/{id}/pricing-agreements): own private
/// tables + shared tables assigned to that customer, in one call — the read model behind the
/// customer detail's "Tariefbasis" section (closes the fan-out of docs/pricing.md §11.4).
/// </summary>
public class CustomerAgreementLinksTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Harness(SqliteTestDbContext Db, PricingAdminService Admin, Guid TenantId, Guid CustomerAId, Guid CustomerBId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerAId, TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-A", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerBId, TenantId = tenantId, Name = "Klant B", CustomerNumber = "KL-B", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var admin = new PricingAdminService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, admin, tenantId, customerAId, customerBId);
    }

    private static Task<PricingAgreementDto> CreateAgreementAsync(
        Harness h, string name, Guid? customerId = null, bool isShared = false, Guid? baseAgreementId = null) =>
        h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            customerId, name, Today.AddMonths(-1), null, true, null, null, null,
            IsShared: isShared, BaseAgreementId: baseAgreementId), CancellationToken.None);

    [Fact]
    public async Task CustomerWithoutAnyTable_ReturnsEmptyList_NotNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // A shared table WITHOUT an assignment for this customer must not appear either.
        await CreateAgreementAsync(h, "Gedeeld zonder koppeling", isShared: true);

        var links = await h.Admin.ListCustomerAgreementsAsync(h.CustomerAId, CancellationToken.None);

        Assert.NotNull(links);
        Assert.Empty(links!);
    }

    [Fact]
    public async Task UnknownCustomer_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Null(await h.Admin.ListCustomerAgreementsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task OwnAndAssignedSharedTables_AreReturnedTogether_WithAssignmentData()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var own = await CreateAgreementAsync(h, "Eigen tabel A", customerId: h.CustomerAId);
        var shared = await CreateAgreementAsync(h, "Distributie België 2026", isShared: true);
        await h.Admin.SaveAssignmentsAsync(shared.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, -5m, null, Today.AddMonths(-2), Today.AddMonths(10), null)],
            CancellationToken.None);
        // Another customer's private table and assignment never leak into A's view.
        await CreateAgreementAsync(h, "Eigen tabel B", customerId: h.CustomerBId);
        var sharedForB = await CreateAgreementAsync(h, "Alleen voor B", isShared: true);
        await h.Admin.SaveAssignmentsAsync(sharedForB.Id,
            [new SavePricingAssignmentRequest(h.CustomerBId, null, null, null, null, null)], CancellationToken.None);

        var links = await h.Admin.ListCustomerAgreementsAsync(h.CustomerAId, CancellationToken.None);

        Assert.NotNull(links);
        Assert.Equal(2, links!.Count);
        var sharedLink = Assert.Single(links, l => l.AgreementId == shared.Id);
        Assert.True(sharedLink.IsShared);
        Assert.NotNull(sharedLink.AssignmentId);
        Assert.Equal(-5m, sharedLink.AssignmentPercentAdjustment);
        Assert.Equal(Today.AddMonths(-2), sharedLink.AssignmentEffectiveFrom);
        Assert.Equal(Today.AddMonths(10), sharedLink.AssignmentEffectiveUntil);
        var ownLink = Assert.Single(links, l => l.AgreementId == own.Id);
        Assert.False(ownLink.IsShared);
        Assert.Null(ownLink.AssignmentId);
    }

    [Fact]
    public async Task DerivedTable_CarriesBaseAgreementName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var baseTable = await CreateAgreementAsync(h, "Basis BE", isShared: true);
        await CreateAgreementAsync(h, "NL = BE +30%", customerId: h.CustomerAId, baseAgreementId: baseTable.Id);

        var links = await h.Admin.ListCustomerAgreementsAsync(h.CustomerAId, CancellationToken.None);

        var derived = Assert.Single(links!);
        Assert.Equal(baseTable.Id, derived.BaseAgreementId);
        Assert.Equal("Basis BE", derived.BaseAgreementName);
    }

    [Fact]
    public async Task PlannedAgreementAdjustment_IsSurfaced_ActivatedOnesAreNot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var own = await CreateAgreementAsync(h, "Eigen tabel A", customerId: h.CustomerAId);
        h.Db.Context.ScheduledPriceAdjustments.AddRange(
            new ScheduledPriceAdjustment
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, AgreementId = own.Id,
                EffectiveDate = Today.AddDays(30), Percent = 3m, Status = ScheduledAdjustmentStatus.Scheduled,
            },
            new ScheduledPriceAdjustment
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, AgreementId = own.Id,
                EffectiveDate = Today.AddDays(60), Percent = 5m, Status = ScheduledAdjustmentStatus.Scheduled,
            },
            new ScheduledPriceAdjustment
            {
                // Already effective ("Actief"): not planned anymore, must not be reported.
                Id = Guid.NewGuid(), TenantId = h.TenantId, AgreementId = own.Id,
                EffectiveDate = Today.AddDays(-10), Percent = 2m, Status = ScheduledAdjustmentStatus.Scheduled,
            });
        await h.Db.Context.SaveChangesAsync();

        var link = Assert.Single((await h.Admin.ListCustomerAgreementsAsync(h.CustomerAId, CancellationToken.None))!);

        // The EARLIEST still-planned adjustment wins.
        Assert.Equal(Today.AddDays(30), link.PlannedAdjustmentDate);
        Assert.Equal(3m, link.PlannedAdjustmentPercent);
        Assert.Null(link.PlannedAdjustmentAmountDelta);
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantSeesNothing_AndCustomerResolvesPerTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var shared = await CreateAgreementAsync(h, "Distributie België 2026", isShared: true);
        await h.Admin.SaveAssignmentsAsync(shared.Id,
            [new SavePricingAssignmentRequest(h.CustomerAId, null, null, null, null, null)], CancellationToken.None);

        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        await h.Db.Context.SaveChangesAsync();
        var otherTenant = new DevTenantContext(otherTenantId);
        var otherAdmin = new PricingAdminService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)));

        // Tenant B cannot even resolve tenant A's customer id — the endpoint 404s.
        Assert.Null(await otherAdmin.ListCustomerAgreementsAsync(h.CustomerAId, CancellationToken.None));
    }
}
