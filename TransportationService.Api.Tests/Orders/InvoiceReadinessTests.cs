using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Wave 2 §5+§6: the typed coverage projection written with every calculation, the stale flag
/// when inputs change without a recalculation, and the deterministic InvoiceReadiness
/// projection (scenario 13: waiting time without price → ReviewRequired; scenario 14: clean
/// dossier → ReadyForInvoice automatically). The evaluator never fires notifications.
/// </summary>
public class InvoiceReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Orders, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid PalletUnitId)
    {
        /// <summary>An engine-less service: pricing-input edits can never recalculate → stale path.</summary>
        public TransportOrderService EnginelessOrders()
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(Guid.NewGuid());
            return new TransportOrderService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, user), new TestClock(Now), null, user, new PermissionSet());
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var palletUnitId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = palletUnitId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var orders = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now),
            new PricingEngine(db.Context, tenant), user, new PermissionSet());
        var admin = new PricingAdminService(db.Context, tenant, audit);
        return new Harness(db, orders, admin, tenantId, customerId, palletUnitId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city) =>
        new(type, null, null, null, null, city, "BE", null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, decimal quantity = 8) => new(
        customerId, "REF-1", new DateOnly(2026, 8, 12), "Pallets", quantity, null, null, null, null, false, false,
        null, null,
        [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt")],
        QuantityUnitCode: "EUROPALLET");

    private static async Task<TransportOrder> StoredOrderAsync(Harness h, Guid orderId) =>
        await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == orderId);

    // --- §5: typed coverage + stale ----------------------------------------------------------

    [Fact]
    public async Task Calculation_WritesTheTypedCoverage_AndClearsStale()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);

        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots
            .SingleAsync(s => s.TransportOrderId == created.Order!.Id);
        Assert.Equal("Full", snapshot.CoverageStatus);
        Assert.False(snapshot.IsStale);
    }

    [Fact]
    public async Task InputChangeWithoutRecalculation_MarksTheSnapshotStale()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var order = created.Order!;

        // The engine-less service (legacy deployment shape) cannot recalculate: a quantity
        // change must flag the frozen numbers instead of silently keeping them credible.
        var update = new UpdateTransportOrderRequest(
            order.CustomerId, order.CustomerReference, order.OrderDate, order.GoodsDescription, 12,
            order.QuantityUnit, order.WeightKg, order.VolumeM3, order.PalletCount, order.AdrRequired, order.CraneRequired,
            order.AgreedPrice, order.Notes,
            order.Stops.Select(s => new TransportOrderStopInput(
                    s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
                .ToList(),
            QuantityUnitCode: order.QuantityUnitCode);
        var updated = await h.EnginelessOrders().UpdateAsync(order.Id, update, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots
            .SingleAsync(s => s.TransportOrderId == order.Id);
        Assert.True(snapshot.IsStale);

        // An explicit recalculation (engine-wired save) clears the flag again.
        var recalculated = await h.Orders.UpdateAsync(order.Id, update, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, recalculated.Outcome);
        Assert.False((await h.Db.Context.TransportOrderPricingSnapshots
            .SingleAsync(s => s.TransportOrderId == order.Id)).IsStale);
    }

    // --- §6: the readiness projection --------------------------------------------------------

    [Fact]
    public async Task Evaluator_NonCompletedOrder_IsNotReady()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);

        var stored = await StoredOrderAsync(h, created.Order!.Id);
        Assert.Equal("NotReady", stored.InvoiceReadiness);
        Assert.Null(stored.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task Evaluator_CompletedWithFullCoverage_IsReadyForInvoice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var stored = await StoredOrderAsync(h, created.Order!.Id);
        stored.Status = TransportOrderStatus.Completed;

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, stored, CancellationToken.None);

        Assert.Equal("ReadyForInvoice", stored.InvoiceReadiness);
        Assert.Null(stored.InvoiceReadinessReasons);
    }

    /// <summary>Scenario 13: a priced order with an unpriced component reviews, never silently invoices.</summary>
    [Fact]
    public async Task Evaluator_CompletedWithIncompleteCoverage_RequiresReview()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // No rules at all: the engine reports "no tariff" coverage → None.
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var stored = await StoredOrderAsync(h, created.Order!.Id);
        stored.Status = TransportOrderStatus.Completed;

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, stored, CancellationToken.None);

        Assert.Equal("ReviewRequired", stored.InvoiceReadiness);
        Assert.Contains("pricing.coverage.none", stored.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task Evaluator_StaleSnapshot_RequiresReview()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var snapshot = await h.Db.Context.TransportOrderPricingSnapshots
            .SingleAsync(s => s.TransportOrderId == created.Order!.Id);
        snapshot.IsStale = true;
        var stored = await StoredOrderAsync(h, created.Order!.Id);
        stored.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, stored, CancellationToken.None);

        Assert.Equal("ReviewRequired", stored.InvoiceReadiness);
        Assert.Contains("pricing.stale", stored.InvoiceReadinessReasons);
    }

    [Fact]
    public async Task Evaluator_TripExecuted_RequiresAPodPerUnloadingStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var stored = await StoredOrderAsync(h, created.Order!.Id);
        stored.Status = TransportOrderStatus.Completed;

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, stored, CancellationToken.None, tripExecutedOverride: true);
        Assert.Equal("ReviewRequired", stored.InvoiceReadiness);
        Assert.Contains("pod.missing", stored.InvoiceReadinessReasons);

        // A current POD for the unloading stop flips it to ready.
        var unloadingStop = await h.Db.Context.TransportOrderStops
            .SingleAsync(s => s.TransportOrderId == stored.Id && s.StopType == StopType.Unloading);
        var trip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0001",
            TripDate = new DateOnly(2026, 8, 12), Status = TripStatus.Completed,
        };
        h.Db.Context.Trips.Add(trip);
        h.Db.Context.ProofsOfDelivery.Add(new ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id,
            TransportOrderId = stored.Id, TransportOrderStopId = unloadingStop.Id,
            IsCurrent = true, RecipientName = "Magazijnier", DeliveredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await InvoiceReadinessEvaluator.EvaluateAsync(h.Db.Context, stored, CancellationToken.None, tripExecutedOverride: true);
        Assert.Equal("ReadyForInvoice", stored.InvoiceReadiness);
    }

    [Fact]
    public async Task StatusChange_ToCompleted_RunsTheEvaluatorAutomatically()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            h.CustomerId, h.PalletUnitId, PriceRuleBasis.PerUnit, null,
            "Pallets", new DateOnly(2026, 1, 1), null, true, 30m, null, null), CancellationToken.None);
        var created = await h.Orders.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var stored = await StoredOrderAsync(h, created.Order!.Id);
        stored.Status = TransportOrderStatus.InProgress;
        await h.Db.Context.SaveChangesAsync();

        var completed = await h.Orders.ChangeStatusAsync(
            created.Order!.Id, TransportOrderStatus.Completed, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, completed.Outcome);
        Assert.Equal("ReadyForInvoice", (await StoredOrderAsync(h, created.Order!.Id)).InvoiceReadiness);
    }
}
