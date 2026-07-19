using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Scanning.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Scanning;

/// <summary>
/// Wave 2: deterministic scan classification, corrections with mandatory reason, tenant/trip
/// isolation and the expected-vs-scanned summary. The server is the source of truth; no scan
/// is ever silently dropped.
/// </summary>
public class ScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, ScanService Sut, TestClock Clock, Guid TenantId, Guid TripId,
        Guid OrderAId, Guid LoadStopAId, Guid UnloadStopAId, Guid ItemA1Id, Guid ItemA2Id,
        Guid OrderBId, Guid ItemB1Id, Guid DriverUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderAId = Guid.NewGuid();
        var orderBId = Guid.NewGuid();
        var loadStopAId = Guid.NewGuid();
        var unloadStopAId = Guid.NewGuid();
        var loadStopBId = Guid.NewGuid();
        var itemA1Id = Guid.NewGuid();
        var itemA2Id = Guid.NewGuid();
        var itemB1Id = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "chauffeur@acme.be", PasswordHash = "x",
            FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.AddRange(
            new TransportOrder
            {
                Id = orderAId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
                OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten A",
            },
            new TransportOrder
            {
                Id = orderBId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0002",
                OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten B",
            });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = loadStopAId, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
            new TransportOrderStop { Id = unloadStopAId, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" },
            new TransportOrderStop { Id = loadStopBId, TenantId = tenantId, TransportOrderId = orderBId, Sequence = 1, StopType = StopType.Loading, City = "Luik" });
        db.Context.CargoItems.AddRange(
            new CargoItem { Id = itemA1Id, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 1, Description = "Pallet bouwstenen", Barcode = "BC-A1", ExpectedQuantity = 10 },
            new CargoItem { Id = itemA2Id, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 2, Description = "Pallet cement", Barcode = "BC-A2", ExpectedQuantity = 5 },
            new CargoItem { Id = itemB1Id, TenantId = tenantId, TransportOrderId = orderBId, Sequence = 1, Description = "Buizen", Barcode = "BC-B1", ExpectedQuantity = 3 });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            DriverId = driverId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.AddRange(
            new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderAId, Sequence = 1 },
            new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderBId, Sequence = 2 });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var sut = CreateService(db, tenant, userId, clock);
        return new Harness(db, sut, clock, tenantId, tripId,
            orderAId, loadStopAId, unloadStopAId, itemA1Id, itemA2Id, orderBId, itemB1Id, userId);
    }

    /// <summary>Builds the full pipeline (package branch included) for any user context.</summary>
    internal static ScanService CreateService(
        SqliteTestDbContext db, DevTenantContext tenant, Guid? userId, TestClock clock)
    {
        var currentUser = new DevCurrentUserContext(userId);
        var eventWriter = new TransportationService.Api.Modules.Packages.Services.PackageEventWriter(
            db.Context, tenant, currentUser, clock);
        return new ScanService(db.Context, tenant, currentUser,
            new AuditService(db.Context, tenant, currentUser),
            new TransportationService.Api.Modules.Packages.Services.PackageBarcodeService(
                db.Context, tenant, currentUser, clock),
            new TransportationService.Api.Modules.Packages.Services.PackageScanProcessor(
                db.Context, tenant, currentUser, eventWriter, clock),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(db.Context, tenant, currentUser, clock),
            clock);
    }

    private static Task<ScanOperationResult> Scan(
        Harness h, Guid stopId, string barcode, decimal quantity = 1, bool damaged = false, string? damageNote = null) =>
        h.Sut.SubmitAsync(h.TripId, stopId,
            new SubmitScanRequest(ScanType.Load, barcode, quantity, damaged, damageNote, "unit-test-device"),
            restrictToOwnDriver: true, CancellationToken.None);

    [Fact]
    public async Task Submit_ExpectedItem_AccumulatesTowardsComplete()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var first = await Scan(h, h.LoadStopAId, "BC-A1", 4);
        Assert.Equal(ScanOutcome.Success, first.Outcome);
        Assert.Equal(ScanResult.Expected, first.Feedback!.Result);
        Assert.Equal(4, first.Feedback.AcceptedQuantity);

        var second = await Scan(h, h.LoadStopAId, "BC-A1", 6);
        Assert.Equal(ScanResult.Expected, second.Feedback!.Result);
        Assert.Equal(10, second.Feedback.AcceptedQuantity);

        var item = second.Feedback.Summary.Items.Single(i => i.CargoItemId == h.ItemA1Id);
        Assert.Equal(CargoScanState.Complete, item.State);
        Assert.Equal(10, item.ScannedQuantity);
    }

    [Fact]
    public async Task Submit_UnknownBarcode_IsRecordedAsUnexpected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await Scan(h, h.LoadStopAId, "ONBEKEND-123");

        Assert.Equal(ScanOutcome.Success, result.Outcome);
        Assert.Equal(ScanResult.UnexpectedItem, result.Feedback!.Result);
        Assert.Null(result.Feedback.CargoItemId);
        Assert.Equal(1, result.Feedback.Summary.UnexpectedScanCount);

        var history = await h.Sut.ListAsync(h.TripId, null, true, CancellationToken.None);
        Assert.Contains(history.Events!, e => e.Barcode == "ONBEKEND-123" && e.Result == ScanResult.UnexpectedItem);
    }

    [Fact]
    public async Task Submit_ItemOfOtherOrderOnTrip_IsWrongItem_AndDoesNotCount()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await Scan(h, h.LoadStopAId, "BC-B1");

        Assert.Equal(ScanResult.WrongItem, result.Feedback!.Result);
        // The wrong-side item is recorded but tallies nothing at this stop.
        Assert.DoesNotContain(result.Feedback.Summary.Items, i => i.CargoItemId == h.ItemB1Id);
        Assert.All(result.Feedback.Summary.Items, i => Assert.Equal(0, i.ScannedQuantity));
    }

    [Fact]
    public async Task Submit_BeyondExpected_FlagsOverDeliveryThenDuplicate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Scan(h, h.LoadStopAId, "BC-A1", 8);
        var over = await Scan(h, h.LoadStopAId, "BC-A1", 5);
        Assert.Equal(ScanResult.OverDelivery, over.Feedback!.Result);
        Assert.Equal(13, over.Feedback.AcceptedQuantity);
        Assert.Equal(CargoScanState.Over, over.Feedback.Summary.Items.Single(i => i.CargoItemId == h.ItemA1Id).State);

        var duplicate = await Scan(h, h.LoadStopAId, "BC-A1", 1);
        Assert.Equal(ScanResult.DuplicateScan, duplicate.Feedback!.Result);
        // A duplicate is recorded but never silently inflates the tally.
        Assert.Equal(13, duplicate.Feedback.AcceptedQuantity);
    }

    [Fact]
    public async Task Submit_Damaged_KeepsNoteAndCountsQuantity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await Scan(h, h.LoadStopAId, "BC-A2", 2, damaged: true, damageNote: "Hoek beschadigd");

        Assert.Equal(ScanResult.DamagedItem, result.Feedback!.Result);
        var item = result.Feedback.Summary.Items.Single(i => i.CargoItemId == h.ItemA2Id);
        Assert.Equal(2, item.ScannedQuantity);
        Assert.Equal(2, item.DamagedQuantity);
        Assert.Equal(CargoScanState.Partial, item.State);

        var history = await h.Sut.ListAsync(h.TripId, h.LoadStopAId, true, CancellationToken.None);
        Assert.Contains(history.Events!, e => e.Damaged && e.DamageNote == "Hoek beschadigd");
    }

    [Fact]
    public async Task Correct_SetsAbsoluteQuantity_WithMandatoryReason_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Scan(h, h.LoadStopAId, "BC-A1", 8);
        await Scan(h, h.LoadStopAId, "BC-A1", 5);

        var noReason = await h.Sut.CorrectAsync(h.TripId, h.LoadStopAId,
            new ScanCorrectionRequest(h.ItemA1Id, ScanType.Load, 10, " "), CancellationToken.None);
        Assert.Equal(ScanOutcome.ValidationFailed, noReason.Outcome);

        var corrected = await h.Sut.CorrectAsync(h.TripId, h.LoadStopAId,
            new ScanCorrectionRequest(h.ItemA1Id, ScanType.Load, 10, "Dubbel gescand door scannerstoring"), CancellationToken.None);

        Assert.Equal(ScanOutcome.Success, corrected.Outcome);
        Assert.Equal(ScanResult.ManualCorrection, corrected.Feedback!.Result);
        Assert.Equal(10, corrected.Feedback.AcceptedQuantity);
        Assert.Equal(CargoScanState.Complete, corrected.Feedback.Summary.Items.Single(i => i.CargoItemId == h.ItemA1Id).State);

        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "ScanEvent" && a.Action == "ManualCorrection");

        var history = await h.Sut.ListAsync(h.TripId, h.LoadStopAId, true, CancellationToken.None);
        Assert.Contains(history.Events!, e => e.Result == ScanResult.ManualCorrection && e.CorrectionReason == "Dubbel gescand door scannerstoring");
    }

    [Fact]
    public async Task Submit_Guards_TripStatus_StopMembership_Tenancy_AndOwnership()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Stop of a foreign order not on this trip.
        var foreignStop = await Scan(h, Guid.NewGuid(), "BC-A1");
        Assert.Equal(ScanOutcome.NotFound, foreignStop.Outcome);

        // Foreign tenant sees nothing.
        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var foreign = CreateService(h.Db, otherTenant, null, new TestClock(Now));
        var crossTenant = await foreign.SubmitAsync(h.TripId, h.LoadStopAId,
            new SubmitScanRequest(ScanType.Load, "BC-A1", 1, false, null, null), false, CancellationToken.None);
        Assert.Equal(ScanOutcome.NotFound, crossTenant.Outcome);

        // Another driver of the same tenant is not allowed when restricted.
        var otherUser = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = otherUser, TenantId = h.TenantId, Email = "ander@acme.be", PasswordHash = "x",
            FirstName = "Piet", LastName = "Peters", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(h.TenantId);
        var otherDriverSut = CreateService(h.Db, tenant, otherUser, new TestClock(Now));
        var notYours = await otherDriverSut.SubmitAsync(h.TripId, h.LoadStopAId,
            new SubmitScanRequest(ScanType.Load, "BC-A1", 1, false, null, null), true, CancellationToken.None);
        Assert.Equal(ScanOutcome.NotYourTrip, notYours.Outcome);

        // Loading is allowed while the trip is still Planned (warehouse pre-loads), but a
        // Draft trip is not loadable and unloading needs the trip underway.
        var trip = await h.Db.Context.Trips.FindAsync(h.TripId);
        trip!.Status = TripStatus.Planned;
        await h.Db.Context.SaveChangesAsync();
        var plannedLoad = await Scan(h, h.LoadStopAId, "BC-A1");
        Assert.Equal(ScanOutcome.Success, plannedLoad.Outcome);
        var plannedUnload = await h.Sut.SubmitAsync(h.TripId, h.UnloadStopAId,
            new SubmitScanRequest(ScanType.Unload, "BC-A1", 1, false, null, null), true, CancellationToken.None);
        Assert.Equal(ScanOutcome.InvalidState, plannedUnload.Outcome);

        trip.Status = TripStatus.Draft;
        await h.Db.Context.SaveChangesAsync();
        var draftLoad = await Scan(h, h.LoadStopAId, "BC-A1");
        Assert.Equal(ScanOutcome.InvalidState, draftLoad.Outcome);
    }

    [Fact]
    public async Task Submit_OnTerminalStop_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.StopExecutions.Add(new StopExecution
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = h.TripId,
            TransportOrderStopId = h.LoadStopAId, Status = StopExecutionStatus.Completed,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await Scan(h, h.LoadStopAId, "BC-A1");

        Assert.Equal(ScanOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Summary_ShowsMissingPartialAndQuantityInvariants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Scan(h, h.LoadStopAId, "BC-A2", 2);
        var summary = await h.Sut.GetStopSummaryAsync(h.TripId, h.LoadStopAId, true, CancellationToken.None);

        Assert.Equal(ScanOutcome.Success, summary.Outcome);
        var a1 = summary.Summary!.Items.Single(i => i.CargoItemId == h.ItemA1Id);
        var a2 = summary.Summary.Items.Single(i => i.CargoItemId == h.ItemA2Id);
        Assert.Equal(CargoScanState.Missing, a1.State);
        Assert.Equal(CargoScanState.Partial, a2.State);
        Assert.Equal(2, a2.ScannedQuantity);
        Assert.Equal(5, a2.ExpectedQuantity);
    }

    [Fact]
    public async Task History_IsNewestFirst_WithUserNames()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Scan(h, h.LoadStopAId, "BC-A1", 1);
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await Scan(h, h.LoadStopAId, "BC-A2", 1);

        var history = await h.Sut.ListAsync(h.TripId, null, true, CancellationToken.None);

        Assert.Equal(ScanOutcome.Success, history.Outcome);
        Assert.Equal(2, history.Events!.Count);
        Assert.Equal("BC-A2", history.Events[0].Barcode);
        Assert.All(history.Events, e => Assert.Equal("Jan Jansen", e.UserName));
    }

    [Fact]
    public async Task Submit_QuantityMustBePositive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var zero = await Scan(h, h.LoadStopAId, "BC-A1", 0);
        Assert.Equal(ScanOutcome.ValidationFailed, zero.Outcome);

        var blankBarcode = await Scan(h, h.LoadStopAId, "  ");
        Assert.Equal(ScanOutcome.ValidationFailed, blankBarcode.Outcome);
    }
}
