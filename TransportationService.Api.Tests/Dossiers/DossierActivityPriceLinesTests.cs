using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Accounting.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Master sprint 2026-09-21 D5 — a standalone billable activity (Plateau, Opslag, Kraanwerk) can
/// be priced through its own SALES LINES. The server computes every amount, the lines total lands
/// once in <c>AgreedPrice</c> (no double counting in the dossier total), "free" is an explicit
/// confirmation and the derived <c>PriceStatus</c> follows provenance — never magnitude.
/// </summary>
public class DossierActivityPriceLinesTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);

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

        public async Task<Guid> TypeIdAsync(string code) =>
            (await Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == TenantId && t.Code == code)).Id;

        public async Task<DossierDetailDto> DossierWithAsync(string typeCode) =>
            await Dossiers().CreateAsync(new SaveDossierRequest(CustomerId: CustomerId, ActivityTypeId: await TypeIdAsync(typeCode)), CancellationToken.None);

        public async Task<DossierDetailDto> AddActivityAsync(DossierDetailDto dossier, string typeCode) =>
            (await Activities().AddAsync(dossier.Id, new SaveDossierActivityRequest(
                await TypeIdAsync(typeCode), Version: dossier.Version), CancellationToken.None))!;

        public Task<DossierDetailDto?> SetLinesAsync(
            Guid dossierId, Guid activityId, IReadOnlyList<DossierActivityPriceLineInput> lines,
            bool freeConfirmed = false, Guid? version = null) =>
            Pricing().SetPriceLinesAsync(dossierId, activityId,
                new SetActivityPriceLinesRequest(version, lines, freeConfirmed), CancellationToken.None);

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

    private static DossierActivityPriceLineInput Line(string label, decimal quantity, decimal unitPrice, string? unit = null,
        Guid? id = null, Guid? salesCategoryId = null, decimal? amount = null) =>
        new(id, label, quantity, unit, unitPrice, salesCategoryId, amount);

    // ------------------------------------------------------------ lines → price

    [Fact]
    public async Task PlateauWithTwoLines_IsPricedAtTheirTotal_AndCountsExactlyOnceInTheDossier()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activity = Only(dossier);
        Assert.Equal("NotPriced", activity.PriceStatus);
        Assert.Empty(activity.PriceLines!);

        var updated = (await h.SetLinesAsync(dossier.Id, activity.Id,
            [Line("Plateau halve dag", 4m, 25m, "uur"), Line("Verplaatsing", 1m, 25m)]))!;

        var priced = Only(updated);
        Assert.Equal("Lines", priced.PricingSource);
        Assert.Equal(125m, priced.AgreedPrice);
        Assert.True(priced.IsPriced);
        Assert.Equal("Priced", priced.PriceStatus);
        Assert.False(priced.FreeConfirmed);
        Assert.Equal(new[] { 100m, 25m }, priced.PriceLines!.Select(l => l.Amount));
        Assert.Equal(new[] { 1, 2 }, priced.PriceLines!.Select(l => l.Sequence));
        Assert.Equal("uur", priced.PriceLines![0].Unit);
        // Exactly once: the lines never add to the total next to AgreedPrice.
        Assert.Equal(125m, updated.Financials.AgreedOrderTotal);
        Assert.Equal((1, 1, 0, 0), (updated.Financials.BillableActivityCount, updated.Financials.PricedActivityCount,
            updated.Financials.UnpricedActivityCount, updated.Financials.ZeroPricedActivityCount));
        Assert.DoesNotContain(updated.Readiness!, i => i.Code.StartsWith("pricing."));
        // A structural child list changed → the dossier token moved (like the other activity mutations).
        Assert.NotEqual(dossier.Version, updated.Version);

        // The list projection (single SQL statement) agrees — parity with the detail.
        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(125m, row.AgreedPriceTotal);
        Assert.Equal((1, 1, 0), (row.BillableActivityCount, row.PricedActivityCount, row.ZeroPricedActivityCount));

        var stored = await h.Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync(p => p.DossierActivityId == activity.Id);
        Assert.Equal((ActivityPricingSource.Lines, (decimal?)null, (decimal?)125m), (stored.PricingSource, stored.FixedAmount, stored.AgreedPrice));
        Assert.Contains(await h.Db.Context.AuditLogs.AsNoTracking().ToListAsync(),
            a => a.EntityType == "DossierActivityPricing" && a.EntityId == activity.Id.ToString() && a.Action == "priceLinesSet");
    }

    [Fact]
    public async Task TheServerComputesEveryAmount_AClientSentAmountIsIgnored()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");

        var updated = (await h.SetLinesAsync(dossier.Id, Only(dossier).Id,
            [Line("Uren", 2.5m, 33.333m, amount: 9999m)]))!;

        var line = Assert.Single(Only(updated).PriceLines!);
        Assert.Equal(83.33m, line.Amount); // round(2.5 × 33.333, 2)
        Assert.Equal(83.33m, Only(updated).AgreedPrice);
    }

    [Fact]
    public async Task IdPreservingReplace_KeepsUpdatesAndRemovesLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;
        var first = Only((await h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 100m), Line("B", 1m, 25m)]))!);
        var keep = first.PriceLines![0];

        var second = Only((await h.SetLinesAsync(dossier.Id, activityId,
            [Line("A gewijzigd", 2m, 100m, id: keep.Id), Line("C", 1m, 10m)], version: first.PricingVersion))!);

        Assert.Equal(210m, second.AgreedPrice);
        Assert.Equal(keep.Id, second.PriceLines![0].Id);
        Assert.Equal("A gewijzigd", second.PriceLines![0].Label);
        Assert.DoesNotContain(second.PriceLines!, l => l.Label == "B");
        Assert.Equal(2, await h.Db.Context.DossierActivityPriceLines.CountAsync());
    }

    // ------------------------------------------------------------ free vs not priced

    [Fact]
    public async Task EmptyListWithFreeConfirmed_IsExplicitlyFree_WithoutTheZeroWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");

        var updated = (await h.SetLinesAsync(dossier.Id, Only(dossier).Id, [], freeConfirmed: true))!;

        var activity = Only(updated);
        Assert.Equal("Free", activity.PriceStatus);
        Assert.True(activity.IsPriced);
        Assert.True(activity.FreeConfirmed);
        Assert.Equal(0m, activity.AgreedPrice);
        Assert.Equal(0m, updated.Financials.AgreedOrderTotal);
        Assert.Equal((1, 0, 0), (updated.Financials.PricedActivityCount, updated.Financials.ZeroPricedActivityCount, updated.Financials.UnpricedActivityCount));
        Assert.DoesNotContain(updated.Readiness!, i => i.Code is "pricing.zero" or "pricing.missing");
        Assert.Equal(0, await new DossierReadinessService(h.Db.Context, new DevTenantContext(h.TenantId)).CountDossiersWithAttentionAsync(CancellationToken.None));

        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(0m, row.AgreedPriceTotal); // a real € 0,00, not "no price"
        Assert.Equal((1, 0), (row.PricedActivityCount, row.ZeroPricedActivityCount));
    }

    [Fact]
    public async Task LinesThatTotalZero_WithoutConfirmation_KeepTheZeroWarning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");

        var updated = (await h.SetLinesAsync(dossier.Id, Only(dossier).Id, [Line("Kosteloos", 1m, 0m)]))!;

        Assert.Equal("Priced", Only(updated).PriceStatus);
        Assert.Equal(1, updated.Financials.ZeroPricedActivityCount);
        Assert.Contains(updated.Readiness!, i => i.Code == "pricing.zero" && i.ActivityId == Only(updated).Id);
    }

    [Fact]
    public async Task EmptyListWithoutConfirmation_GoesBackToNotPriced_AndLeavesTheTotalEmpty()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;
        var priced = Only((await h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 100m)]))!);

        var cleared = (await h.SetLinesAsync(dossier.Id, activityId, [], version: priced.PricingVersion))!;

        var activity = Only(cleared);
        Assert.Equal("NotPriced", activity.PriceStatus);
        Assert.False(activity.IsPriced);
        Assert.Null(activity.AgreedPrice); // never 0
        Assert.Equal("None", activity.PricingSource);
        Assert.Empty(activity.PriceLines!);
        Assert.Equal((0, 1), (cleared.Financials.PricedActivityCount, cleared.Financials.UnpricedActivityCount));
        var missing = Assert.Single(cleared.Readiness!, i => i.Code == "pricing.missing");
        Assert.Equal("Plateau: verkoopprijs ontbreekt.", missing.Message);
        Assert.Equal(activityId, missing.ActivityId);
        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Null(row.AgreedPriceTotal);
    }

    // ------------------------------------------------------------ fixed ↔ lines

    [Fact]
    public async Task SwitchingBetweenFixedAndLines_IsAllowed_AndNeverCountsTwice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;
        var withFixed = Only((await h.SetPriceAsync(dossier.Id, activityId, 300m))!);

        var withLines = (await h.SetLinesAsync(dossier.Id, activityId, [Line("Uren", 4m, 50m)], version: withFixed.PricingVersion))!;

        Assert.Equal(("Lines", 200m), (Only(withLines).PricingSource, Only(withLines).AgreedPrice));
        Assert.Equal(200m, withLines.Financials.AgreedOrderTotal);
        Assert.Null((await h.Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync()).FixedAmount);

        var backToFixed = (await h.SetPriceAsync(dossier.Id, activityId, 350m, Only(withLines).PricingVersion))!;

        Assert.Equal(("OneOff", 350m), (Only(backToFixed).PricingSource, Only(backToFixed).AgreedPrice));
        Assert.Empty(Only(backToFixed).PriceLines!);
        Assert.Equal(350m, backToFixed.Financials.AgreedOrderTotal);
        Assert.Equal(0, await h.Db.Context.DossierActivityPriceLines.CountAsync());
        // Both switches are in the audit trail.
        var actions = (await h.Db.Context.AuditLogs.AsNoTracking().Where(a => a.EntityType == "DossierActivityPricing").ToListAsync())
            .Select(a => a.Action).ToList();
        Assert.Equal(2, actions.Count(a => a == "agreedPriceSet"));
        Assert.Contains("priceLinesSet", actions);
    }

    [Fact]
    public async Task AFixedZero_IsNeverSilentlyFree()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;
        var free = Only((await h.SetLinesAsync(dossier.Id, activityId, [], freeConfirmed: true))!);

        var fixedZero = (await h.SetPriceAsync(dossier.Id, activityId, 0m, free.PricingVersion))!;

        Assert.False(Only(fixedZero).FreeConfirmed);
        Assert.Equal("Priced", Only(fixedZero).PriceStatus);
        Assert.Contains(fixedZero.Readiness!, i => i.Code == "pricing.zero");
    }

    // ------------------------------------------------------------ refusals

    [Fact]
    public async Task TransportActivity_IsRefused_WithTheOrderMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("DIRECT_TRANSPORT");

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.SetLinesAsync(dossier.Id, Only(dossier).Id, [Line("A", 1m, 100m)]));

        Assert.Contains("Transportactiviteiten worden via hun transportopdracht geprijsd.", ex.Message);
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync());
    }

    [Fact]
    public async Task NonBillableType_ClosedDossier_AndInvalidLines_AreRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.SetLinesAsync(dossier.Id, activityId, [Line(" ", 1m, 10m)]));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.SetLinesAsync(dossier.Id, activityId, [Line("A", 0m, 10m)]));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, -1m)]));
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync());

        var type = await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == "PLATEAU");
        type.IsBillable = false;
        await h.Db.Context.SaveChangesAsync();
        var notBillable = await Assert.ThrowsAsync<DomainValidationException>(() => h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 10m)]));
        Assert.Contains("niet factureerbaar", notBillable.Message);
        // No commercial status at all for a non-billable type.
        Assert.Null(Only((await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!).PriceStatus);

        type.IsBillable = true;
        await h.Db.Context.SaveChangesAsync();
        await DossierTestLifecycle.CloseAsync(h.Db.Context, h.TenantId, dossier.Id);
        await Assert.ThrowsAsync<DomainValidationException>(() => h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 10m)]));
    }

    [Fact]
    public async Task StaleVersion_YieldsConflict_AndChangesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;
        await h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 100m)]);

        var conflict = await Assert.ThrowsAsync<DossierVersionConflictException>(
            () => h.SetLinesAsync(dossier.Id, activityId, [Line("B", 1m, 999m)], version: Guid.NewGuid()));

        Assert.Equal(100m, Only(conflict.Current).AgreedPrice);
        Assert.Equal("A", Assert.Single(await h.Db.Context.DossierActivityPriceLines.AsNoTracking().ToListAsync()).Label);
    }

    [Fact]
    public async Task LockedOrInvoicedPrice_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;
        await h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 100m)]);
        var stored = await h.Db.Context.DossierActivityPricings.SingleAsync();
        stored.Status = OrderPricingStatus.Locked;
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.SetLinesAsync(dossier.Id, activityId, [Line("B", 1m, 50m)]));

        Assert.Equal(DossierActivityPricingService.LockedMessage, ex.Message);
        Assert.Equal(100m, (await h.Db.Context.DossierActivityPricings.AsNoTracking().SingleAsync()).AgreedPrice);
    }

    [Fact]
    public async Task ALineIdOfAnotherActivity_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");
        dossier = await h.AddActivityAsync(dossier, "OPSLAG");
        var plateau = dossier.Activities!.Single(a => a.ActivityTypeCode == "PLATEAU");
        var storage = dossier.Activities!.Single(a => a.ActivityTypeCode == "OPSLAG");
        var foreignLine = (await h.SetLinesAsync(dossier.Id, storage.Id, [Line("Opslag", 1m, 80m)]))!
            .Activities!.Single(a => a.Id == storage.Id).PriceLines!.Single();

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.SetLinesAsync(dossier.Id, plateau.Id, [Line("Gekaapt", 1m, 1m, id: foreignLine.Id)]));

        Assert.Equal("Opslag", (await h.Db.Context.DossierActivityPriceLines.AsNoTracking().SingleAsync()).Label);
    }

    [Fact]
    public async Task ASalesCategoryOfAnotherTenant_IsRefused_AndAnOwnOneIsStored()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var own = new SalesCategory { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "KRAAN", Name = "Kraanwerk" };
        var foreign = new SalesCategory { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Code = "X", Name = "Vreemd" };
        h.Db.Context.AddRange(own, foreign);
        await h.Db.Context.SaveChangesAsync();
        var dossier = await h.DossierWithAsync("PLATEAU");
        var activityId = Only(dossier).Id;

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 10m, salesCategoryId: foreign.Id)]));
        Assert.Equal(0, await h.Db.Context.DossierActivityPriceLines.CountAsync());

        var saved = (await h.SetLinesAsync(dossier.Id, activityId, [Line("A", 1m, 10m, salesCategoryId: own.Id)]))!;
        Assert.Equal(own.Id, Only(saved).PriceLines!.Single().SalesCategoryId);
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeOrPriceTheActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");

        var foreign = await h.Pricing(Guid.NewGuid()).SetPriceLinesAsync(dossier.Id, Only(dossier).Id,
            new SetActivityPriceLinesRequest(null, [Line("A", 1m, 10m)]), CancellationToken.None);

        Assert.Null(foreign);
        Assert.Equal(0, await h.Db.Context.DossierActivityPricings.CountAsync());
    }

    // ------------------------------------------------------------ order-backed priceStatus + numbered readiness

    [Fact]
    public async Task OrderBackedActivity_DerivesItsPriceStatusFromTheOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("DIRECT_TRANSPORT");
        var activityId = Only(dossier).Id;
        // No order yet → simply not priced.
        Assert.Equal("NotPriced", Only(dossier).PriceStatus);
        dossier = (await h.Activities().CreateOrderForActivityAsync(dossier.Id, activityId, dossier.Version, CancellationToken.None))!;
        var orderId = Only(dossier).LinkedTransportOrderId!.Value;

        async Task<string?> StatusAsync(Action<TransportOrder> order, Action<TransportOrderPricingSnapshot>? snapshot = null)
        {
            var entity = await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);
            entity.PriceIsManual = false;
            entity.PricingSource = OrderPricingSource.Contract;
            entity.OneOffFixedAmount = null;
            entity.AgreedPrice = null;
            order(entity);
            var row = await h.Db.Context.TransportOrderPricingSnapshots.SingleOrDefaultAsync(s => s.TransportOrderId == orderId);
            if (row is null)
            {
                row = new TransportOrderPricingSnapshot
                {
                    Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = orderId,
                    TariffDate = new DateOnly(2026, 9, 21), Currency = "EUR",
                };
                h.Db.Context.Add(row);
            }

            row.CoverageStatus = "Full";
            row.IsStale = false;
            snapshot?.Invoke(row);
            await h.Db.Context.SaveChangesAsync();
            return Only((await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!).PriceStatus;
        }

        Assert.Equal("NotPriced", await StatusAsync(_ => { }));
        // The engine's empty zero is NOT a price — and certainly not "Free".
        Assert.Equal("NotPriced", await StatusAsync(o => o.AgreedPrice = 0m));
        Assert.Equal("Free", await StatusAsync(o => { o.PricingSource = OrderPricingSource.OneOff; o.OneOffFixedAmount = 0m; o.AgreedPrice = 0m; }));
        Assert.Equal("PartiallyPriced", await StatusAsync(o => o.AgreedPrice = 450m, s => s.CoverageStatus = "Partial"));
        Assert.Equal("PartiallyPriced", await StatusAsync(o => o.AgreedPrice = 450m, s => s.CoverageStatus = "None"));
        Assert.Equal("PartiallyPriced", await StatusAsync(o => o.AgreedPrice = 450m, s => s.IsStale = true));
        Assert.Equal("Priced", await StatusAsync(o => o.AgreedPrice = 450m));
        Assert.Equal("Priced", await StatusAsync(o => o.AgreedPrice = 450m, s => s.CoverageStatus = "NotApplicable"));
    }

    [Fact]
    public async Task ReadinessMessages_CarryTheOrderNumber_AndBothIds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("DIRECT_TRANSPORT");
        var activityId = Only(dossier).Id;
        dossier = (await h.Activities().CreateOrderForActivityAsync(dossier.Id, activityId, dossier.Version, CancellationToken.None))!;
        var orderId = Only(dossier).LinkedTransportOrderId!.Value;
        var number = Only(dossier).LinkedOrderNumber!;

        var price = Assert.Single(dossier.Readiness!, i => i.Code == "pricing.missing");
        Assert.Equal($"{number}: verkoopprijs ontbreekt.", price.Message);
        Assert.Equal((orderId, activityId), (price.TransportOrderId, price.ActivityId));

        var date = Assert.Single(dossier.Readiness!, i => i.Code == "route.date_missing");
        Assert.Equal($"{number}: planningsdatum ontbreekt.", date.Message);
        Assert.Equal((orderId, activityId), (date.TransportOrderId, date.ActivityId));

        // A transport activity WITHOUT its order keeps the route incomplete, whatever the others say.
        var second = await h.AddActivityAsync(dossier, "DIRECT_TRANSPORT");
        var orderless = second.Activities!.Single(a => a.LinkedTransportOrderId is null);
        Assert.Contains(second.Readiness!, i => i.Code == "route.order_missing" && i.ActivityId == orderless.Id);
    }

    // ------------------------------------------------------------ provenance parity (SQL ↔ C#)

    [Fact]
    public async Task Parity_ListAndDetail_FollowActivityPricingState()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var dossier = await h.DossierWithAsync("PLATEAU");          // fixed 300
        dossier = await h.AddActivityAsync(dossier, "OPSLAG");      // lines 125
        dossier = await h.AddActivityAsync(dossier, "KRAANWERK");   // free (confirmed)
        dossier = await h.AddActivityAsync(dossier, "OVERIG");      // fixed 0 (unconfirmed)
        Guid Id(string code) => dossier.Activities!.Single(a => a.ActivityTypeCode == code).Id;
        await h.SetPriceAsync(dossier.Id, Id("PLATEAU"), 300m);
        await h.SetLinesAsync(dossier.Id, Id("OPSLAG"), [Line("A", 1m, 100m), Line("B", 1m, 25m)]);
        await h.SetLinesAsync(dossier.Id, Id("KRAANWERK"), [], freeConfirmed: true);
        await h.SetPriceAsync(dossier.Id, Id("OVERIG"), 0m);

        var pricings = await h.Db.Context.DossierActivityPricings.AsNoTracking().ToListAsync();
        var expectedPriced = pricings.Count(ActivityPricingState.IsPriced);
        var expectedZero = pricings.Count(ActivityPricingState.IsUnconfirmedZero);
        Assert.Equal(4, expectedPriced);
        Assert.Equal(1, expectedZero);
        // The translated expressions select the same rows as the compiled ones.
        Assert.Equal(expectedPriced, await h.Db.Context.DossierActivityPricings.CountAsync(ActivityPricingState.IsPricedExpression));
        Assert.Equal(expectedZero, await h.Db.Context.DossierActivityPricings.CountAsync(ActivityPricingState.IsUnconfirmedZeroExpression));

        var detail = (await h.Dossiers().GetAsync(dossier.Id, CancellationToken.None))!;
        var row = (await h.Dossiers().ListAsync(null, null, null, CancellationToken.None)).Single(d => d.Id == dossier.Id);
        Assert.Equal(425m, detail.Financials.AgreedOrderTotal);
        Assert.Equal(425m, row.AgreedPriceTotal);
        Assert.Equal((detail.Financials.BillableActivityCount, detail.Financials.PricedActivityCount, detail.Financials.ZeroPricedActivityCount),
            (row.BillableActivityCount, row.PricedActivityCount, row.ZeroPricedActivityCount));
        Assert.Equal(expectedPriced, row.PricedActivityCount);
        Assert.Equal(expectedZero, row.ZeroPricedActivityCount);
    }
}
