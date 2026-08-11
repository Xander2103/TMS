using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Wave 1 phases 4+5: fast dossier creation, the activity layer, and optimistic concurrency.
/// Covers plan test-matrix rows 1-8 and 14 (backend).
/// </summary>
public class DossierFoundationTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid EntityId)
    {
        public DossierService Dossiers(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }

        public DossierActivityService Activities(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var audit = new AuditService(Db.Context, tenant, new DevCurrentUserContext(null));
            var dossiers = new DossierService(Db.Context, tenant, audit, new TestClock(Now));
            var orders = new TransportOrderService(Db.Context, tenant, audit, new TestClock(Now));
            return new DossierActivityService(Db.Context, tenant, audit, dossiers, orders, new TestClock(Now));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = entityId, TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Nexans NV",
            IsActive = true, DefaultLegalEntityId = entityId,
        });
        await db.Context.SaveChangesAsync();

        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId, entityId);
    }

    private async Task<Guid> TypeIdAsync(Harness h, string code) =>
        (await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == code)).Id;

    // Row 1+3: minimal create — customer only; number claimed, date defaulted, entity inherited.
    [Fact]
    public async Task FastCreate_CustomerOnly_InheritsEntityAndDefaults()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);

        Assert.Equal("DOS-0001", dossier.DossierNumber);
        Assert.Equal(new DateOnly(2026, 8, 12), dossier.DossierDate);
        Assert.Equal(h.EntityId, dossier.LegalEntityId);
        Assert.Equal("Acme Transport BV", dossier.LegalEntityName);
        Assert.Equal("Nexans NV — 12-08-2026", dossier.Title);
        Assert.Empty(dossier.Activities!);
        Assert.Contains(dossier.Readiness!, i => i.Code == "activity.none" && i.Severity == "Info");
    }

    // Row 3 variant: customer without default entity → tenant default.
    [Fact]
    public async Task FastCreate_WithoutCustomerDefault_FallsBackToTenantDefaultEntity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.SingleAsync(c => c.Id == h.CustomerId);
        customer.DefaultLegalEntityId = null;
        await h.Db.Context.SaveChangesAsync();

        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);

        Assert.Equal(h.EntityId, dossier.LegalEntityId); // tenant default (IsDefault)
    }

    // Blocked/inactive customers get no new dossiers (order-intake parity).
    [Fact]
    public async Task FastCreate_BlockedCustomer_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.SingleAsync(c => c.Id == h.CustomerId);
        customer.IsBlocked = true;
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None));
        Assert.Contains("geblokkeerd", ex.Message);
    }

    // Template tile: quick-start type becomes the first activity.
    [Fact]
    public async Task FastCreate_WithTemplate_AddsFirstActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var craneTypeId = await TypeIdAsync(h, "KRAANWERK");

        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId, ActivityTypeId: craneTypeId), CancellationToken.None);

        var activity = Assert.Single(dossier.Activities!);
        Assert.Equal("KRAANWERK", activity.ActivityTypeCode);
        Assert.Null(activity.LinkedTransportOrderId); // no fake order; readiness guides completion
        Assert.True(activity.AllowsDuration);
    }

    // Rows 4+5: multiple activities; crane + plateau as SEPARATE activities with accompaniment.
    [Fact]
    public async Task CranePlusPlateau_AreTwoLinkedActivities()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);

        var withCrane = await h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
            await TypeIdAsync(h, "KRAANTRANSPORT"), DurationHours: 2.5m, Version: dossier.Version), CancellationToken.None);
        var craneActivity = Assert.Single(withCrane!.Activities!);

        var withPlateau = await h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
            await TypeIdAsync(h, "PLATEAU"), LinkedActivityId: craneActivity.Id, Version: withCrane.Version), CancellationToken.None);

        Assert.Equal(2, withPlateau!.Activities!.Count);
        var plateau = withPlateau.Activities!.Single(a => a.ActivityTypeCode == "PLATEAU");
        Assert.Equal(craneActivity.Id, plateau.LinkedActivityId);
        Assert.Equal(2.5m, withPlateau.Activities!.Single(a => a.ActivityTypeCode == "KRAANTRANSPORT").DurationHours);
    }

    // Accompaniment must stay within the dossier.
    [Fact]
    public async Task Accompaniment_AcrossDossiers_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId, ActivityTypeId: await TypeIdAsync(h, "KRAANWERK")), CancellationToken.None);
        var second = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);

        var plateauTypeId = await TypeIdAsync(h, "PLATEAU");
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Activities().AddAsync(second.Id, new SaveDossierActivityRequest(
                plateauTypeId, LinkedActivityId: first.Activities![0].Id), CancellationToken.None));
        Assert.Contains("hetzelfde dossier", ex.Message);
    }

    // Row 6: storage-only dossier — valid without any transport order.
    [Fact]
    public async Task StorageOnlyDossier_IsValid_WithoutTransportOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId, ActivityTypeId: await TypeIdAsync(h, "OPSLAG")), CancellationToken.None);

        Assert.Empty(dossier.Orders);
        var activity = Assert.Single(dossier.Activities!);
        Assert.False(activity.HasStops);
        Assert.Contains(dossier.Readiness!, i => i.Code == "pricing.none" && i.Severity == "Info");
        Assert.DoesNotContain(dossier.Readiness!, i => i.Severity == "Blocking");
    }

    // Row 8: transport activity + CreateLinkedOrder → draft order without stops saves; readiness lists the gap.
    [Fact]
    public async Task TransportActivity_WithDraftOrder_SavesWithoutStops_AndReadinessGuides()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);

        var updated = await h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
            await TypeIdAsync(h, "DIRECT_TRANSPORT"), Label: "2 europallets Antwerpen–Luik",
            CreateLinkedOrder: true, Version: dossier.Version), CancellationToken.None);

        var activity = Assert.Single(updated!.Activities!);
        Assert.NotNull(activity.LinkedTransportOrderId);
        Assert.Equal("ORD-0001", activity.LinkedOrderNumber);
        Assert.Equal("Draft", activity.LinkedOrderStatus);
        // The order lives in THIS dossier — no wrapper was auto-created.
        Assert.Single(await h.Db.Context.TransportDossiers.Where(d => d.TenantId == h.TenantId).ToListAsync());
        Assert.Single(updated.Orders);
        // Confirm gate surfaced BEFORE the user hits Bevestigen.
        Assert.Contains(updated.Readiness!, i => i.Code == "order.confirm.stops" && i.Severity == "Blocking");
        Assert.Contains(updated.Readiness!, i => i.Code == "route.date_missing");
    }

    // Existing order-less transport activity → explicit "Transportopdracht aanmaken" links a
    // draft order into THIS dossier (no wrapper); a second attempt and standalone types refuse.
    [Fact]
    public async Task CreateOrderForActivity_LinksDraftOrder_IntoOwnDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(new SaveDossierRequest(
            CustomerId: h.CustomerId, ActivityTypeId: await TypeIdAsync(h, "DIRECT_TRANSPORT")), CancellationToken.None);
        var activity = Assert.Single(dossier.Activities!);
        Assert.Null(activity.LinkedTransportOrderId); // fast create never fabricates orders

        var updated = await h.Activities().CreateOrderForActivityAsync(
            dossier.Id, activity.Id, dossier.Version, CancellationToken.None);

        var linked = Assert.Single(updated!.Activities!);
        Assert.NotNull(linked.LinkedTransportOrderId);
        Assert.Equal("ORD-0001", linked.LinkedOrderNumber);
        Assert.Equal("Draft", linked.LinkedOrderStatus);
        Assert.Single(updated.Orders); // linked into THIS dossier…
        Assert.Single(await h.Db.Context.TransportDossiers.Where(d => d.TenantId == h.TenantId).ToListAsync()); // …no wrapper

        // Already linked → refused.
        var again = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Activities().CreateOrderForActivityAsync(dossier.Id, linked.Id, updated.Version, CancellationToken.None));
        Assert.Contains("al een opdracht", again.Message);

        // Standalone (no-stops) activity → refused.
        var withCrane = await h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
            await TypeIdAsync(h, "KRAANWERK"), Version: updated.Version), CancellationToken.None);
        var crane = withCrane!.Activities!.Single(a => a.ActivityTypeCode == "KRAANWERK");
        var standalone = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Activities().CreateOrderForActivityAsync(dossier.Id, crane.Id, withCrane.Version, CancellationToken.None));
        Assert.Contains("transportactiviteiten", standalone.Message);
    }

    // Auto-wrap: an order created WITHOUT a dossier (EDI/portal/legacy path) gets its own wrapper.
    [Fact]
    public async Task OrderCreate_WithoutDossier_AutoWrapsIntoOwnDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orders = new TransportOrderService(h.Db.Context, new DevTenantContext(h.TenantId),
            new AuditService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(null)), new TestClock(Now));

        var result = await orders.CreateAsync(new Modules.Orders.Dtos.CreateTransportOrderRequest(
            h.CustomerId, "REF-1", new DateOnly(2026, 8, 13), "2 paletten",
            null, null, null, null, null, false, false, null, null, []), CancellationToken.None);

        Assert.Equal(Modules.Orders.Dtos.TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Order!.DossierId);
        Assert.NotNull(result.Order.DossierNumber);
        var wrapper = await h.Db.Context.TransportDossiers
            .SingleAsync(d => d.OriginTransportOrderId == result.Order.Id);
        Assert.Equal(h.CustomerId, wrapper.CustomerId);
        Assert.Equal("REF-1", wrapper.CustomerReference);
        Assert.Equal(new DateOnly(2026, 8, 13), wrapper.DossierDate);
        var activity = await h.Db.Context.DossierActivities.SingleAsync(a => a.DossierId == wrapper.Id);
        Assert.Equal(result.Order.Id, activity.LinkedTransportOrderId);
    }

    // Row 14: stale version → 409-shaped conflict carrying the current state; null skips.
    [Fact]
    public async Task DossierMutation_WithStaleVersion_Conflicts_AndNullSkips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);
        var staleVersion = dossier.Version;

        // A concurrent edit bumps the token (interceptor does it on any modification).
        await h.Dossiers().UpdateAsync(dossier.Id,
            new SaveDossierRequest("Nieuwe titel", CustomerId: h.CustomerId, Version: staleVersion), CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<DossierVersionConflictException>(() =>
            h.Dossiers().UpdateAsync(dossier.Id,
                new SaveDossierRequest("Verloren titel", CustomerId: h.CustomerId, Version: staleVersion), CancellationToken.None));
        Assert.Equal("Nieuwe titel", conflict.Current.Title); // 409 body = current state for rebase

        // Null version (legacy callers) still writes.
        var legacy = await h.Dossiers().UpdateAsync(dossier.Id,
            new SaveDossierRequest("Legacy titel", CustomerId: h.CustomerId), CancellationToken.None);
        Assert.Equal("Legacy titel", legacy!.Title);
    }

    [Fact]
    public async Task OrderUpdate_WithStaleVersion_ReturnsVersionConflict_WithCurrentState()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var orders = new TransportOrderService(h.Db.Context, new DevTenantContext(h.TenantId),
            new AuditService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(null)), new TestClock(Now));
        var created = await orders.CreateAsync(new Modules.Orders.Dtos.CreateTransportOrderRequest(
            h.CustomerId, null, null, "2 paletten", null, null, null, null, null, false, false, null, null, []),
            CancellationToken.None);
        var stale = created.Order!.Version;

        Modules.Orders.Dtos.UpdateTransportOrderRequest UpdateWith(string goods, Guid? version) => new(
            h.CustomerId, null, null, goods, null, null, null, null, null, false, false, null, null, [],
            Version: version);

        var first = await orders.UpdateAsync(created.Order.Id, UpdateWith("3 paletten", stale), CancellationToken.None);
        Assert.Equal(Modules.Orders.Dtos.TransportOrderOperationOutcome.Success, first.Outcome);

        var conflict = await orders.UpdateAsync(created.Order.Id, UpdateWith("5 paletten", stale), CancellationToken.None);
        Assert.Equal(Modules.Orders.Dtos.TransportOrderOperationOutcome.VersionConflict, conflict.Outcome);
        Assert.Equal("3 paletten", conflict.Order!.GoodsDescription); // current state travels back

        var legacy = await orders.UpdateAsync(created.Order.Id, UpdateWith("6 paletten", null), CancellationToken.None);
        Assert.Equal(Modules.Orders.Dtos.TransportOrderOperationOutcome.Success, legacy.Outcome);
    }

    // Row 9/10: tenant isolation of the activity layer.
    [Fact]
    public async Task ActivityLayer_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(
            new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);
        var foreignTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "B", Slug = "b", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(h.Db.Context, new DevTenantContext(foreignTenant)).EnsureSeededAsync(CancellationToken.None);
        var foreignTypeId = (await h.Db.Context.ActivityTypes
            .SingleAsync(t => t.TenantId == foreignTenant && t.Code == "OPSLAG")).Id;

        // Using another tenant's ActivityTypeId is refused.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(foreignTypeId), CancellationToken.None));
        Assert.Contains("bestaat niet", ex.Message);

        // The other tenant cannot even see the dossier.
        Assert.Null(await h.Activities(foreignTenant).AddAsync(
            dossier.Id, new SaveDossierActivityRequest(foreignTypeId), CancellationToken.None));
    }
}
