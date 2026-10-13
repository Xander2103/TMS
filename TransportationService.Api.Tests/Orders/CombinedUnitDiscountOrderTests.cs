using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tarification.Dtos;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tarification.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Phase 8 fix round 1 (spec §29-31): order-level integration coverage for combined-unit
/// degression that the phase review flagged as missing on top of <c>CombinedUnitDiscountTests</c>
/// (which exercises the engine directly). Covers three gaps: (1) TransportOrderService's own
/// <c>BuildPricingGroupsAsync</c> group-building wiring — one group per unloading stop, identical
/// addresses sharing an AddressKey without being pre-merged, unmanaged units excluded, unlinked
/// cargo falling into the "order" group; (2) a full order save producing persisted, per-address
/// discount lines with exact euro amounts and an AgreedPrice that reflects them; (3) a discount
/// linked to a DERIVED agreement's own id firing only when that specific agreement — not its
/// base — is the one actually engaged for the order's customer.
/// </summary>
public class CombinedUnitDiscountOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 7, 27);

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PricingAdminService Admin,
        Guid TenantId, Guid CustomerId, Guid CustomerId2, Guid EuroId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var customerId2 = Guid.NewGuid();
        var euroId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant X", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId2, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Klant Y", IsActive = true });
        db.Context.UnitTypes.Add(new UnitType { Id = euroId, TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var currentUser = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, currentUser);
        var admin = new PricingAdminService(db.Context, tenant, audit);
        var engine = new PricingEngine(db.Context, tenant);
        var sut = new TransportOrderService(db.Context, tenant, audit, new TestClock(Now), engine, currentUser, permissionService: null);
        return new Harness(db, sut, admin, tenantId, customerId, customerId2, euroId);
    }

    private static TransportOrderStopInput Stop(StopType type, string city, string? postalCode = null, string? address = null) =>
        new(type, null, null, address, postalCode, city, "BE", null, null, null, null);

    private static Task<CombinedUnitDiscountDto> CreateDiscountAsync(
        Harness h, string name, IReadOnlyList<SaveCombinedUnitDiscountUnitRequest> units,
        IReadOnlyList<SaveCombinedUnitDiscountTierRequest> tiers,
        DegressionScope scope = DegressionScope.DeliveryAddress, Guid? customerId = null, Guid? agreementId = null) =>
        h.Admin.CreateCombinedDiscountAsync(new SaveCombinedUnitDiscountRequest(
            customerId, agreementId, name, scope, Today.AddYears(-1), null, true, units, tiers), CancellationToken.None);

    // --- 1. BuildPricingGroupsAsync / order -> groups wiring -----------------------------------

    [Fact]
    public async Task BuildPricingGroups_OnePerUnloadingStop_SameAddressKey_ExcludesUnmanagedUnits_NullStopFallsIntoOrderGroup()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = new CreateTransportOrderRequest(
            h.CustomerId, "REF-1", Today, "Gemengde lading", null, null, null, null, null, false, false,
            500m, null,
            [
                Stop(StopType.Loading, "Antwerpen"),
                Stop(StopType.Unloading, "Hasselt", "3500", "Kanaalstraat 1"),
                Stop(StopType.Unloading, "Hasselt", "3500", "Kanaalstraat 1"), // identical address, distinct stop
            ],
            CargoItems:
            [
                new CargoItemInput("Pallets stop 1", null, 2, null, null, UnloadingStopIndex: 1, QuantityUnitCode: "EUROPALLET"),
                new CargoItemInput("Pallets stop 2", null, 3, null, null, UnloadingStopIndex: 2, QuantityUnitCode: "EUROPALLET"),
                new CargoItemInput("Onbeheerde eenheid", null, 5, null, null, UnloadingStopIndex: 1, QuantityUnitCode: "ONBEHEERD"),
                new CargoItemInput("Geen losstop toegewezen", null, 1, null, null, QuantityUnitCode: "EUROPALLET"),
            ]);

        var created = await h.Sut.CreateAsync(request, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);

        var order = await h.Db.Context.TransportOrders.Include(o => o.Stops).SingleAsync(o => o.Id == created.Order!.Id);
        var cargoItems = await h.Db.Context.CargoItems.Where(c => c.TransportOrderId == order.Id).ToListAsync();

        var method = typeof(TransportOrderService).GetMethod("BuildPricingGroupsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<IReadOnlyList<PriceCalculationGroup>>)method.Invoke(h.Sut, [order, cargoItems, CancellationToken.None])!;
        var groups = await task;

        // (a) one group per unloading stop, plus the "order" fallback group -> 3 groups total.
        Assert.Equal(3, groups.Count);
        var unloadingStopIds = order.Stops
            .Where(s => s.StopType == StopType.Unloading)
            .OrderBy(s => s.Sequence)
            .Select(s => s.Id.ToString())
            .ToList();
        Assert.Equal(2, unloadingStopIds.Count);
        var group1 = Assert.Single(groups, g => g.GroupKey == unloadingStopIds[0]);
        var group2 = Assert.Single(groups, g => g.GroupKey == unloadingStopIds[1]);
        var orderGroup = Assert.Single(groups, g => g.GroupKey == "order");

        // (c) the unmanaged-unit cargo line ("ONBEHEERD" has no matching UnitType) never
        // contributes a unit to any group: stop1 only carries its EUROPALLET quantity.
        var stop1Unit = Assert.Single(group1.Units);
        Assert.Equal(h.EuroId, stop1Unit.UnitTypeId);
        Assert.Equal(2m, stop1Unit.Quantity);
        var stop2Unit = Assert.Single(group2.Units);
        Assert.Equal(h.EuroId, stop2Unit.UnitTypeId);
        Assert.Equal(3m, stop2Unit.Quantity);

        // (d) the cargo line with no UnloadingStopId falls into the "order" group.
        Assert.Equal("Order", orderGroup.GroupLabel);
        Assert.Null(orderGroup.AddressKey);
        var orderUnit = Assert.Single(orderGroup.Units);
        Assert.Equal(h.EuroId, orderUnit.UnitTypeId);
        Assert.Equal(1m, orderUnit.Quantity);

        // (b) identical address (same street/postal/city, no LocationId) -> same AddressKey on
        // both unloading-stop groups; BuildPricingGroupsAsync itself never merges them (that is the
        // engine's/MergeGroups' job, exercised right below via the two scopes).
        Assert.NotNull(group1.AddressKey);
        Assert.Equal(group1.AddressKey, group2.AddressKey);

        var underStopScope = CombinedUnitDiscountMath.MergeGroups(groups, DegressionScope.Stop);
        Assert.Equal(3, underStopScope.Count); // Stop scope never merges, even on a shared AddressKey.

        var underAddressScope = CombinedUnitDiscountMath.MergeGroups(groups, DegressionScope.DeliveryAddress);
        Assert.Equal(2, underAddressScope.Count); // stop1 + stop2 merge; the null-AddressKey order group stays on its own.
        var merged = Assert.Single(underAddressScope, g => g.Key == group1.AddressKey);
        Assert.Equal(5m, merged.Quantities[h.EuroId]);
    }

    // --- 2. End-to-end combined discount through a real order save -----------------------------

    [Fact]
    public async Task RealOrderSave_DeliveryAddressScope_PersistsPerAddressDiscountLines_WithExactAmounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var agreement = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Standaard", Today.AddYears(-1), null, true, null, null, null), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.EuroId, PriceRuleBasis.PerUnit, null, "Europallet", Today.AddYears(-1), null, true, 50m, null, null,
            AgreementId: agreement.Id), CancellationToken.None);
        await CreateDiscountAsync(h, "Combikorting",
            [new SaveCombinedUnitDiscountUnitRequest(h.EuroId, 1m)],
            [new SaveCombinedUnitDiscountTierRequest(1, 2, 5), new SaveCombinedUnitDiscountTierRequest(3, 4, 8)],
            scope: DegressionScope.DeliveryAddress, customerId: h.CustomerId);

        var request = new CreateTransportOrderRequest(
            h.CustomerId, "REF-2", Today, "Pallets", 5, null, null, null, null, false, false,
            null, null,
            [
                Stop(StopType.Loading, "Antwerpen"),
                Stop(StopType.Unloading, "Gent", "9000", "Havenlaan 1"),
                Stop(StopType.Unloading, "Brugge", "8000", "Vaartstraat 2"),
            ],
            CargoItems:
            [
                new CargoItemInput("Pallets Gent", null, 2, null, null, UnloadingStopIndex: 1, QuantityUnitCode: "EUROPALLET"),
                new CargoItemInput("Pallets Brugge", null, 3, null, null, UnloadingStopIndex: 2, QuantityUnitCode: "EUROPALLET"),
            ],
            QuantityUnitCode: "EUROPALLET");

        var created = await h.Sut.CreateAsync(request, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        // Base: 5 x 50 = 250. Gent share 2/5 x 250 = 100 -> tier 1-2 -> -5% = -5.00.
        // Brugge share 3/5 x 250 = 150 -> tier 3-4 -> -8% = -12.00. Two different addresses never merge.
        var gentLine = Assert.Single(created.Order!.PricingLines!, l => l.Label.Contains("Gent"));
        var bruggeLine = Assert.Single(created.Order.PricingLines!, l => l.Label.Contains("Brugge"));
        Assert.Equal(-5.00m, gentLine.Amount);
        Assert.Equal(-12.00m, bruggeLine.Amount);
        Assert.Equal(233.00m, created.Order.AgreedPrice);
        Assert.Equal(233.00m, created.Order.CalculatedPrice);

        // The lines are genuinely persisted, not just present on the in-memory result DTO.
        var persisted = await h.Db.Context.TransportOrderPricingLines
            .Where(l => l.TransportOrderId == created.Order.Id)
            .ToListAsync();
        Assert.Contains(persisted, l => l.Label.Contains("Gent") && l.Amount == -5.00m);
        Assert.Contains(persisted, l => l.Label.Contains("Brugge") && l.Amount == -12.00m);
    }

    // --- 3. Derived-agreement-linked discount ----------------------------------------------------

    [Fact]
    public async Task DerivedAgreementLinkedDiscount_FiresOnlyWhenTheDerivedAgreementItselfIsEngaged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var basis = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            null, "Basistabel", Today.AddYears(-1), null, true, null, null, null, IsShared: true), CancellationToken.None);
        await h.Admin.CreateRuleAsync(new SavePriceRuleRequest(
            null, h.EuroId, PriceRuleBasis.PerUnit, null, "Europallet", Today.AddYears(-1), null, true, 40m, null, null,
            AgreementId: basis.Id), CancellationToken.None);
        // A private derived table for customer 1: no rules of its own, reuses the basis's rule via
        // BaseAgreementId, and (unlike a shared derived table) applies automatically — no assignment.
        var derived = await h.Admin.CreateAgreementAsync(new SavePricingAgreementRequest(
            h.CustomerId, "Afgeleide tabel", Today.AddYears(-1), null, true, null, null, null,
            BaseAgreementId: basis.Id), CancellationToken.None);

        // Customer 2 engages the BASE table directly (never through the derived table).
        await h.Admin.SaveAssignmentsAsync(basis.Id,
            [new SavePricingAssignmentRequest(h.CustomerId2, null, null, null, null, null)], CancellationToken.None);

        await CreateDiscountAsync(h, "Combikorting",
            [new SaveCombinedUnitDiscountUnitRequest(h.EuroId, 1m)],
            [new SaveCombinedUnitDiscountTierRequest(1, null, 10)],
            scope: DegressionScope.Order, agreementId: derived.Id);

        CreateTransportOrderRequest Request(Guid customerId) => new(
            customerId, "REF-3", Today, "Pallets", 1, null, null, null, null, false, false,
            null, null,
            [Stop(StopType.Loading, "Antwerpen"), Stop(StopType.Unloading, "Hasselt", "3500")],
            QuantityUnitCode: "EUROPALLET");

        var viaDerived = await h.Sut.CreateAsync(Request(h.CustomerId), CancellationToken.None);
        var viaBaseDirectly = await h.Sut.CreateAsync(Request(h.CustomerId2), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, viaDerived.Outcome);
        Assert.Equal(TransportOrderOperationOutcome.Success, viaBaseDirectly.Outcome);

        Assert.Contains(viaDerived.Order!.PricingLines!, l => l.Label.StartsWith("Combikorting"));
        Assert.Equal(36m, viaDerived.Order.AgreedPrice); // 40 - 10%

        // Customer 2 never engaged the derived table (only the base one) -> the discount, linked
        // to the derived table's own id, must never fire for this order.
        Assert.DoesNotContain(viaBaseDirectly.Order!.PricingLines!, l => l.Label.StartsWith("Combikorting"));
        Assert.Equal(40m, viaBaseDirectly.Order.AgreedPrice);
    }
}
