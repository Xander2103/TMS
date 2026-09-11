using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
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
/// UX sprint 2026-09-09 §2.5 — readiness issues carry a <c>Field</c> the dossier page can
/// focus, the new <c>pricing.missing</c> rule surfaces an unpriced open order, and the
/// <c>route.date_missing</c> operator-precedence bug is fixed.
/// </summary>
public class DossierReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 09, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        private DevTenantContext Tenant => new(TenantId);

        public DossierService Dossiers()
        {
            var tenant = Tenant;
            return new DossierService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }

        public DossierActivityService Activities()
        {
            var tenant = Tenant;
            var audit = new AuditService(Db.Context, tenant, new DevCurrentUserContext(null));
            var dossiers = new DossierService(Db.Context, tenant, audit, new TestClock(Now));
            var orders = new TransportOrderService(Db.Context, tenant, audit, new TestClock(Now));
            return new DossierActivityService(Db.Context, tenant, audit, dossiers, orders, new TestClock(Now));
        }

        public DossierReadinessService Readiness() => new(Db.Context, Tenant);

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        /// <summary>Dossier with one transport activity and its linked draft order (no stops, no price).</summary>
        public async Task<(DossierDetailDto Dossier, TransportOrder Order)> DossierWithDraftOrderAsync()
        {
            var dossier = await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId), CancellationToken.None);
            var updated = await Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
                await TypeIdAsync("DIRECT_TRANSPORT"), CreateLinkedOrder: true, Version: dossier.Version), CancellationToken.None);
            var orderId = updated!.Activities!.Single().LinkedTransportOrderId!.Value;
            var order = await Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
            return (updated, order);
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
        return new Harness(db, tenantId, customerId);
    }

    private static async Task AddStopAsync(Harness h, Guid orderId, StopType type, int sequence, DateTime? plannedFrom)
    {
        h.Db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId,
            Sequence = sequence, StopType = type, LocationName = type.ToString(), City = "Antwerpen",
            PlannedFrom = plannedFrom,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    // ------------------------------------------------------------ fields per code

    [Fact]
    public async Task EmptyDossier_ActivityNone_PointsAtTheAddActivityControl()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);

        var issue = Assert.Single(dossier.Readiness!, i => i.Code == "activity.none");
        Assert.Equal("Info", issue.Severity);
        Assert.Equal("activiteiten", issue.Section);
        Assert.Equal("activity.add", issue.Field);
        Assert.Equal("Planning", issue.Stage);
    }

    [Fact]
    public async Task TransportActivityWithoutOrder_RouteOrderMissing_LivesInTheRouteSection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(new SaveDossierRequest(
            CustomerId: h.CustomerId, ActivityTypeId: await h.TypeIdAsync("DIRECT_TRANSPORT")), CancellationToken.None);

        var issue = Assert.Single(dossier.Readiness!, i => i.Code == "route.order_missing");
        Assert.Equal("Warning", issue.Severity);
        Assert.Equal("route", issue.Section);
        Assert.Equal("stops.loading", issue.Field);
        // No order yet → pricing.none (Info) and NOT pricing.missing (that needs an order).
        var pricing = Assert.Single(dossier.Readiness!, i => i.Code == "pricing.none");
        Assert.Equal("price", pricing.Field);
        Assert.Equal("prijs", pricing.Section);
        Assert.DoesNotContain(dossier.Readiness!, i => i.Code == "pricing.missing");
    }

    [Fact]
    public async Task DraftOrderWithoutStops_IssuesCarryTheirFields()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, _) = await h.DossierWithDraftOrderAsync();

        var stops = Assert.Single(dossier.Readiness!, i => i.Code == "order.confirm.stops");
        Assert.Equal("Blocking", stops.Severity);
        Assert.Equal("route", stops.Section);
        Assert.Equal("stops.loading", stops.Field); // loading missing → loading first

        var date = Assert.Single(dossier.Readiness!, i => i.Code == "route.date_missing");
        Assert.Equal("route", date.Section);
        Assert.Equal("stops.plannedFrom", date.Field);

        var price = Assert.Single(dossier.Readiness!, i => i.Code == "pricing.missing");
        Assert.Equal("Warning", price.Severity);
        Assert.Equal("prijs", price.Section);
        Assert.Equal("price", price.Field);
        Assert.Equal("Commercial", price.Stage);
        Assert.Equal("ORD-0001: nog geen verkoopprijs.", price.Message);
    }

    [Fact]
    public async Task ConfirmStops_PointsAtUnloading_WhenOnlyLoadingExists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        await AddStopAsync(h, order.Id, StopType.Loading, 1, plannedFrom: null);

        var issues = await h.Readiness().EvaluateAsync(dossier.Id, CancellationToken.None);

        var stops = Assert.Single(issues, i => i.Code == "order.confirm.stops");
        Assert.Equal("stops.unloading", stops.Field);
        Assert.Contains("loslocatie is nog onbekend", stops.Message);
    }

    [Fact]
    public async Task PricingIncompleteAndStale_CarryThePriceField()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        h.Db.Context.TransportOrderPricingSnapshots.Add(new TransportOrderPricingSnapshot
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = order.Id,
            TariffDate = order.OrderDate, Currency = "EUR", CoverageStatus = "Partial", IsStale = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var issues = await h.Readiness().EvaluateAsync(dossier.Id, CancellationToken.None);

        Assert.Equal("price", Assert.Single(issues, i => i.Code == "pricing.incomplete").Field);
        Assert.Equal("price", Assert.Single(issues, i => i.Code == "pricing.stale").Field);
        // pricing.incomplete already names this order's price gap → no duplicate pricing.missing.
        Assert.DoesNotContain(issues, i => i.Code == "pricing.missing");
    }

    // ------------------------------------------------------------ pricing.missing

    [Fact]
    public async Task PricingMissing_FollowsTheSinglePricedDefinition()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        var readiness = h.Readiness();

        // AgreedPrice null → missing.
        Assert.Null(order.AgreedPrice);
        Assert.Contains(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");

        // Engine wrote 0 (no counted line) → still missing.
        order.AgreedPrice = 0m;
        await h.Db.Context.SaveChangesAsync();
        Assert.Contains(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");

        // A positive agreed price → priced.
        order.AgreedPrice = 450m;
        await h.Db.Context.SaveChangesAsync();
        Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");

        // A manual override counts as priced whatever the amount.
        order.AgreedPrice = 0m;
        order.PriceIsManual = true;
        await h.Db.Context.SaveChangesAsync();
        Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");

        // Hardening 2026-09-10: a one-off price agreement at € 0 is an explicit price too —
        // the backend accepts OneOffFixedAmount = 0 and the engine then derives AgreedPrice = 0.
        order.PriceIsManual = false;
        order.PricingSource = OrderPricingSource.OneOff;
        order.OneOffFixedAmount = 0m;
        order.AgreedPrice = 0m;
        await h.Db.Context.SaveChangesAsync();
        Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");
        // Step 13: priced, but an intentional € 0 is still worth a look (pricing.zero, non-blocking).
        Assert.Equal(1, await readiness.CountDossiersWithAttentionAsync(CancellationToken.None));
    }

    // ------------------------------------------------------------ pricing.zero (step 13)

    /// <summary>
    /// The intentional-zero warning follows the SAME provenance rule as "priced": a one-off
    /// agreement at € 0 and an override at € 0 warn; the engine's empty zero and a positive
    /// price do not. It carries the order AND the activity that carries it, and never blocks.
    /// </summary>
    [Fact]
    public async Task PricingZero_FollowsTheProvenanceRule_AndIsNonBlocking()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        var activityId = dossier.Activities!.Single().Id;
        var readiness = h.Readiness();

        // Engine zero without provenance: unpriced → pricing.missing, never pricing.zero.
        order.AgreedPrice = 0m;
        await h.Db.Context.SaveChangesAsync();
        var engineZero = await readiness.EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.Contains(engineZero, i => i.Code == "pricing.missing" && i.TransportOrderId == order.Id && i.ActivityId == activityId);
        Assert.DoesNotContain(engineZero, i => i.Code == "pricing.zero");

        // One-off agreement at € 0 through the real command: priced + zero warning.
        var tenant = new DevTenantContext(h.TenantId);
        var orders = new TransportOrderService(h.Db.Context, tenant,
            new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        var oneOff = await orders.SetOneOffPriceAsync(order.Id, new SetOneOffPriceRequest(0m, order.Version), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, oneOff.Outcome);
        var oneOffIssues = await readiness.EvaluateAsync(dossier.Id, CancellationToken.None);
        var zero = Assert.Single(oneOffIssues, i => i.Code == "pricing.zero");
        Assert.Equal("Warning", zero.Severity);
        Assert.Equal("prijs", zero.Section);
        Assert.Equal("price", zero.Field);
        Assert.Equal(order.Id, zero.TransportOrderId);
        Assert.Equal(activityId, zero.ActivityId);
        Assert.Equal($"Opdracht {order.OrderNumber} heeft een verkoopprijs van € 0,00. Controleer of dit bewust is.", zero.Message);
        Assert.DoesNotContain(oneOffIssues, i => i.Code == "pricing.missing");
        Assert.DoesNotContain(oneOffIssues, i => i.Severity == "Blocking" && i.Code.StartsWith("pricing."));
        var refreshed = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(1, refreshed.Financials.PricedOrderCount);
        Assert.Equal(1, refreshed.Financials.PricedActivityCount);
        Assert.Equal(1, refreshed.Financials.ZeroPricedActivityCount);
        Assert.Equal(0m, refreshed.Financials.AgreedOrderTotal);

        // A positive agreement: no warning anymore.
        var current = await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        Assert.Equal(TransportOrderOperationOutcome.Success,
            (await orders.SetOneOffPriceAsync(order.Id, new SetOneOffPriceRequest(120m, current.Version), CancellationToken.None)).Outcome);
        Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code.StartsWith("pricing."));

        // Manual override at € 0: priced + zero warning as well.
        var tracked = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == order.Id);
        tracked.PricingSource = OrderPricingSource.Contract;
        tracked.OneOffFixedAmount = null;
        tracked.PriceIsManual = true;
        tracked.PriceOverrideReason = "goodwill";
        tracked.AgreedPrice = 0m;
        await h.Db.Context.SaveChangesAsync();
        Assert.Single(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.zero");

        // Once invoiced nobody can act on it anymore: silent.
        tracked.Status = TransportOrderStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();
        Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.zero");
    }

    // ------------------------------------------------------------ hardening 2026-09-10

    [Fact]
    public async Task StorageOnlyDossier_GetsItsOwnPricingMissing_NeverARouteOrOrderSuggestion()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.Dossiers().CreateAsync(new SaveDossierRequest(
            CustomerId: h.CustomerId, ActivityTypeId: await h.TypeIdAsync("OPSLAG")), CancellationToken.None);

        var activity = Assert.Single(dossier.Activities!);
        Assert.False(activity.HasStops);
        // Step 13: storage is a billable unit with its own price record — the attention item names
        // the activity (the page opens its price editor); no order-shaped issue is ever produced.
        var missing = Assert.Single(dossier.Readiness!, i => i.Code == "pricing.missing");
        Assert.Equal(activity.Id, missing.ActivityId);
        Assert.Null(missing.TransportOrderId);
        Assert.DoesNotContain(dossier.Readiness!, i => i.Code == "route.order_missing" || i.Code == "pricing.none");
        Assert.Equal(1, await h.Readiness().CountDossiersWithAttentionAsync(CancellationToken.None));
    }

    /// <summary>
    /// A dossier with TWO transport orders: every order-level issue names its order, so the page
    /// can target the right editor instead of silently editing the first one; the activity-level
    /// rule names its activity.
    /// </summary>
    [Fact]
    public async Task MultiOrderDossier_EveryOrderLevelIssueCarriesItsOrderIdentity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, first) = await h.DossierWithDraftOrderAsync();
        var withSecond = await h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
            await h.TypeIdAsync("EXPRESS"), CreateLinkedOrder: true, Version: dossier.Version), CancellationToken.None);
        var secondActivity = withSecond!.Activities!.Single(a => a.LinkedTransportOrderId != first.Id);
        var secondId = secondActivity.LinkedTransportOrderId!.Value;
        var withThird = await h.Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
            await h.TypeIdAsync("DIRECT_TRANSPORT"), Version: withSecond.Version), CancellationToken.None);
        var unlinkedActivity = withThird!.Activities!.Single(a => a.LinkedTransportOrderId is null);

        // First order: complete route with a date, but unpriced. Second: no stops at all.
        await AddStopAsync(h, first.Id, StopType.Loading, 1, new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc));
        await AddStopAsync(h, first.Id, StopType.Unloading, 2, null);

        var issues = await h.Readiness().EvaluateAsync(dossier.Id, CancellationToken.None);

        var stops = Assert.Single(issues, i => i.Code == "order.confirm.stops");
        Assert.Equal(secondId, stops.TransportOrderId);
        var date = Assert.Single(issues, i => i.Code == "route.date_missing");
        Assert.Equal(secondId, date.TransportOrderId);
        Assert.Equal(new[] { first.Id, secondId }.Order(),
            issues.Where(i => i.Code == "pricing.missing").Select(i => i.TransportOrderId!.Value).Order());
        var orderMissing = Assert.Single(issues, i => i.Code == "route.order_missing");
        Assert.Equal(unlinkedActivity.Id, orderMissing.ActivityId);
        Assert.Null(orderMissing.TransportOrderId);
        Assert.DoesNotContain(issues, i => i.Code == "pricing.none");
    }

    [Fact]
    public async Task PricingMissing_OnlyForOpenCommercialStatuses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        var readiness = h.Readiness();

        foreach (var status in new[] { TransportOrderStatus.Draft, TransportOrderStatus.Submitted, TransportOrderStatus.Confirmed })
        {
            order.Status = status;
            await h.Db.Context.SaveChangesAsync();
            Assert.Contains(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");
        }

        foreach (var status in new[] { TransportOrderStatus.Planned, TransportOrderStatus.Completed, TransportOrderStatus.Cancelled })
        {
            order.Status = status;
            await h.Db.Context.SaveChangesAsync();
            Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "pricing.missing");
        }
    }

    // ------------------------------------------------------------ route.date_missing regression

    [Fact]
    public async Task DateMissing_IsNotReported_ForASubmittedOrderThatHasADate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        order.Status = TransportOrderStatus.Submitted;
        await h.Db.Context.SaveChangesAsync();
        await AddStopAsync(h, order.Id, StopType.Loading, 1, plannedFrom: new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));
        await AddStopAsync(h, order.Id, StopType.Unloading, 2, plannedFrom: null);

        var issues = await h.Readiness().EvaluateAsync(dossier.Id, CancellationToken.None);

        // Before the fix `!HasDate && Draft || Submitted || Confirmed` fired for EVERY Submitted order.
        Assert.DoesNotContain(issues, i => i.Code == "route.date_missing");
        Assert.DoesNotContain(issues, i => i.Code == "order.confirm.stops");
    }

    [Fact]
    public async Task DateMissing_IsReported_ForAConfirmedOrderWithoutADate_ButNotForAPlannedOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        await AddStopAsync(h, order.Id, StopType.Loading, 1, plannedFrom: null);
        await AddStopAsync(h, order.Id, StopType.Unloading, 2, plannedFrom: null);
        var readiness = h.Readiness();

        order.Status = TransportOrderStatus.Confirmed;
        await h.Db.Context.SaveChangesAsync();
        Assert.Contains(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "route.date_missing");

        order.Status = TransportOrderStatus.Planned;
        await h.Db.Context.SaveChangesAsync();
        Assert.DoesNotContain(await readiness.EvaluateAsync(dossier.Id, CancellationToken.None), i => i.Code == "route.date_missing");
    }

    // ------------------------------------------------------------ dashboard counter

    [Fact]
    public async Task AttentionCount_IncludesAnOpenDossierWhoseLinkedOrderIsUnpriced()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (_, order) = await h.DossierWithDraftOrderAsync();
        // Structurally complete: the activity has its order, no coverage snapshot exists.
        var readiness = h.Readiness();

        Assert.Equal(1, await readiness.CountDossiersWithAttentionAsync(CancellationToken.None));

        order.AgreedPrice = 450m;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(0, await readiness.CountDossiersWithAttentionAsync(CancellationToken.None));

        // A manual override at 0 is priced too — but an intentional € 0 keeps the tile lit (step 13).
        order.AgreedPrice = 0m;
        order.PriceIsManual = true;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(1, await readiness.CountDossiersWithAttentionAsync(CancellationToken.None));

        // Closed commercial phase → the price gap no longer demands attention.
        order.PriceIsManual = false;
        order.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(0, await readiness.CountDossiersWithAttentionAsync(CancellationToken.None));
    }

    /// <summary>
    /// Regression 2026-09-11 (browser smoke): the dossier's agreed price is saved through the
    /// price-only command. With an incomplete route it succeeds, the pricing warning disappears,
    /// the route warnings stay (the route is still incomplete — nothing was weakened), and the
    /// dossier re-read shows the price.
    /// </summary>
    [Fact]
    public async Task AgreedPriceOnAnIncompleteRoute_ClearsPricingMissing_AndKeepsTheRouteIssues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossier, order) = await h.DossierWithDraftOrderAsync();
        // Only a loading stop, no unloading, no date: route incomplete in two ways.
        await AddStopAsync(h, order.Id, StopType.Loading, 1, null);
        var readiness = h.Readiness();
        var before = await readiness.EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.Contains(before, i => i.Code == "pricing.missing" && i.TransportOrderId == order.Id);
        Assert.Contains(before, i => i.Code == "order.confirm.stops" && i.Field == "stops.unloading");
        Assert.Contains(before, i => i.Code == "route.date_missing");

        var tenant = new DevTenantContext(h.TenantId);
        var orders = new TransportOrderService(h.Db.Context, tenant,
            new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        var result = await orders.SetOneOffPriceAsync(order.Id, new SetOneOffPriceRequest(450m, order.Version), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(450m, result.Order!.OneOffFixedAmount);

        var after = await readiness.EvaluateAsync(dossier.Id, CancellationToken.None);
        Assert.DoesNotContain(after, i => i.Code.StartsWith("pricing."));
        Assert.Contains(after, i => i.Code == "order.confirm.stops" && i.Field == "stops.unloading");
        Assert.Contains(after, i => i.Code == "route.date_missing");

        // Full dossier refresh: the order counts as priced and carries € 450.
        var refreshed = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        Assert.Equal(1, refreshed.Financials.PricedOrderCount);
        Assert.True(Assert.Single(refreshed.Orders).IsPriced);
        Assert.Equal(450m, (await h.Db.Context.TransportOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).OneOffFixedAmount);
        // The unloading stop is still missing: the order cannot be confirmed — route rules untouched.
        Assert.DoesNotContain(await h.Db.Context.TransportOrderStops.AsNoTracking().Where(s => s.TransportOrderId == order.Id).ToListAsync(),
            s => s.StopType == StopType.Unloading);
    }
}
