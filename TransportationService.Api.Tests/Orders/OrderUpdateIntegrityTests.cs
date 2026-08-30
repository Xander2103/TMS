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
        Assert.Single(result.Order.Stops, s => !existingIds.Contains(s.Id));
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
        var added = Assert.Single(result.Order!.Stops, s => !ownIds.Contains(s.Id));
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

    /// <summary>
    /// Review I-4: a refused stop plan must leave the tracked entity COMPLETELY untouched. The
    /// stop guards used to run after ~20 header fields had already been assigned, so a 400 left a
    /// fully-applied, never-validated header edit in the change tracker — which any later
    /// <c>IAuditService.RecordAsync</c> in the same DI scope would have flushed (it calls
    /// SaveChangesAsync unconditionally). The whole stop plan is now validated before the first
    /// mutation.
    /// </summary>
    [Fact]
    public async Task Update_RefusedStopRemoval_LeavesHeaderVersionAndStopsUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var before = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        var stopsBefore = await h.Db.Context.TransportOrderStops.AsNoTracking()
            .Where(s => s.TransportOrderId == order.Id).OrderBy(s => s.Sequence)
            .Select(s => new { s.Id, s.Sequence, s.StopType, s.City, s.Instructions }).ToListAsync();
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var update = UpdateFrom(reloaded!);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            // Every one of these header edits must be discarded along with the refusal.
            CustomerReference = "GEWIJZIGD",
            Notes = "Deze notitie mag nooit landen",
            GoodsDescription = "Iets heel anders",
            AdrRequired = true,
            Stops = [Stop(StopType.Loading, city: "Luik"), update.Stops.Single(s => s.StopType == StopType.Unloading)],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);

        // Force a flush of anything the refused call might have left in the tracker: if the header
        // had been mutated before the guard, this would persist it.
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var after = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.CustomerReference, after.CustomerReference);
        Assert.Equal(before.Notes, after.Notes);
        Assert.Equal(before.GoodsDescription, after.GoodsDescription);
        Assert.Equal(before.AdrRequired, after.AdrRequired);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);

        var stopsAfter = await h.Db.Context.TransportOrderStops.AsNoTracking()
            .Where(s => s.TransportOrderId == order.Id).OrderBy(s => s.Sequence)
            .Select(s => new { s.Id, s.Sequence, s.StopType, s.City, s.Instructions }).ToListAsync();
        Assert.Equal(stopsBefore, stopsAfter);
    }

    /// <summary>
    /// Review I-3: the Draft/Submitted carve-out is the only branch in this change that writes to
    /// another module's rows, so it gets its own test. Package pins are best-effort links (the
    /// scan pipeline documents a null pin as the fallback), so on a not-yet-confirmed order the
    /// stop may go — but the pin must be RELEASED, never left dangling on a soft-deleted row.
    /// The Confirmed counterpart is refused; see
    /// <see cref="Update_RemovingAStopWithPackagesOnAConfirmedOrder_IsRefused"/>.
    /// </summary>
    [Fact]
    public async Task Update_DraftOrder_RemovingAPinnedStop_ReleasesThePackagePins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        Assert.Equal(TransportOrderStatus.Draft, order.Status);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var unloadingStopId = order.Stops.Single(s => s.StopType == StopType.Unloading).Id;
        var pinnedBefore = await h.Db.Context.Packages.AsNoTracking()
            .Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.NotEmpty(pinnedBefore);
        Assert.All(pinnedBefore, p => Assert.Equal(unloadingStopId, p.DeliveryStopId));
        h.Db.Context.ChangeTracker.Clear();

        // Drop the pinned unloading stop and offer a new one. A Draft order is not physically
        // bound, so this is allowed.
        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = [update.Stops.Single(s => s.StopType == StopType.Loading), Stop(StopType.Unloading, city: "Brugge")],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);

        h.Db.Context.ChangeTracker.Clear();
        var droppedStop = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == unloadingStopId);
        Assert.True(droppedStop.IsDeleted);

        var packagesAfter = await h.Db.Context.Packages.AsNoTracking()
            .Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.NotEmpty(packagesAfter);
        // The pin to the removed stop is released; the pin to the PRESERVED loading stop stays.
        Assert.All(packagesAfter, p => Assert.Null(p.DeliveryStopId));
        Assert.All(packagesAfter, p => Assert.Equal(
            result.Order!.Stops.Single(s => s.StopType == StopType.Loading).Id, p.LoadingStopId));
    }

    /// <summary>
    /// Review M-3: reordering is where in-place updates are most likely to surprise (transient
    /// duplicate sequences, cargo stop indexes re-resolved by position). Ids must survive and the
    /// sequences must follow the new request order.
    /// </summary>
    [Fact]
    public async Task Update_ReorderingEchoedStops_KeepsIdsAndRenumbersSequences()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"),
            Stop(StopType.Unloading, city: "Brugge")), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var order = created.Order!;
        var gentId = order.Stops.Single(s => s.City == "Gent").Id;
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;

        // Swap the two unloading stops.
        var update = UpdateFrom(order);
        var reordered = new[] { update.Stops[0], update.Stops[2], update.Stops[1] };
        var result = await h.Sut.UpdateAsync(order.Id, update with { Stops = reordered }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal([1, 2, 3], result.Order!.Stops.Select(s => s.Sequence).ToArray());
        Assert.Equal(bruggeId, result.Order.Stops.Single(s => s.Sequence == 2).Id);
        Assert.Equal(gentId, result.Order.Stops.Single(s => s.Sequence == 3).Id);

        h.Db.Context.ChangeTracker.Clear();
        var persisted = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TransportOrderId == order.Id).ToListAsync();
        Assert.Equal(3, persisted.Count); // no phantom rows, no soft-deleted twins
        Assert.All(persisted, s => Assert.False(s.IsDeleted));
        Assert.Equal(2, persisted.Single(s => s.Id == bruggeId).Sequence);
        Assert.Equal(3, persisted.Single(s => s.Id == gentId).Sequence);
    }

    /// <summary>
    /// Review M-4 (and the coordinator's ruling): the same stop id echoed twice in one request is
    /// ambiguous — the client cannot mean one row twice. Rather than silently inventing a second
    /// stop, the whole edit is refused so nobody's stop identity is guessed at.
    /// </summary>
    [Fact]
    public async Task Update_WithADuplicateEchoedStopId_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var loadingStopId = order.Stops.Single(s => s.StopType == StopType.Loading).Id;

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = [.. update.Stops, Stop(StopType.Unloading, city: "Brugge") with { Id = loadingStopId }],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("meermaals", result.Error!);

        h.Db.Context.ChangeTracker.Clear();
        var stops = await h.Db.Context.TransportOrderStops.AsNoTracking()
            .Where(s => s.TransportOrderId == order.Id).ToListAsync();
        Assert.Equal(2, stops.Count);
    }

    /// <summary>
    /// Review M-2: the "foreign id" rule must hold across TENANTS, not just across orders. The
    /// match set comes from the tenant-scoped order navigation, so another tenant's stop id can
    /// never resolve — proven here against a real second tenant rather than a sibling order.
    /// </summary>
    [Fact]
    public async Task Update_WithAStopIdOfAnotherTenant_TreatsItAsNew_AndNeverTouchesThatTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var otherTenantId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var otherStopId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant
        {
            Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime,
        });
        var otherCustomerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer
        {
            Id = otherCustomerId, TenantId = otherTenantId, CustomerNumber = "KL-X", Name = "Vreemde NV", IsActive = true,
        });
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = otherOrderId, TenantId = otherTenantId, CustomerId = otherCustomerId, OrderNumber = "ORD-X",
            OrderDate = new DateOnly(2026, 8, 30), Status = TransportOrderStatus.Confirmed,
            Stops =
            [
                new TransportOrderStop
                {
                    Id = otherStopId, TenantId = otherTenantId, Sequence = 1,
                    StopType = StopType.Loading, City = "Rotterdam",
                },
            ],
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var order = await CreateTwoStopOrderAsync(h);
        var ownIds = order.Stops.Select(s => s.Id).ToHashSet();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = [.. update.Stops, Stop(StopType.Unloading, city: "Brugge") with { Id = otherStopId }],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var added = Assert.Single(result.Order!.Stops, s => !ownIds.Contains(s.Id));
        Assert.NotEqual(otherStopId, added.Id);

        h.Db.Context.ChangeTracker.Clear();
        var foreignStop = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == otherStopId);
        Assert.Equal(otherTenantId, foreignStop.TenantId);
        Assert.Equal(otherOrderId, foreignStop.TransportOrderId);
        Assert.Equal("Rotterdam", foreignStop.City);
        Assert.False(foreignStop.IsDeleted);
    }
}
