using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Master sprint 2026-09-21 (D2): crane job kinds. An OnSiteLifting order is site stop(s) + a
/// work description — no goods, no loading/unloading route — and is only possible on an activity
/// type flagged <c>SupportsOnSiteWork</c>. Every other order keeps exactly the rules it had.
/// </summary>
public class OnSiteLiftingOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed class AllowAll : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        /// <summary>With the real pricing engine, so the zero-goods pricing paths are exercised.</summary>
        public TransportOrderService Orders(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            var currentUser = new DevCurrentUserContext(Guid.NewGuid());
            return new TransportOrderService(Db.Context, tenant, new AuditService(Db.Context, tenant, currentUser),
                new TestClock(Now), new PricingEngine(Db.Context, tenant), currentUser, new AllowAll());
        }

        public TransportDocumentService Documents() => new(Db.Context, new DevTenantContext(TenantId));
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await SeedTenantAsync(db, tenantId, customerId, "acme");
        return new Harness(db, tenantId, customerId);
    }

    private static async Task SeedTenantAsync(SqliteTestDbContext db, Guid tenantId, Guid customerId, string slug)
    {
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = slug, Slug = slug, IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = $"{slug} Kraanwerken BV", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Bouwbedrijf Peeters", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
    }

    private static async Task<Guid> TypeIdAsync(Harness h, string code, Guid? tenantId = null) =>
        (await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == (tenantId ?? h.TenantId) && t.Code == code)).Id;

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, DateTimeKind.Utc);

    private static TransportOrderStopInput Site(DateTime? from = null, DateTime? to = null, bool manual = false, Guid? id = null) =>
        new(StopType.Site, null, "Werf Dok Noord", "Dok Noord 4", "9000", "Gent", "BE", from, to, null, null,
            Id: id, PlannedToIsManual: manual);

    private static TransportOrderStopInput Transport(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    /// <summary>An on-site lifting order: NO goods description, quantity or cargo lines.</summary>
    private static CreateTransportOrderRequest LiftingRequest(
        Harness h, Guid activityTypeId, string? workDescription = "Dakspanten plaatsen met de 60-tonner",
        IReadOnlyList<TransportOrderStopInput>? stops = null, decimal? durationHours = null) => new(
        h.CustomerId, "REF-KRAAN", new DateOnly(2026, 9, 22), GoodsDescription: null,
        null, null, null, null, null, false, CraneRequired: true, null, null,
        Stops: stops ?? [Site()],
        ActivityTypeId: activityTypeId,
        ActivityDurationHours: durationHours,
        CraneJobKind: CraneJobKind.OnSiteLifting,
        WorkDescription: workDescription,
        LiftLoadWeightKg: 3200m, LiftLoadDimensions: "12 × 2,4 × 1,1 m", LiftRadiusMeters: 18m, LiftHeightMeters: 14.5m,
        LiftConditions: "Verharde ondergrond, stempelplaten nodig", LiftEquipment: "4-sprong ketting, evenaar");

    private static UpdateTransportOrderRequest UpdateFrom(
        TransportOrderDetailDto order, IReadOnlyList<TransportOrderStopInput> stops,
        CraneJobKind? kind, string? workDescription, string? goodsDescription = null) => new(
        order.CustomerId, order.CustomerReference, order.OrderDate, goodsDescription,
        null, null, null, null, null, false, order.CraneRequired, null, null,
        Stops: stops, CraneJobKind: kind, WorkDescription: workDescription);

    [Fact]
    public async Task OnSiteLifting_SavesWithoutGoodsLines_AndNeverCreatesCargoFromTheLiftLoad()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Orders().CreateAsync(LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var order = result.Order!;
        Assert.Equal(CraneJobKind.OnSiteLifting, order.CraneJobKind);
        Assert.Equal("Dakspanten plaatsen met de 60-tonner", order.WorkDescription);
        Assert.Equal(3200m, order.LiftLoadWeightKg);
        Assert.Equal(14.5m, order.LiftHeightMeters);
        Assert.True(order.ActivitySupportsOnSiteWork);
        Assert.Equal(StopType.Site, Assert.Single(order.Stops).StopType);
        // A lift load is lift data — never a goods line, never a header quantity/weight.
        Assert.Empty(order.CargoItems);
        Assert.Empty(await h.Db.Context.CargoItems.Where(c => c.TransportOrderId == order.Id).ToListAsync());
        Assert.Null(order.Quantity);
        Assert.Null(order.WeightKg);
    }

    [Fact]
    public async Task OnSiteLifting_WithoutWorkDescription_IsAFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var typeId = await TypeIdAsync(h, "KRAANTRANSPORT");

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Orders().CreateAsync(LiftingRequest(h, typeId, workDescription: "   "), CancellationToken.None));

        Assert.True(error.FieldErrors!.ContainsKey("workDescription"));
        Assert.Contains("werkomschrijving", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await h.Db.Context.TransportOrders.Where(o => o.TenantId == h.TenantId).ToListAsync());
    }

    [Fact]
    public async Task OnSiteLifting_WithoutSiteLocation_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var typeId = await TypeIdAsync(h, "KRAANTRANSPORT");

        // No stop at all.
        var noStop = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Orders().CreateAsync(LiftingRequest(h, typeId, stops: []), CancellationToken.None));
        Assert.True(noStop.FieldErrors!.ContainsKey("stops"));

        // A site stop without location, city or address.
        var nowhere = new TransportOrderStopInput(StopType.Site, null, "Werf", null, null, null, "BE", null, null, null, null);
        var placeless = await h.Orders().CreateAsync(LiftingRequest(h, typeId, stops: [nowhere]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, placeless.Outcome);
        Assert.Contains("werfstop", placeless.Error!);

        // An address alone locates a site.
        var addressOnly = new TransportOrderStopInput(StopType.Site, null, null, "Kaai 12", null, null, null, null, null, null, null);
        var ok = await h.Orders().CreateAsync(LiftingRequest(h, typeId, stops: [addressOnly]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, ok.Outcome);
    }

    [Fact]
    public async Task OnSiteLifting_WithALoadingStop_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Orders().CreateAsync(
            LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT"), stops: [Transport(StopType.Loading, "Antwerpen"), Site()]),
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("laad- of losstops", result.Error!);
    }

    [Fact]
    public async Task SiteStop_OnANormalOrder_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Orders().CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, "REF-1", new DateOnly(2026, 9, 22), "12 paletten", null, null, null, null, null, false, false, null, null,
            Stops: [Transport(StopType.Loading, "Antwerpen"), Site(), Transport(StopType.Unloading, "Gent")]), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("werfstop", result.Error!);
    }

    [Fact]
    public async Task TransportWithCrane_StillEnforcesTheExistingRules()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var typeId = await TypeIdAsync(h, "KRAANTRANSPORT");
        CreateTransportOrderRequest Request(string? goods, IReadOnlyList<CargoItemInput>? cargo = null) => new(
            h.CustomerId, "REF-1", new DateOnly(2026, 9, 22), goods, null, null, null, null, null, false, true, null, null,
            Stops: [Transport(StopType.Loading, "Antwerpen"), Transport(StopType.Unloading, "Gent")],
            CargoItems: cargo, ActivityTypeId: typeId, CraneJobKind: CraneJobKind.TransportWithCrane);

        // The minimal goods rule still applies…
        var noGoods = await h.Orders().CreateAsync(Request(goods: null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, noGoods.Outcome);
        Assert.Contains("goederenlijn", noGoods.Error!);

        // …goods lines still auto-link to the single loading/unloading stop…
        var created = await h.Orders().CreateAsync(
            Request("Betonelementen", [new CargoItemInput("Betonelement", null, 4, "stuks", null)]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var line = Assert.Single(created.Order!.CargoItems);
        Assert.Equal(created.Order.Stops.Single(s => s.StopType == StopType.Loading).Id, line.LoadingStopId);
        Assert.Equal(created.Order.Stops.Single(s => s.StopType == StopType.Unloading).Id, line.UnloadingStopId);

        // …and confirming still needs a loading AND an unloading stop.
        var loadingOnly = await h.Orders().CreateAsync(Request("Betonelementen") with
        {
            Stops = [Transport(StopType.Loading, "Antwerpen")],
        }, CancellationToken.None);
        var confirm = await h.Orders().ChangeStatusAsync(loadingOnly.Order!.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, confirm.Outcome);
        Assert.Contains("laad- en één losstop", confirm.Error!);
    }

    [Fact]
    public async Task ActivityTypeWithoutTheFlag_RefusesOnSiteLifting()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // DISTRIBUTIE has stops but not SupportsOnSiteWork; the default type (no ActivityTypeId) neither.
        foreach (var typeId in new Guid?[] { await TypeIdAsync(h, "DISTRIBUTIE"), null })
        {
            var request = LiftingRequest(h, Guid.Empty) with { ActivityTypeId = typeId };
            var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
                h.Orders().CreateAsync(request, CancellationToken.None));
            Assert.True(error.FieldErrors!.ContainsKey("craneJobKind"));
        }

        // The rule is the FLAG, not the code: a tenant that flags Distributie may use it.
        var distributie = await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == "DISTRIBUTIE");
        distributie.SupportsOnSiteWork = true;
        await h.Db.Context.SaveChangesAsync();
        var allowed = await h.Orders().CreateAsync(LiftingRequest(h, distributie.Id), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, allowed.Outcome);
    }

    [Fact]
    public async Task Confirming_OnSiteLifting_NeedsASiteStopAndWorkDescription_NotALoadingAndUnloadingStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Orders().CreateAsync(LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT")), CancellationToken.None);
        var confirmed = await h.Orders().ChangeStatusAsync(created.Order!.Id, TransportOrderStatus.Confirmed, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);
        Assert.Equal(TransportOrderStatus.Confirmed, confirmed.Order!.Status);

        Assert.NotNull(CraneJobRules.ConfirmationError(CraneJobKind.OnSiteLifting, null, [Site()]));
        Assert.NotNull(CraneJobRules.ConfirmationError(CraneJobKind.OnSiteLifting, "Hijsen", []));
        Assert.Null(CraneJobRules.ConfirmationError(CraneJobKind.OnSiteLifting, "Hijsen", [Site()]));
    }

    [Fact]
    public async Task SiteStopEnd_FollowsStartAndDuration_UnlessManual()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var typeId = await TypeIdAsync(h, "KRAANTRANSPORT");

        // 22:00 + 4 h runs past midnight: the end lands on the next day.
        var created = await h.Orders().CreateAsync(
            LiftingRequest(h, typeId, stops: [Site(At(22, 22))], durationHours: 4m), CancellationToken.None);
        var stop = Assert.Single(created.Order!.Stops);
        Assert.Equal(At(23, 2), stop.PlannedTo);
        Assert.False(stop.PlannedToIsManual);
        Assert.Equal(4m, created.Order.ActivityDurationHours);

        // Moving the start moves the end — even when the client echoes the old end.
        var moved = await h.Orders().UpdateAsync(created.Order.Id, UpdateFrom(created.Order,
            [Site(At(22, 8, 30), stop.PlannedTo, id: stop.Id)], CraneJobKind.OnSiteLifting, created.Order.WorkDescription),
            CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, moved.Outcome);
        Assert.Equal(stop.Id, Assert.Single(moved.Order!.Stops).Id);
        Assert.Equal(At(22, 12, 30), moved.Order.Stops[0].PlannedTo);

        // A manual end is the planner's own value and survives the next start change.
        var manual = await h.Orders().UpdateAsync(created.Order.Id, UpdateFrom(moved.Order,
            [Site(At(22, 8, 30), At(22, 17), manual: true, id: stop.Id)], CraneJobKind.OnSiteLifting, created.Order.WorkDescription),
            CancellationToken.None);
        Assert.Equal(At(22, 17), manual.Order!.Stops[0].PlannedTo);
        var startMoved = await h.Orders().UpdateAsync(created.Order.Id, UpdateFrom(manual.Order,
            [Site(At(22, 10), At(22, 17), manual: true, id: stop.Id)], CraneJobKind.OnSiteLifting, created.Order.WorkDescription),
            CancellationToken.None);
        Assert.Equal(At(22, 17), startMoved.Order!.Stops[0].PlannedTo);
        Assert.True(startMoved.Order.Stops[0].PlannedToIsManual);
    }

    [Fact]
    public async Task Update_ThatDoesNotSendTheCraneFields_LeavesThemAsStored()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Orders().CreateAsync(LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT")), CancellationToken.None);
        var stop = created.Order!.Stops[0];

        // An older client: no CraneJobKind, no WorkDescription, no lift data on the PUT.
        var updated = await h.Orders().UpdateAsync(created.Order.Id,
            UpdateFrom(created.Order, [Site(id: stop.Id)], kind: null, workDescription: null), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(CraneJobKind.OnSiteLifting, updated.Order!.CraneJobKind);
        Assert.Equal(created.Order.WorkDescription, updated.Order.WorkDescription);
        Assert.Equal(3200m, updated.Order.LiftLoadWeightKg);
    }

    [Fact]
    public async Task Pricing_WorksWithZeroGoodsLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Contract pricing with nothing to price: no crash, no "unpriced goods" coverage.
        var created = await h.Orders().CreateAsync(LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT")), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.DoesNotContain(created.Order!.PricingSnapshot?.Coverage ?? [], c => c.Status != "Full");
        Assert.Contains(created.Order.PricingSnapshot?.CoverageStatus, new[] { null, "NotApplicable" });

        // One-off agreed price (fixed price for the job).
        var oneOff = await h.Orders().SetOneOffPriceAsync(created.Order.Id, new SetOneOffPriceRequest(1850m), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, oneOff.Outcome);
        Assert.Equal(1850m, oneOff.Order!.AgreedPrice);
        Assert.Equal("NotApplicable", oneOff.Order.PricingSnapshot!.CoverageStatus);

        // Manual sales lines: hours × rate and a surcharge as a plain amount.
        var lines = await h.Orders().SaveOrderPriceLinesAsync(created.Order.Id,
        [
            new SaveOrderPriceLineRequest(null, "Kraanuren", 4m, 145m, null, null, Unit: "UUR"),
            new SaveOrderPriceLineRequest(null, "Verplaatsing", null, null, 95m, null),
        ], CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, lines.Outcome);
        Assert.Equal(1850m + 580m + 95m, lines.Order!.AgreedPrice);

        // The price can be confirmed: no goods means no unpriced goods.
        var confirmed = await h.Orders().ConfirmOrderPricingAsync(created.Order.Id, null, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);
    }

    [Fact]
    public async Task DocumentStrategy_ResolvesWorkOrder_AndTheRendererReturnsAPdf()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Orders().CreateAsync(
            LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT"), stops: [Site(At(22, 6))], durationHours: 1.5m), CancellationToken.None);

        var strategy = await h.Documents().GetStrategyAsync(created.Order!.Id, CancellationToken.None);
        Assert.Equal(DocumentStrategyResolver.KindWorkOrder, strategy!.Kind);
        Assert.False(strategy.NoneRequired);

        var rendered = await h.Documents().RenderAsync(created.Order.Id, "work-order", CancellationToken.None);
        Assert.NotNull(rendered);
        Assert.Equal($"werkbon-{created.Order.OrderNumber}.pdf", rendered.Value.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(rendered.Value.Content, 0, 4));
    }

    [Fact]
    public void DocumentStrategy_NeverRequiresACmrForOnSiteWork_ButKeepsOverrideAndCustomerPrecedence()
    {
        // Cross-border + ADR would be a CMR for a transport; on-site work gets its work order.
        var onSite = DocumentStrategyResolver.Resolve(null, "GenerateOwn", crossBorder: true, adrRequired: true, null, [], onSiteWork: true);
        Assert.Equal(DocumentStrategyResolver.KindWorkOrder, onSite.Kind);
        Assert.True(onSite.GeneratesOwnDocument);

        var transport = DocumentStrategyResolver.Resolve(null, "GenerateOwn", crossBorder: true, adrRequired: true, null, []);
        Assert.Equal(DocumentStrategyResolver.KindCmr, transport.Kind);

        // Precedence unchanged: order override first, then the customer strategy.
        Assert.True(DocumentStrategyResolver.Resolve("NoneRequired", "GenerateOwn", false, false, null, [], onSiteWork: true).NoneRequired);
        Assert.True(DocumentStrategyResolver.Resolve(null, "CustomerDocument", false, false, null, [], onSiteWork: true).UsesCustomerDocument);
    }

    [Fact]
    public void WorkOrderRenderer_PrintsOnlyWhatIsFilled_AndStaysAValidPdf()
    {
        var party = new TransportDocumentParty("Acme Kraanwerken BV", "Kaai 1 9000 Gent", "BE0123456789");
        var snapshot = new TransportDocumentSnapshot(
            DocumentStrategyResolver.KindWorkOrder, "ORD-0001", new DateOnly(2026, 9, 22), party,
            new TransportDocumentParty("Bouwbedrijf Peeters", null, null),
            [new TransportDocumentStop("Werf", "Werf Dok Noord", "Dok Noord 4 9000 Gent", null)],
            Lines: [], TotalWeightKg: null, CustomerReference: "REF-KRAAN", Notes: null,
            WorkOrder: new TransportDocumentWorkOrder(
                string.Join(" ", Enumerable.Repeat("Dakspanten plaatsen volgens hijsplan.", 30)),
                new DateTime(2026, 9, 22, 22, 0, 0), new DateTime(2026, 9, 23, 2, 0, 0), 4m,
                3200m, null, 18m, null, null, "4-sprong ketting"));

        var pdf = TransportDocumentRenderer.Render(snapshot);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        // The batch renderer takes the same layout.
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(TransportDocumentRenderer.RenderBatch([snapshot, snapshot]), 0, 4));
    }

    [Fact]
    public async Task TenantIsolation_ForeignActivityTypeAndForeignOrderAreRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        await SeedTenantAsync(h.Db, otherTenantId, Guid.NewGuid(), "other");

        // Another tenant's (flagged!) activity type proves nothing here.
        var foreignType = await h.Orders().CreateAsync(
            LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT", otherTenantId)), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidReference, foreignType.Outcome);

        // Another tenant can neither read, edit, price nor print this tenant's order.
        var created = await h.Orders().CreateAsync(LiftingRequest(h, await TypeIdAsync(h, "KRAANTRANSPORT")), CancellationToken.None);
        var foreign = h.Orders(otherTenantId);
        Assert.Null(await foreign.GetByIdAsync(created.Order!.Id, CancellationToken.None));
        var update = await foreign.UpdateAsync(created.Order.Id,
            UpdateFrom(created.Order, [Site()], CraneJobKind.OnSiteLifting, "Overgenomen"), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, update.Outcome);
        Assert.Equal(TransportOrderOperationOutcome.NotFound,
            (await foreign.SetOneOffPriceAsync(created.Order.Id, new SetOneOffPriceRequest(1m), CancellationToken.None)).Outcome);
        Assert.Null(await new TransportDocumentService(h.Db.Context, new DevTenantContext(otherTenantId))
            .RenderAsync(created.Order.Id, "work-order", CancellationToken.None));
    }
}
