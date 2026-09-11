using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Step 13 (2026-09-11) — standalone billable activities (Opslag, Kraanwerk) own their sales
/// price through <see cref="DossierActivityPricing"/>; no transport order is ever invented for
/// it. Same provenance semantics as the order: no record = unpriced, € 0 = priced + warning.
/// Dossier total and completeness count BILLABLE UNITS (design doc §2.2–§2.5).
/// </summary>
public class DossierActivityPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 11, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        private DevTenantContext Tenant => new(TenantId);

        private AuditService Audit(ITenantContext tenant) => new(Db.Context, tenant, new DevCurrentUserContext(null));

        public DossierService Dossiers() => new(Db.Context, Tenant, Audit(Tenant), new TestClock(Now));

        public DossierActivityService Activities()
        {
            var tenant = Tenant;
            var orders = new TransportOrderService(Db.Context, tenant, Audit(tenant), new TestClock(Now));
            return new DossierActivityService(Db.Context, tenant, Audit(tenant), Dossiers(), orders, new TestClock(Now));
        }

        public DossierActivityPricingService Pricing(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierActivityPricingService(Db.Context, tenant, Audit(tenant),
                new DossierService(Db.Context, tenant, Audit(tenant), new TestClock(Now)));
        }

        public TransportOrderService Orders() => new(Db.Context, Tenant, Audit(Tenant), new TestClock(Now));

        public DossierReadinessService Readiness() => new(Db.Context, Tenant);

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        /// <summary>Dossier whose first activity is of the given type (no order for standalone types).</summary>
        public async Task<DossierDetailDto> DossierWithAsync(string typeCode) =>
            await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId, ActivityTypeId: await TypeIdAsync(typeCode)), CancellationToken.None);

        public async Task<DossierDetailDto> AddActivityAsync(DossierDetailDto dossier, string typeCode, bool createOrder = false) =>
            (await Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
                await TypeIdAsync(typeCode), CreateLinkedOrder: createOrder, Version: dossier.Version), CancellationToken.None))!;

        public Task<DossierDetailDto?> SetPriceAsync(Guid dossierId, Guid activityId, decimal? amount, Guid? version = null) =>
            Pricing().SetAgreedPriceAsync(dossierId, activityId, new SetActivityPriceRequest(amount, version), CancellationToken.None);
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
        return new Harness(db, tenantId, customerId);
    }

    private static DossierActivityDto Only(DossierDetailDto dossier) => Assert.Single(dossier.Activities!);

    // ------------------------------------------------------------ storage-only / crane-only

    [Fact]
    public async Task StorageOnlyDossier_PersistsAnAgreedPrice_WithoutAnyTransportOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);
        Assert.False(activity.HasStops);
        Assert.True(activity.IsBillable);
        Assert.False(activity.IsPriced);

        var updated = (await h.SetPriceAsync(dossier.Id, activity.Id, 350m))!;

        var priced = Only(updated);
        Assert.Equal("OneOff", priced.PricingSource);
        Assert.Equal(350m, priced.AgreedPrice);
        Assert.True(priced.IsPriced);
        Assert.Equal("Draft", priced.PricingStatus);
        Assert.NotNull(priced.PricingVersion);
        // No order was invented anywhere.
        Assert.Empty(updated.Orders);
        Assert.Equal(0, await h.Db.Context.TransportOrders.CountAsync(o => o.TenantId == h.TenantId));
        // The dossier money follows the activity.
        Assert.Equal(350m, updated.Financials.AgreedOrderTotal);
        Assert.Equal(1, updated.Financials.BillableActivityCount);
        Assert.Equal(1, updated.Financials.PricedActivityCount);
        Assert.Equal(0, updated.Financials.ZeroPricedActivityCount);
        Assert.DoesNotContain(updated.Readiness!, i => i.Code.StartsWith("pricing."));

        // Survives a full re-read (persisted server-side, not page state).
        var reread = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(350m, Only(reread).AgreedPrice);
        Assert.Equal(priced.PricingVersion, Only(reread).PricingVersion);
        var stored = await h.Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync(p => p.DossierActivityId == activity.Id);
        Assert.Equal(ActivityPricingSource.OneOff, stored.PricingSource);
        Assert.Equal(350m, stored.FixedAmount);
        Assert.Equal(350m, stored.AgreedPrice);
        Assert.Equal(h.TenantId, stored.TenantId);

        // The list projection (single statement) agrees.
        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(350m, row.AgreedPriceTotal);
        Assert.Equal(0, row.OrderCount);
        Assert.Equal(1, row.BillableActivityCount);
        Assert.Equal(1, row.PricedActivityCount);
        Assert.Equal(0, row.ZeroPricedActivityCount);

        // Audited old → new.
        Assert.Contains(await h.Db.Context.AuditLogs.AsNoTracking().ToListAsync(),
            a => a.EntityType == "DossierActivityPricing" && a.EntityId == activity.Id.ToString() && a.Action == "agreedPriceSet");
    }

    [Fact]
    public async Task CraneOnlyDossier_PersistsAnAgreedPrice_WithoutAFakeOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("KRAANWERK");

        var updated = (await h.SetPriceAsync(dossier.Id, Only(dossier).Id, 500m))!;

        Assert.Equal(500m, Only(updated).AgreedPrice);
        Assert.True(Only(updated).IsPriced);
        Assert.Empty(updated.Orders);
        Assert.Equal(500m, updated.Financials.AgreedOrderTotal);
        Assert.Equal((1, 1), (updated.Financials.BillableActivityCount, updated.Financials.PricedActivityCount));
        Assert.DoesNotContain(updated.Readiness!, i => i.Code == "route.order_missing");
    }

    // ------------------------------------------------------------ provenance: € 0 vs none

    [Fact]
    public async Task IntentionalZero_CountsAsPriced_AndGetsTheNonBlockingZeroWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");

        var updated = (await h.SetPriceAsync(dossier.Id, Only(dossier).Id, 0m))!;

        var activity = Only(updated);
        Assert.True(activity.IsPriced);
        Assert.Equal(0m, activity.AgreedPrice);
        Assert.Equal("OneOff", activity.PricingSource);
        Assert.Equal(1, updated.Financials.PricedActivityCount);
        Assert.Equal(1, updated.Financials.ZeroPricedActivityCount);
        Assert.Equal(0m, updated.Financials.AgreedOrderTotal);

        var zero = Assert.Single(updated.Readiness!, i => i.Code == "pricing.zero");
        Assert.Equal("Warning", zero.Severity);
        Assert.Equal("prijs", zero.Section);
        Assert.Equal("price", zero.Field);
        Assert.Equal(activity.Id, zero.ActivityId);
        Assert.Equal("Opslag heeft een verkoopprijs van € 0,00. Controleer of dit bewust is.", zero.Message);
        Assert.DoesNotContain(updated.Readiness!, i => i.Code == "pricing.missing");
        Assert.DoesNotContain(updated.Readiness!, i => i.Severity == "Blocking");
        // A priced-at-zero unit is worth a look on the dashboard tile too.
        Assert.Equal(1, await h.Readiness().CountDossiersWithAttentionAsync(CancellationToken.None));

        // The list shows a real € 0,00 (not "no price") plus the zero marker.
        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(0m, row.AgreedPriceTotal);
        Assert.Equal(1, row.PricedActivityCount);
        Assert.Equal(1, row.ZeroPricedActivityCount);
    }

    [Fact]
    public async Task NoPrice_IsUnpriced_WithAMissingWarningThatNamesTheActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");

        var activity = Only(dossier);
        Assert.False(activity.IsPriced);
        Assert.Null(activity.AgreedPrice);
        Assert.Equal("None", activity.PricingSource);
        Assert.Null(activity.PricingVersion);
        Assert.Equal(1, dossier.Financials.BillableActivityCount);
        Assert.Equal(0, dossier.Financials.PricedActivityCount);

        var missing = Assert.Single(dossier.Readiness!, i => i.Code == "pricing.missing");
        Assert.Equal("Warning", missing.Severity);
        Assert.Equal(activity.Id, missing.ActivityId);
        Assert.Null(missing.TransportOrderId);
        Assert.Equal("Nog geen verkoopprijs voor Opslag.", missing.Message);
        Assert.DoesNotContain(dossier.Readiness!, i => i.Code == "pricing.zero" || i.Code == "pricing.none");
        Assert.Equal(1, await h.Readiness().CountDossiersWithAttentionAsync(CancellationToken.None));

        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Null(row.AgreedPriceTotal);
        Assert.Equal(1, row.BillableActivityCount);
        Assert.Equal(0, row.PricedActivityCount);
    }

    [Fact]
    public async Task ClearingThePrice_ReturnsTheActivityToUnpriced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);
        var priced = (await h.SetPriceAsync(dossier.Id, activity.Id, 350m))!;

        var cleared = (await h.SetPriceAsync(dossier.Id, activity.Id, null, Only(priced).PricingVersion))!;

        Assert.False(Only(cleared).IsPriced);
        Assert.Null(Only(cleared).AgreedPrice);
        Assert.Equal("None", Only(cleared).PricingSource);
        Assert.Equal(0, cleared.Financials.PricedActivityCount);
        Assert.Contains(cleared.Readiness!, i => i.Code == "pricing.missing" && i.ActivityId == activity.Id);
    }

    // ------------------------------------------------------------ mixed dossier

    [Fact]
    public async Task MixedDossier_TotalAndCompleteness_CountEveryBillableUnit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("DIRECT_TRANSPORT");
        dossier = await h.AddActivityAsync(dossier, "OPSLAG");
        dossier = await h.AddActivityAsync(dossier, "KRAANWERK");
        // The transport activity gets its real order (it is transport work) and a one-off € 450.
        var transport = dossier.Activities!.Single(a => a.ActivityTypeCode == "DIRECT_TRANSPORT");
        dossier = (await h.Activities().CreateOrderForActivityAsync(dossier.Id, transport.Id, dossier.Version, CancellationToken.None))!;
        var orderId = dossier.Activities!.Single(a => a.Id == transport.Id).LinkedTransportOrderId!.Value;
        var order = await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal(TransportOrderOperationOutcome.Success,
            (await h.Orders().SetOneOffPriceAsync(orderId, new SetOneOffPriceRequest(450m, order.Version), CancellationToken.None)).Outcome);
        var storage = dossier.Activities!.Single(a => a.ActivityTypeCode == "OPSLAG");
        var crane = dossier.Activities!.Single(a => a.ActivityTypeCode == "KRAANWERK");

        var partial = (await h.SetPriceAsync(dossier.Id, storage.Id, 200m))!;

        // 450 + 200, crane still missing → 2 of 3.
        Assert.Equal(650m, partial.Financials.AgreedOrderTotal);
        Assert.Equal(3, partial.Financials.BillableActivityCount);
        Assert.Equal(2, partial.Financials.PricedActivityCount);
        Assert.Equal(1, partial.Financials.PricedOrderCount);
        var transportUnit = partial.Activities!.Single(a => a.Id == transport.Id);
        Assert.Equal(("Order", 450m, true), (transportUnit.PricingSource, transportUnit.AgreedPrice, transportUnit.IsPriced));
        var missing = Assert.Single(partial.Readiness!, i => i.Code == "pricing.missing");
        Assert.Equal(crane.Id, missing.ActivityId);
        Assert.Equal("Nog geen verkoopprijs voor Kraanwerk ter plaatse.", missing.Message);
        var partialRow = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(650m, partialRow.AgreedPriceTotal);
        Assert.Equal((3, 2, 1), (partialRow.BillableActivityCount, partialRow.PricedActivityCount, partialRow.OrderCount));

        var complete = (await h.SetPriceAsync(dossier.Id, crane.Id, 150m))!;

        Assert.Equal(800m, complete.Financials.AgreedOrderTotal);
        Assert.Equal(3, complete.Financials.PricedActivityCount);
        Assert.DoesNotContain(complete.Readiness!, i => i.Code.StartsWith("pricing."));
        // Pricing one activity never touched another one's record.
        Assert.Equal(200m, complete.Activities!.Single(a => a.Id == storage.Id).AgreedPrice);
        Assert.Equal(450m, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == orderId)).OneOffFixedAmount);
        var completeRow = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(800m, completeRow.AgreedPriceTotal);
        Assert.Equal(3, completeRow.PricedActivityCount);
    }

    [Fact]
    public async Task NonBillableType_IsNoCommercialUnit_AndCannotBePriced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var type = await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == "OPSLAG");
        type.IsBillable = false;
        await h.Db.Context.SaveChangesAsync();
        var dossier = await h.DossierWithAsync("OPSLAG");

        Assert.False(Only(dossier).IsBillable);
        Assert.Equal(0, dossier.Financials.BillableActivityCount);
        Assert.DoesNotContain(dossier.Readiness!, i => i.Code.StartsWith("pricing."));
        Assert.Equal(0, await h.Readiness().CountDossiersWithAttentionAsync(CancellationToken.None));
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.SetPriceAsync(dossier.Id, Only(dossier).Id, 100m));
        Assert.Contains("niet factureerbaar", ex.Message);
    }

    // ------------------------------------------------------------ refusals

    [Fact]
    public async Task TransportActivity_IsPricedThroughItsOrder_NeverThroughTheActivityCommand()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("DIRECT_TRANSPORT");

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.SetPriceAsync(dossier.Id, Only(dossier).Id, 100m));
        Assert.Contains("transportopdracht", ex.Message);
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync());
    }

    [Fact]
    public async Task NegativeAmount_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");

        await Assert.ThrowsAsync<DomainValidationException>(() => h.SetPriceAsync(dossier.Id, Only(dossier).Id, -1m));
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync());
    }

    [Fact]
    public async Task ClosedDossier_RefusesPriceChanges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        await h.Dossiers().CloseAsync(dossier.Id, CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => h.SetPriceAsync(dossier.Id, Only(dossier).Id, 100m));
    }

    [Fact]
    public async Task LockedOrInvoicedPrice_IsRefused_WithTheLockMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);
        await h.SetPriceAsync(dossier.Id, activity.Id, 350m);
        var stored = await h.Db.Context.DossierActivityPricings.SingleAsync(p => p.DossierActivityId == activity.Id);
        stored.Status = OrderPricingStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.SetPriceAsync(dossier.Id, activity.Id, 400m));

        Assert.Equal(DossierActivityPricingService.LockedMessage, ex.Message);
        Assert.Equal(350m, (await h.Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync(p => p.DossierActivityId == activity.Id)).FixedAmount);
        Assert.Equal("Invoiced", Only((await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!).PricingStatus);
    }

    // ------------------------------------------------------------ concurrency & tenancy

    [Fact]
    public async Task StaleVersion_YieldsConflictWithTheCurrentState_AndChangesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);
        var first = (await h.SetPriceAsync(dossier.Id, activity.Id, 350m))!;
        var currentVersion = Only(first).PricingVersion!.Value;

        var conflict = await Assert.ThrowsAsync<DossierVersionConflictException>(
            () => h.SetPriceAsync(dossier.Id, activity.Id, 999m, Guid.NewGuid()));

        Assert.Equal(350m, Only(conflict.Current).AgreedPrice);
        Assert.Equal(currentVersion, Only(conflict.Current).PricingVersion);
        Assert.Equal(350m, (await h.Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync(p => p.DossierActivityId == activity.Id)).FixedAmount);

        // The right token goes through and bumps the token.
        var second = (await h.SetPriceAsync(dossier.Id, activity.Id, 400m, currentVersion))!;
        Assert.Equal(400m, Only(second).AgreedPrice);
        Assert.NotEqual(currentVersion, Only(second).PricingVersion);
    }

    [Fact]
    public async Task ASecondActivePriceRecord_IsRefusedByTheDatabase()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);
        await h.SetPriceAsync(dossier.Id, activity.Id, 350m);

        h.Db.Context.DossierActivityPricings.Add(new DossierActivityPricing
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierActivityId = activity.Id,
            PricingSource = ActivityPricingSource.OneOff, FixedAmount = 1m, AgreedPrice = 1m,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeOrPriceTheActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);

        var foreign = await h.Pricing(Guid.NewGuid())
            .SetAgreedPriceAsync(dossier.Id, activity.Id, new SetActivityPriceRequest(100m), CancellationToken.None);

        Assert.Null(foreign);
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync());
    }

    [Fact]
    public async Task DeletingTheActivity_TakesItsPriceRecordAlong()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("OPSLAG");
        var activity = Only(dossier);
        var priced = (await h.SetPriceAsync(dossier.Id, activity.Id, 350m))!;

        var after = (await h.Activities().DeleteAsync(dossier.Id, activity.Id, priced.Version, CancellationToken.None))!;

        Assert.Empty(after.Activities!);
        Assert.Equal(0, after.Financials.BillableActivityCount);
        Assert.Equal(0m, after.Financials.AgreedOrderTotal);
        // Soft-deleted activity → its record is invisible to every read path.
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync(p => p.DossierActivityId == activity.Id));
    }
}
