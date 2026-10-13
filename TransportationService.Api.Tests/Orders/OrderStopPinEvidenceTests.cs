using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Wave 1 fix wave A — the EVIDENCE rules around stop identity.
///
/// A2: releasing a package pin used to be keyed on <c>Status == Confirmed</c>, but a corrected
/// order (Confirmed → Draft) can carry printed labels while a Confirmed order can carry colli
/// nobody has touched. The rule is now evidence-based and status-blind: a pin may be released or
/// re-pinned only while every live collo on it carries nothing but its generation event.
///
/// A3: the HARD-reference set used to include rows produced by ERRORS (execution exceptions,
/// wrong-scan events) and by CLOSED work (cancelled packages, ETAs of cancelled trips), so a
/// single mis-scan pinned a stop forever, in every editable status.
///
/// A1(b): a goods line whose stop link moves while its colli are already in circulation is
/// refused; while they are not, the colli follow the line.
///
/// A4: a stop-only or cargo-only edit now bumps the order's concurrency token, so the dossier
/// drawers' 409 rebase can see a colleague's concurrent route change.
/// </summary>
public class OrderStopPinEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PackageGenerationService Packages,
        Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
            PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
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
        return new Harness(db, sut, packages, tenantId, customerId, locationId);
    }

    private static TransportOrderStopInput Stop(StopType type, Guid? locationId = null, string? city = null) =>
        new(type, locationId, null, null, null, city, locationId is null ? "BE" : null, null, null, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, params TransportOrderStopInput[] stops) => new(
        customerId, "PO-777", new DateOnly(2026, 8, 30), "20 paletten bouwmateriaal",
        20, "paletten", 12500, null, 20, false, false, 1450m, null, stops);

    private static UpdateTransportOrderRequest UpdateFrom(TransportOrderDetailDto d) => new(
        d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
        d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
        d.AgreedPrice, d.Notes,
        d.Stops.Select(s => new TransportOrderStopInput(
                s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions, Id: s.Id))
            .ToList(),
        LegalEntityId: d.LegalEntityId,
        QuantityUnitCode: d.QuantityUnitCode);

    /// <summary>Two stops (Antwerpen laden → Gent lossen) and one goods line of 2 colli.</summary>
    private static async Task<TransportOrderDetailDto> CreateTwoStopOrderAsync(Harness h)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        }, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        return created.Order!;
    }

    /// <summary>Three stops (laden → lossen Gent → lossen Brugge) and one goods line on Gent.</summary>
    private static async Task<TransportOrderDetailDto> CreateThreeStopOrderAsync(Harness h)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"),
            Stop(StopType.Unloading, city: "Brugge")) with
        {
            CargoItems =
            [
                new CargoItemInput("Onderdelen", null, 2, null, null,
                    LoadingStopIndex: 0, UnloadingStopIndex: 1, QuantityUnitCode: "EUROPALLET"),
            ],
        }, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        return created.Order!;
    }

    /// <summary>Stages one post-generation custody event on every live collo of the order.</summary>
    private static async Task StampEventAsync(Harness h, Guid orderId, PackageEventType type, Guid? stopId = null)
    {
        var packages = await h.Db.Context.Packages.AsNoTracking()
            .Where(p => p.TransportOrderId == orderId).ToListAsync();
        foreach (var package in packages)
        {
            h.Db.Context.PackageEvents.Add(new PackageEvent
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = package.Id, EventType = type,
                TransportOrderId = orderId, TransportOrderStopId = stopId, OccurredAt = Now.UtcDateTime,
            });
        }

        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();
    }

    private static UpdateTransportOrderRequest DropUnloadingStop(UpdateTransportOrderRequest update) => update with
    {
        Stops = [update.Stops.Single(s => s.StopType == StopType.Loading), Stop(StopType.Unloading, city: "Brugge")],
    };

    // ------------------------------------------------------------------ A2 (evidence, not status)

    [Fact]
    public async Task Update_DraftOrder_RemovingAStopPinnedByALabelledPackage_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        Assert.Equal(TransportOrderStatus.Draft, order.Status);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        // The labels are printed and stuck on the pallets: the pin is now physical evidence,
        // whatever the order's status says.
        await StampEventAsync(h, order.Id, PackageEventType.Labelled);

        var unloadingStopId = order.Stops.Single(s => s.StopType == StopType.Unloading).Id;
        var result = await h.Sut.UpdateAsync(order.Id, DropUnloadingStop(UpdateFrom(order)), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("verwijderd", result.Error!);

        h.Db.Context.ChangeTracker.Clear();
        var stop = await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.Id == unloadingStopId);
        Assert.False(stop.IsDeleted);
        var packages = await h.Db.Context.Packages.AsNoTracking().Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.All(packages, p => Assert.Equal(unloadingStopId, p.DeliveryStopId));
    }

    [Fact]
    public async Task Update_ConfirmedOrder_RemovingAStopPinnedByUntouchedPackages_ReleasesThePins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var unloadingStopId = order.Stops.Single(s => s.StopType == StopType.Unloading).Id;
        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var result = await h.Sut.UpdateAsync(order.Id, DropUnloadingStop(UpdateFrom(reloaded!)), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);

        h.Db.Context.ChangeTracker.Clear();
        var dropped = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == unloadingStopId);
        Assert.True(dropped.IsDeleted);
        var packages = await h.Db.Context.Packages.AsNoTracking().Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.NotEmpty(packages);
        Assert.All(packages, p => Assert.Null(p.DeliveryStopId));
    }

    // ------------------------------------------------------------------ A3 (error/closed evidence)

    [Fact]
    public async Task Update_RemovingAStopWithOnlyAWrongPackageScan_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;

        // A collo of ANOTHER order is scanned at Brugge: the scanner records the rejection as an
        // ExecutionException plus a WrongPackageScan event, both pinned to that stop. Neither
        // proves Brugge belongs to this order's route.
        var otherOrder = await CreateTwoStopOrderAsync(h);
        await h.Packages.GenerateForOrderAsync(otherOrder.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var strayPackage = await h.Db.Context.Packages.AsNoTracking()
            .FirstAsync(p => p.TransportOrderId == otherOrder.Id);

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0100",
            TripDate = new DateOnly(2026, 8, 30), Status = TripStatus.InProgress,
        });
        h.Db.Context.ExecutionExceptions.Add(new Modules.Exceptions.Entities.ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = order.Id,
            TransportOrderStopId = bruggeId, Type = Modules.Exceptions.Entities.ExecutionExceptionType.WrongStopPackage,
            Description = "Collo hoort bij een andere stop", OccurredAt = Now.UtcDateTime,
        });
        h.Db.Context.PackageEvents.Add(new PackageEvent
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = strayPackage.Id,
            EventType = PackageEventType.WrongPackageScan, TransportOrderId = order.Id,
            TransportOrderStopId = bruggeId, OccurredAt = Now.UtcDateTime,
        });
        h.Db.Context.ScanEvents.Add(new ScanEvent
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = order.Id,
            TransportOrderStopId = bruggeId, ScanType = ScanType.Unload, Result = ScanResult.WrongItem,
            Quantity = 1m, OccurredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Where(s => s.City != "Brugge").ToList(),
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        var dropped = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == bruggeId);
        Assert.True(dropped.IsDeleted);
    }

    [Fact]
    public async Task Update_RemovingAStopWithACompletedExecution_IsStillRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0101",
            TripDate = new DateOnly(2026, 8, 30), Status = TripStatus.Completed,
        });
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = bruggeId,
            Status = StopExecutionStatus.Completed, ArrivedAt = Now.UtcDateTime, DepartedAt = Now.UtcDateTime,
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
        var stop = await h.Db.Context.TransportOrderStops.AsNoTracking().SingleAsync(s => s.Id == bruggeId);
        Assert.False(stop.IsDeleted);
    }

    [Fact]
    public async Task Update_RemovingAStopPinnedOnlyByCancelledPackages_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var unloadingStopId = order.Stops.Single(s => s.StopType == StopType.Unloading).Id;
        var packages = await h.Db.Context.Packages.Where(p => p.TransportOrderId == order.Id).ToListAsync();
        foreach (var package in packages)
        {
            // Cancelled colli are closed work: labelled or not, they can no longer pin a stop.
            package.CurrentLifecycleStatus = PackageLifecycleStatus.Cancelled;
            h.Db.Context.PackageEvents.Add(new PackageEvent
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = package.Id,
                EventType = PackageEventType.Labelled, OccurredAt = Now.UtcDateTime,
            });
        }

        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var result = await h.Sut.UpdateAsync(order.Id, DropUnloadingStop(UpdateFrom(reloaded!)), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        var dropped = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == unloadingStopId);
        Assert.True(dropped.IsDeleted);
    }

    [Fact]
    public async Task Update_RemovingAStopWithAnEtaOfACancelledTrip_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;

        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0102",
            TripDate = new DateOnly(2026, 8, 30), Status = TripStatus.Cancelled,
        });
        h.Db.Context.StopEtas.Add(new StopEta
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = bruggeId,
            CurrentEta = Now.UtcDateTime, Source = EtaSource.Heuristic, Status = EtaStatus.OnTime,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Where(s => s.City != "Brugge").ToList(),
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        var dropped = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == bruggeId);
        Assert.True(dropped.IsDeleted);
    }

    // ------------------------------------------------------------------ A1(b) cargo → stop link

    private static UpdateTransportOrderRequest MoveGoodsLineToBrugge(
        UpdateTransportOrderRequest update, TransportOrderDetailDto order) => update with
        {
            CargoItems =
            [
                new CargoItemInput("Onderdelen", null, 2, null, null,
                    LoadingStopIndex: 0, UnloadingStopIndex: 2, QuantityUnitCode: "EUROPALLET",
                    Id: order.CargoItems.Single().Id),
            ],
        };

    [Fact]
    public async Task Update_MovingAGoodsLineToAnotherStop_WithScannedPackages_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        await StampEventAsync(h, order.Id, PackageEventType.LoadScan);

        var gentId = order.Stops.Single(s => s.City == "Gent").Id;
        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var result = await h.Sut.UpdateAsync(order.Id,
            MoveGoodsLineToBrugge(UpdateFrom(reloaded!), reloaded!), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Goederenlijn 1", result.Error!);
        Assert.Contains("stop 2", result.Error!);

        h.Db.Context.ChangeTracker.Clear();
        var cargo = await h.Db.Context.CargoItems.AsNoTracking().SingleAsync(c => c.TransportOrderId == order.Id);
        Assert.Equal(gentId, cargo.UnloadingStopId);
        var packages = await h.Db.Context.Packages.AsNoTracking().Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.All(packages, p => Assert.Equal(gentId, p.DeliveryStopId));
    }

    [Fact]
    public async Task Update_MovingAGoodsLineToAnotherStop_RepinsUntouchedPackages()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var result = await h.Sut.UpdateAsync(order.Id,
            MoveGoodsLineToBrugge(UpdateFrom(reloaded!), reloaded!), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);

        h.Db.Context.ChangeTracker.Clear();
        var cargo = await h.Db.Context.CargoItems.AsNoTracking().SingleAsync(c => c.TransportOrderId == order.Id);
        Assert.Equal(bruggeId, cargo.UnloadingStopId);
        var packages = await h.Db.Context.Packages.AsNoTracking().Where(p => p.TransportOrderId == order.Id).ToListAsync();
        Assert.NotEmpty(packages);
        // The colli follow the line, so the scan pipeline never sees a package pinned to a stop
        // its own goods line no longer serves.
        Assert.All(packages, p => Assert.Equal(bruggeId, p.DeliveryStopId));
    }

    // ------------------------------------------------------------------ A4 concurrency token

    [Fact]
    public async Task Update_ChangingOnlyAStop_BumpsTheOrderVersion()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var versionBefore = order.Version;

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            // Nothing on the header changes — only the stop's instructions.
            Stops = update.Stops.Select(s => s with { Instructions = "Bel bij aankomst" }).ToList(),
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.NotEqual(versionBefore, result.Order!.Version);

        h.Db.Context.ChangeTracker.Clear();
        var persisted = await h.Db.Context.TransportOrders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(result.Order.Version, persisted.Version);

        // And the stale token is now rejected, which is what the drawers' rebase banner needs.
        var stale = await h.Sut.UpdateAsync(order.Id, update with { Version = versionBefore }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.VersionConflict, stale.Outcome);
    }

    [Fact]
    public async Task Update_ChangingOnlyAGoodsLine_BumpsTheOrderVersion()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateTwoStopOrderAsync(h);
        var versionBefore = order.Version;

        var update = UpdateFrom(order);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            // Only a line-level note: nothing here feeds the derived header summary, so without
            // the explicit bump the order row would stay Unchanged and keep its old token.
            CargoItems =
            [
                new CargoItemInput("Onderdelen", null, 2, null, "Stapelen tot 2 hoog",
                    QuantityUnitCode: "EUROPALLET", Id: order.CargoItems.Single().Id),
            ],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.NotEqual(versionBefore, result.Order!.Version);
    }
}
