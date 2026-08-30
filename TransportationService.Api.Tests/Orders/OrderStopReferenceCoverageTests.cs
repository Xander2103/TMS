using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Eta.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Labels;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.Scanning;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// Second-pass test review I-2 / fix-wave item A14 — <c>LoadStopReferencesAsync</c> unions EIGHT
/// sources, and before this file only two of them (StopExecution and package pins) were exercised:
/// deleting any of the other blocks left the whole suite green, while three of those sources carry
/// a NON-nullable stop FK, so removing such a stop is outright corruption.
///
/// One test per source, each of which fails if its own <c>hard.UnionWith(...)</c> block is deleted,
/// plus the two consequences the C-01 audit named and nothing asserted: the LABEL still resolves
/// its pinned stop with addresses after an edit, and the SCAN tally still counts pre-edit scans.
/// The A3 counterpart — sources that record an error or closed work and deliberately do NOT pin —
/// lives in <see cref="OrderStopPinEvidenceTests"/>; the one error source with a matching "still
/// blocks" case (the execution exception) is repeated here so both directions sit side by side.
/// </summary>
public class OrderStopReferenceCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, PackageGenerationService Packages,
        PackageLabelService Labels, Guid TenantId, Guid CustomerId, Guid LocationId);

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
            PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1, TradingName = "Acme Transport",
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Terminal Links",
            Street = "Noorderlaan", HouseNumber = "10", PostalCode = "2030",
            City = "Antwerpen", CountryCode = "BE", Type = LocationType.Terminal, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, user);
        var events = new PackageEventWriter(db.Context, tenant, user, clock);
        var sut = new TransportOrderService(db.Context, tenant, audit, clock);
        var packages = new PackageGenerationService(db.Context, tenant, audit,
            new PackageBarcodeService(db.Context, tenant, user, clock), events);
        var labels = new PackageLabelService(db.Context, tenant, user, audit, new LabelRenderService(), events, clock);
        return new Harness(db, sut, packages, labels, tenantId, customerId, locationId);
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

    /// <summary>Laden Antwerpen → lossen Gent → lossen Brugge, with one goods line of 2 colli.</summary>
    private static async Task<TransportOrderDetailDto> CreateThreeStopOrderAsync(Harness h)
    {
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"),
            Stop(StopType.Unloading, city: "Brugge")) with
        {
            CargoItems =
            [
                new CargoItemInput("Onderdelen", "BC-1", 2, null, null,
                    LoadingStopIndex: 0, UnloadingStopIndex: 1, QuantityUnitCode: "EUROPALLET"),
            ],
        }, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        return created.Order!;
    }

    private static async Task<Guid> AddTripAsync(Harness h, TripStatus status = TripStatus.InProgress)
    {
        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = $"RIT-{Guid.NewGuid():N}"[..12],
            TripDate = new DateOnly(2026, 8, 30), Status = status,
        });
        await h.Db.Context.SaveChangesAsync();
        return tripId;
    }

    /// <summary>Drops the "Brugge" stop from the request — the act every case below shares.</summary>
    private static async Task<TransportOrderOperationResult> DropBruggeAsync(Harness h, TransportOrderDetailDto order)
    {
        h.Db.Context.ChangeTracker.Clear();
        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var update = UpdateFrom(reloaded!);
        return await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Where(s => s.City != "Brugge").ToList(),
        }, CancellationToken.None);
    }

    private static async Task AssertStopSurvivedAsync(Harness h, Guid stopId, TransportOrderOperationResult result)
    {
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("operationeel in gebruik", result.Error!);
        h.Db.Context.ChangeTracker.Clear();
        var stop = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == stopId);
        Assert.False(stop.IsDeleted);
    }

    // ---------------------------------------------------------- one case per reference source

    [Fact]
    public async Task Update_RemovingAStopWithAProofOfDelivery_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        var tripId = await AddTripAsync(h);
        h.Db.Context.ProofsOfDelivery.Add(new ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = order.Id,
            TransportOrderStopId = bruggeId, RecipientName = "M. Peeters", DeliveredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await AssertStopSurvivedAsync(h, bruggeId, await DropBruggeAsync(h, order));
    }

    [Fact]
    public async Task Update_RemovingAStopWithALiveTripEta_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        var tripId = await AddTripAsync(h);
        h.Db.Context.StopEtas.Add(new StopEta
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = bruggeId,
            CurrentEta = Now.UtcDateTime, Source = EtaSource.Heuristic, Status = EtaStatus.OnTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await AssertStopSurvivedAsync(h, bruggeId, await DropBruggeAsync(h, order));
    }

    [Fact]
    public async Task Update_RemovingAStopWithARealScan_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        var tripId = await AddTripAsync(h);
        h.Db.Context.ScanEvents.Add(new ScanEvent
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = order.Id,
            TransportOrderStopId = bruggeId, ScanType = ScanType.Unload, Result = ScanResult.Expected,
            Quantity = 1m, Barcode = "BC-1", OccurredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await AssertStopSurvivedAsync(h, bruggeId, await DropBruggeAsync(h, order));
    }

    [Fact]
    public async Task Update_RemovingAStopWithACustodyPackageEvent_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var package = await h.Db.Context.Packages.AsNoTracking().FirstAsync(p => p.TransportOrderId == order.Id);
        h.Db.Context.PackageEvents.Add(new PackageEvent
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, PackageId = package.Id, EventType = PackageEventType.UnloadScan,
            TransportOrderId = order.Id, TransportOrderStopId = bruggeId, OccurredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await AssertStopSurvivedAsync(h, bruggeId, await DropBruggeAsync(h, order));
    }

    [Fact]
    public async Task Update_RemovingAStopWithAnIncident_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        h.Db.Context.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, IncidentType = IncidentType.Damage,
            Title = "Schade bij lossen", Description = "Pallet beschadigd", TransportOrderId = order.Id,
            SourceStopId = bruggeId,
        });
        await h.Db.Context.SaveChangesAsync();

        await AssertStopSurvivedAsync(h, bruggeId, await DropBruggeAsync(h, order));
    }

    [Fact]
    public async Task Update_RemovingAStopWithAStopExecution_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        var tripId = await AddTripAsync(h);
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = bruggeId,
            Status = StopExecutionStatus.Arrived, ArrivedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        await AssertStopSurvivedAsync(h, bruggeId, await DropBruggeAsync(h, order));
    }

    /// <summary>
    /// The A3 direction, kept next to its siblings: an execution EXCEPTION is an error report with
    /// a nullable stop link, written by the scanner's rejection path. It records that something
    /// went wrong at a stop, never that the stop belongs to the order — so it must not pin.
    /// </summary>
    [Fact]
    public async Task Update_RemovingAStopWithOnlyAnExecutionException_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        var bruggeId = order.Stops.Single(s => s.City == "Brugge").Id;
        var tripId = await AddTripAsync(h);
        h.Db.Context.ExecutionExceptions.Add(new Modules.Exceptions.Entities.ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = order.Id,
            TransportOrderStopId = bruggeId, Type = Modules.Exceptions.Entities.ExecutionExceptionType.CustomerUnavailable,
            Description = "Klant niet bereikbaar", OccurredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await DropBruggeAsync(h, order);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();
        var stop = await h.Db.Context.TransportOrderStops.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(s => s.Id == bruggeId);
        Assert.True(stop.IsDeleted);
    }

    // ---------------------------------------------------------- the two consequences C-01 names

    /// <summary>
    /// The audit's C-01 symptom in the field: after an edit the label printed a blank sender and
    /// recipient, because the pin resolved to a soft-deleted twin. With stop identity preserved the
    /// freshly rendered label carries the same addresses as before the edit.
    /// </summary>
    [Fact]
    public async Task Update_ConfirmedOrder_KeepsTheLabelSnapshotAddresses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Packages.GenerateForOrderAsync(order.Id, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var packageId = (await h.Db.Context.Packages.AsNoTracking().FirstAsync(p => p.TransportOrderId == order.Id)).Id;

        var before = await RenderSnapshotAsync(h, packageId, reprintReason: null);
        Assert.Equal("Terminal Links", before.SenderName);
        Assert.Equal("Noorderlaan 10", before.SenderStreet);
        Assert.Equal("2030 Antwerpen", before.SenderPostalCodeCity);
        Assert.Equal("Gent", before.RecipientPostalCodeCity);

        h.Db.Context.ChangeTracker.Clear();
        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var update = UpdateFrom(reloaded!);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Select(s => s with { Instructions = "Bel bij aankomst" }).ToList(),
        }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);

        var after = await RenderSnapshotAsync(h, packageId, reprintReason: "Na routewijziging");
        Assert.Equal(before.SenderName, after.SenderName);
        Assert.Equal(before.SenderStreet, after.SenderStreet);
        Assert.Equal(before.SenderPostalCodeCity, after.SenderPostalCodeCity);
        Assert.Equal(before.RecipientPostalCodeCity, after.RecipientPostalCodeCity);
        Assert.Equal(before.DeliveryStopSequence, after.DeliveryStopSequence);
    }

    private static async Task<LabelSnapshot> RenderSnapshotAsync(Harness h, Guid packageId, string? reprintReason)
    {
        var (pdf, error) = await h.Labels.PrintAsync(
            [packageId], LabelFormat.Thermal100x150, reprintReason, CancellationToken.None);
        Assert.Null(error);
        Assert.NotNull(pdf);
        h.Db.Context.ChangeTracker.Clear();
        var label = await h.Db.Context.PackageLabels.AsNoTracking()
            .Where(l => l.PackageId == packageId).OrderByDescending(l => l.Version).FirstAsync();
        return JsonSerializer.Deserialize<LabelSnapshot>(
            label.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>
    /// The other C-01 consequence: a scan tally is derived from ScanEvent rows joined on the stop.
    /// When an edit replaced the stop, the driver's earlier scans dropped out of the count and the
    /// stop read "0 van 2" again. With identity preserved the tally survives the edit untouched.
    /// </summary>
    [Fact]
    public async Task Update_ConfirmedOrder_KeepsThePreEditScansInTheStopTally()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await CreateThreeStopOrderAsync(h);
        await h.Sut.ChangeStatusAsync(order.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        var gentId = order.Stops.Single(s => s.City == "Gent").Id;
        var tripId = await AddTripAsync(h);
        h.Db.Context.TripOrders.Add(new TripOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = order.Id, Sequence = 1,
        });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var scans = ScanServiceTests.CreateService(
            h.Db, new DevTenantContext(h.TenantId), userId: null, new TestClock(Now));
        var scanned = await scans.SubmitAsync(tripId, gentId,
            new SubmitScanRequest(ScanType.Unload, "BC-1", 1m, false, null, "unit-test"),
            restrictToOwnDriver: false, CancellationToken.None);
        Assert.Equal(ScanOutcome.Success, scanned.Outcome);
        h.Db.Context.ChangeTracker.Clear();

        var reloaded = await h.Sut.GetByIdAsync(order.Id, CancellationToken.None);
        var update = UpdateFrom(reloaded!);
        var result = await h.Sut.UpdateAsync(order.Id, update with
        {
            Stops = update.Stops.Select(s => s with { Instructions = "Achteraan lossen" }).ToList(),
        }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        h.Db.Context.ChangeTracker.Clear();

        var summary = await scans.GetStopSummaryAsync(tripId, gentId, restrictToOwnDriver: false, CancellationToken.None);
        Assert.Equal(ScanOutcome.Success, summary.Outcome);
        var line = Assert.Single(summary.Summary!.Items);
        Assert.Equal(1m, line.ScannedQuantity);
    }
}
