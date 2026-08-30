using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Wave 1 production blockers C-02 and C-01.
///
/// C-02: <c>UpdateAsync</c> assigned <c>order.CustomerId</c> and rewrote <c>LegalEntityId</c>
/// straight from the request, bypassing the dedicated change flows (reason, pricing
/// invalidation, dossier/invoice guards, audit). A plain header edit may never move an order to
/// another customer or invoicing entity — that is a server-side invariant, not a UI restriction.
///
/// C-01: stops were wholesale-replaced with fresh ids on every update while the old rows were
/// only SOFT deleted, so every reference pinned to a stop (package pins, stop executions, POD,
/// scans, ETA, exceptions) silently pointed at a hidden row. Stop identity is now: an echoed id
/// belonging to THIS order is the same stop (updated in place), no/unknown id is a new stop, a
/// stop that is not echoed is removed — and removal is refused while the stop is operationally
/// referenced.
/// </summary>
public class OrderUpdateIntegrityTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PackageGenerationService Packages,
        Guid TenantId, Guid CustomerId, Guid OtherCustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
            PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Dok NV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Terminal Links",
            City = "Antwerpen", CountryCode = "BE", Type = LocationType.Terminal, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var sut = new TransportOrderService(db.Context, tenant, new AuditService(db.Context, tenant, user), clock);
        var packages = new PackageGenerationService(db.Context, tenant,
            new AuditService(db.Context, tenant, user),
            new PackageBarcodeService(db.Context, tenant, user, clock),
            new PackageEventWriter(db.Context, tenant, user, clock));
        return new Harness(db, sut, packages, tenantId, customerId, otherCustomerId, locationId);
    }

    private static TransportOrderStopInput Stop(StopType type, Guid? locationId = null, string? city = null) =>
        new(type, locationId, null, null, null, city, locationId is null ? "BE" : null, null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, params TransportOrderStopInput[] stops) => new(
        customerId, "PO-777", new DateOnly(2026, 8, 30), "20 paletten bouwmateriaal",
        20, "paletten", 12500, null, 20, false, false, 1450m, null, stops);

    /// <summary>
    /// Maps the detail DTO back into an update request the way a real client does: echoing every
    /// stop id (identity preserving). Pass <paramref name="echoStopIds"/> = false to mimic a
    /// legacy client that omits them (every stop is then a NEW stop).
    /// </summary>
    private static UpdateTransportOrderRequest UpdateFrom(TransportOrderDetailDto d, bool echoStopIds = true) => new(
        d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
        d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
        d.AgreedPrice, d.Notes,
        d.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions,
                Id: echoStopIds ? s.Id : null))
            .ToList(),
        LegalEntityId: d.LegalEntityId,
        QuantityUnitCode: d.QuantityUnitCode);

    private static async Task<TransportOrderDetailDto> CreateTwoStopOrderAsync(Harness h)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        return created.Order!;
    }

    // ---------------------------------------------------------------- C-02

    [Fact]
    public async Task Update_WithDifferentCustomer_IsRefused_AndLeavesTheOrderUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);

        var result = await h.Sut.UpdateAsync(order.Id,
            UpdateFrom(order) with { CustomerId = h.OtherCustomerId, Notes = "gewijzigd" }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Klant wijzigen", result.Error!);

        h.Db.Context.ChangeTracker.Clear();
        var persisted = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(h.CustomerId, persisted.CustomerId);
        Assert.Null(persisted.Notes); // the whole edit is rejected, not partially applied
    }

    [Fact]
    public async Task Update_WithDifferentLegalEntity_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var entityId = Guid.NewGuid();
        h.Db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = entityId, TenantId = h.TenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var order = await CreateTwoStopOrderAsync(h);

        var result = await h.Sut.UpdateAsync(order.Id,
            UpdateFrom(order) with { LegalEntityId = entityId }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Entiteit wijzigen", result.Error!);

        h.Db.Context.ChangeTracker.Clear();
        var persisted = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Null(persisted.LegalEntityId);
    }

    [Fact]
    public async Task Update_EchoingTheSameCustomerAndEntity_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);

        var result = await h.Sut.UpdateAsync(order.Id,
            UpdateFrom(order) with { Notes = "Spoed" }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal("Spoed", result.Order!.Notes);
        Assert.Equal(h.CustomerId, result.Order.CustomerId);
    }

    // ---------------------------------------------------------------- C-01

    [Fact]
    public async Task Update_EchoingStopIds_PreservesStopIdentity_AndEveryReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var loadingStopId = order.Stops.Single(s => s.StopType == StopType.Loading).Id;
        var unloadingStopId = order.Stops.Single(s => s.StopType == StopType.Unloading).Id;

        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        var generated = await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        Assert.NotNull(generated);
        h.Db.Context.ChangeTracker.Clear();
        var packagesBefore = await h.Db.Context.Packages.AsNoTracking()
            .Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.NotEmpty(packagesBefore);
        Assert.All(packagesBefore, p => Assert.Equal(loadingStopId, p.LoadingStopId));
        Assert.All(packagesBefore, p => Assert.Equal(unloadingStopId, p.DeliveryStopId));

        // A stop execution: the strongest reference (non-nullable FK, records a real-world event).
        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0001",
            TripDate = new DateOnly(2026, 8, 30), Status = TripStatus.InProgress,
        });
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = loadingStopId,
            Status = StopExecutionStatus.Arrived, ArrivedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var update = UpdateFrom(reloaded!);
        update = update with
        {
            Stops = update.Stops.Select(s => s with { Instructions = "Bel bij aankomst" }).ToList(),
        };
        var result = await h.Sut.UpdateAsync(order.Id, update, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(loadingStopId, result.Order!.Stops.Single(s => s.StopType == StopType.Loading).Id);
        Assert.Equal(unloadingStopId, result.Order.Stops.Single(s => s.StopType == StopType.Unloading).Id);
        Assert.All(result.Order.Stops, s => Assert.Equal("Bel bij aankomst", s.Instructions));

        h.Db.Context.ChangeTracker.Clear();
        // Every stop row of this order is still LIVE (no soft-deleted twins left behind).
        var liveStops = await h.Db.Context.TransportOrderStops.AsNoTracking()
            .Where(s => s.TransportOrderId == order.Id).ToListAsync();
        Assert.Equal(2, liveStops.Count);

        // Package pins still resolve through the (soft-delete-filtered) stop set.
        var packagesAfter = await h.Db.Context.Packages.AsNoTracking()
            .Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.All(packagesAfter, p => Assert.Equal(loadingStopId, p.LoadingStopId));
        Assert.All(packagesAfter, p => Assert.Contains(p.DeliveryStopId!.Value, liveStops.Select(s => s.Id)));

        var execution = await h.Db.Context.StopExecutions.AsNoTracking().SingleAsync();
        Assert.Contains(execution.TransportOrderStopId, liveStops.Select(s => s.Id));
    }

    [Fact]
    public async Task Update_AddingAStop_GivesANewIdOnlyToTheAddedStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var existingIds = order.Stops.Select(s => s.Id).ToHashSet();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = [.. update.Stops, Stop(StopType.Unloading, city: "Brugge")],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(3, result.Order!.Stops.Count);
        Assert.Equal(2, result.Order.Stops.Count(s => existingIds.Contains(s.Id)));
        Assert.Single(result.Order.Stops.Where(s => !existingIds.Contains(s.Id)));
        Assert.Equal([1, 2, 3], result.Order.Stops.Select(s => s.Sequence).ToArray());
    }

    [Fact]
    public async Task Update_RemovingAnUnreferencedStop_SoftDeletesOnlyThatStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"),
            Stop(StopType.Unloading, city: "Brugge")), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var order = created.Order!;
        var droppedId = order.Stops.Single(s => s.City == "Brugge").Id;
        var keptIds = order.Stops.Where(s => s.Id != droppedId).Select(s => s.Id).ToList();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Where(s => s.City != "Brugge").ToList(),
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(keptIds, result.Order!.Stops.Select(s => s.Id).ToList());

        h.Db.Context.ChangeTracker.Clear();
        var dropped = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == droppedId);
        Assert.True(dropped.IsDeleted);
    }

    [Fact]
    public async Task Update_RemovingAStopWithPackagesOnAConfirmedOrder_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var update = UpdateFrom(reloaded!);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            // Drop the loading stop the packages are pinned to and offer a brand-new one instead.
            Stops = [Stop(StopType.Loading, city: "Luik"), update.Stops.Single(s => s.StopType == StopType.Unloading)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("verwijderd", result.Error!);

        h.Db.Context.ChangeTracker.Clear();
        var stops = await h.Db.Context.TransportOrderStops.AsNoTracking()
            .Where(s => s.TransportOrderId == order.Id).ToListAsync();
        Assert.Equal(2, stops.Count);
        Assert.Contains(stops, s => s.StopType == StopType.Loading && s.City == "Antwerpen");
    }

    [Fact]
    public async Task Update_RemovingAStopWithAStopExecution_IsRefused_EvenInDraft()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"),
            Stop(StopType.Unloading, city: "Brugge")), CancellationToken.None);
        var order = created.Order!;
        var droppedId = order.Stops.Single(s => s.City == "Brugge").Id;

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0002",
            TripDate = new DateOnly(2026, 8, 30), Status = TripStatus.InProgress,
        });
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = droppedId,
            Status = StopExecutionStatus.Arrived, ArrivedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Where(s => s.City != "Brugge").ToList(),
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        var stop = await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.Id == droppedId);
        Assert.False(stop.IsDeleted);
    }

    [Fact]
    public async Task Update_ChangingTheTypeOfAReferencedStop_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Three stops so the retype still leaves a loading AND an unloading stop — the refusal
        // must come from the stop's references, not from the confirmation rule.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"),
            Stop(StopType.Unloading, city: "Brugge")), CancellationToken.None);
        var order = created.Order!;
        var referencedId = order.Stops.Single(s => s.City == "Brugge").Id;

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0003",
            TripDate = new DateOnly(2026, 8, 30), Status = TripStatus.InProgress,
        });
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = referencedId,
            Status = StopExecutionStatus.Arrived, ArrivedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops
                .Select(s => s.Id == referencedId ? s with { StopType = StopType.Loading } : s)
                .ToList(),
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("type", result.Error!, StringComparison.OrdinalIgnoreCase);

        h.Db.Context.ChangeTracker.Clear();
        var stop = await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.Id == referencedId);
        Assert.Equal(StopType.Unloading, stop.StopType);
    }

    [Fact]
    public async Task Update_WithAStopIdOfAnotherOrder_TreatsItAsNew_AndNeverAdoptsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignOrder = await CreateTwoStopOrderAsync(h);
        var foreignStopId = foreignOrder.Stops.First().Id;
        var order = await CreateTwoStopOrderAsync(h);
        var ownIds = order.Stops.Select(s => s.Id).ToHashSet();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = [.. update.Stops, Stop(StopType.Unloading, city: "Brugge") with { Id = foreignStopId }],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var added = Assert.Single(result.Order!.Stops.Where(s => !ownIds.Contains(s.Id)));
        Assert.NotEqual(foreignStopId, added.Id);

        h.Db.Context.ChangeTracker.Clear();
        // The other order kept its own stop, untouched.
        var foreignStop = await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.Id == foreignStopId);
        Assert.Equal(foreignOrder.Id, foreignStop.TransportOrderId);
        Assert.NotEqual("Brugge", foreignStop.City);
    }

    /// <summary>
    /// Legacy clients that do NOT echo stop ids still get the old wholesale replacement — and
    /// the cargo re-link that goes with it (preserved cargo may never keep pointing at a
    /// soft-deleted stop).
    /// </summary>
    [Fact]
    public async Task Update_WithoutStopIds_ReplacesStops_AndRelinksPreservedCargo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var oldIds = order.Stops.Select(s => s.Id).ToHashSet();
        var cargoId = order.CargoItems.Single().Id;

        var result = await h.Sut.UpdateAsync(order.Id,
            UpdateFrom(order, echoStopIds: false) with { CargoItems = null }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.All(result.Order!.Stops, s => Assert.DoesNotContain(s.Id, oldIds));

        h.Db.Context.ChangeTracker.Clear();
        var cargo = await h.Db.Context.CargoItems.AsNoTracking().SingleAsync(c => c.Id == cargoId);
        Assert.Equal(result.Order.Stops.Single(s => s.StopType == StopType.Loading).Id, cargo.LoadingStopId);
        Assert.Equal(result.Order.Stops.Single(s => s.StopType == StopType.Unloading).Id, cargo.UnloadingStopId);
    }

    /// <summary>Preserved stops must NOT have their cargo links churned: the relink is a no-op.</summary>
    [Fact]
    public async Task Update_EchoingStopIds_LeavesPreservedCargoLinksUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var cargo = order.CargoItems.Single();

        var result = await h.Sut.UpdateAsync(order.Id,
            UpdateFrom(order) with { CargoItems = null }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var preserved = Assert.Single(result.Order!.CargoItems);
        Assert.Equal(cargo.LoadingStopId, preserved.LoadingStopId);
        Assert.Equal(cargo.UnloadingStopId, preserved.UnloadingStopId);
    }
}
