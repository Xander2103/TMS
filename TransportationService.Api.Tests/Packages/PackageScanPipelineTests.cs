using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Scanning.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.Scanning;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

/// <summary>
/// Wave P4/P5: the package branch of the one scan pipeline. Load/unload outcomes, wrong
/// trip/stop, duplicates, damage, refusal, group expansion with itemized child failures,
/// the missing flow with controlled resolution, idempotent replay and the departure gate.
/// </summary>
public class PackageScanPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness : IDisposable
    {
        public required SqliteTestDbContext Db { get; init; }
        public required ScanService Scans { get; init; }
        public required TripPackageService TripPackages { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid TripId { get; init; }
        public required Guid OrderAId { get; init; }
        public required Guid LoadStopAId { get; init; }
        public required Guid LoadStopA2Id { get; init; }
        public required Guid UnloadStopAId { get; init; }
        public required Guid PackageP1Id { get; init; }
        public required Guid PackageP2Id { get; init; }
        public required Guid GroupParentId { get; init; }
        public required Guid ChildAId { get; init; }
        public required Guid ChildBId { get; init; }
        public required Guid ForeignPackageId { get; init; }
        public required Trip Trip { get; init; }

        public void Dispose() => Db.Dispose();
    }

    private static Package NewPackage(
        Guid id, Guid tenantId, Guid orderId, string number, string barcode,
        Guid? loadingStopId = null, Guid? deliveryStopId = null, Guid? parentId = null,
        bool mandatory = true, PackageLifecycleStatus status = PackageLifecycleStatus.Labelled) => new()
    {
        Id = id, TenantId = tenantId, TransportOrderId = orderId,
        PackageNumber = number, BarcodeValue = barcode, Description = $"Colli {number}",
        LoadingStopId = loadingStopId, DeliveryStopId = deliveryStopId, ParentPackageId = parentId,
        IsMandatory = mandatory, CurrentLifecycleStatus = status,
    };

    private static PackageBarcode NewBarcode(Guid tenantId, Guid packageId, string value, bool active = true) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, PackageId = packageId,
        Value = value, Type = PackageBarcodeType.Code128, IsActive = active,
    };

    private static async Task<Harness> SeedAsync(string departureRule = "AllowWithWarning")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderAId = Guid.NewGuid();
        var orderForeignId = Guid.NewGuid();
        var loadStopAId = Guid.NewGuid();
        var loadStopA2Id = Guid.NewGuid();
        var unloadStopAId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var groupParent = Guid.NewGuid();
        var childA = Guid.NewGuid();
        var childB = Guid.NewGuid();
        var foreignPackage = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, PackageDepartureRule = departureRule });
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
                Id = orderForeignId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0099",
                OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Andere rit",
            });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = loadStopAId, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
            new TransportOrderStop { Id = loadStopA2Id, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 2, StopType = StopType.Loading, City = "Mechelen" },
            new TransportOrderStop { Id = unloadStopAId, TenantId = tenantId, TransportOrderId = orderAId, Sequence = 3, StopType = StopType.Unloading, City = "Gent" });
        var trip = new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            DriverId = driverId, Status = TripStatus.InProgress,
        };
        db.Context.Trips.Add(trip);
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderAId, Sequence = 1 });

        db.Context.Packages.AddRange(
            NewPackage(p1, tenantId, orderAId, "PKG-00001", "PKG-00001-AAAA", loadStopAId, unloadStopAId),
            NewPackage(p2, tenantId, orderAId, "PKG-00002", "PKG-00002-AAAA"),
            NewPackage(groupParent, tenantId, orderAId, "PKG-00003", "PKG-00003-AAAA"),
            NewPackage(childA, tenantId, orderAId, "PKG-00004", "PKG-00004-AAAA", parentId: groupParent),
            NewPackage(childB, tenantId, orderAId, "PKG-00005", "PKG-00005-AAAA", parentId: groupParent),
            NewPackage(foreignPackage, tenantId, orderForeignId, "PKG-00090", "PKG-00090-AAAA"));
        db.Context.PackageBarcodes.AddRange(
            NewBarcode(tenantId, p1, "PKG-00001-AAAA"),
            NewBarcode(tenantId, p2, "PKG-00002-AAAA"),
            NewBarcode(tenantId, groupParent, "PKG-00003-AAAA"),
            NewBarcode(tenantId, childA, "PKG-00004-AAAA"),
            NewBarcode(tenantId, childB, "PKG-00005-AAAA"),
            NewBarcode(tenantId, foreignPackage, "PKG-00090-AAAA"));
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var currentUser = new DevCurrentUserContext(userId);
        var scans = ScanServiceTests.CreateService(db, tenant, userId, clock);
        var tripPackages = new TripPackageService(db.Context, tenant, currentUser,
            new PackageEventWriter(db.Context, tenant, currentUser, clock),
            new AuditService(db.Context, tenant, currentUser), clock);

        return new Harness
        {
            Db = db, Scans = scans, TripPackages = tripPackages, TenantId = tenantId, TripId = tripId,
            OrderAId = orderAId, LoadStopAId = loadStopAId, LoadStopA2Id = loadStopA2Id, UnloadStopAId = unloadStopAId,
            PackageP1Id = p1, PackageP2Id = p2, GroupParentId = groupParent,
            ChildAId = childA, ChildBId = childB, ForeignPackageId = foreignPackage, Trip = trip,
        };
    }

    private static Task<ScanOperationResult> Scan(
        Harness h, Guid stopId, string barcode, ScanType type = ScanType.Load,
        bool damaged = false, string? damageNote = null, Guid? clientEventId = null,
        bool refused = false, bool partial = false, string? note = null) =>
        h.Scans.SubmitAsync(h.TripId, stopId,
            new SubmitScanRequest(type, barcode, 1, damaged, damageNote, "test-scanner",
                clientEventId, refused, partial, note),
            restrictToOwnDriver: true, CancellationToken.None);

    private static async Task<Package> Reload(Harness h, Guid packageId)
    {
        var package = await h.Db.Context.Packages.AsNoTracking()
            .FirstAsync(p => p.Id == packageId);
        return package;
    }

    [Fact]
    public async Task LoadScan_PreTransitPackage_LoadsAndWritesCustody()
    {
        using var h = await SeedAsync();

        var result = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        Assert.Equal(ScanOutcome.Success, result.Outcome);
        var feedback = result.Feedback!;
        Assert.Equal(ScanFeedbackLevel.Success, feedback.Level);
        Assert.NotNull(feedback.Package);
        Assert.Equal("Success", feedback.Package!.Outcome);
        Assert.Equal("Loaded", feedback.Package.LifecycleStatus);

        Assert.Equal(PackageLifecycleStatus.Loaded, (await Reload(h, h.PackageP1Id)).CurrentLifecycleStatus);

        var scanEvent = await h.Db.Context.ScanEvents.AsNoTracking().SingleAsync();
        Assert.Equal(h.PackageP1Id, scanEvent.PackageId);
        Assert.Equal("Success", scanEvent.PackageOutcome);

        var custody = await h.Db.Context.PackageEvents.AsNoTracking()
            .Where(e => e.PackageId == h.PackageP1Id).ToListAsync();
        var loadEvent = Assert.Single(custody);
        Assert.Equal(PackageEventType.LoadScan, loadEvent.EventType);
        Assert.Equal(PackageLifecycleStatus.Labelled, loadEvent.OldStatus);
        Assert.Equal(PackageLifecycleStatus.Loaded, loadEvent.NewStatus);
        Assert.Equal(scanEvent.Id, loadEvent.ScanEventId);
    }

    [Fact]
    public async Task LoadScan_Twice_SecondIsDuplicateWithoutMovement()
    {
        using var h = await SeedAsync();

        await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");
        var second = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        Assert.Equal(ScanOutcome.Success, second.Outcome);
        Assert.Equal(ScanResult.DuplicateScan, second.Feedback!.Result);
        Assert.Equal("AlreadyLoaded", second.Feedback.Package!.Outcome);
        Assert.Equal(ScanFeedbackLevel.Warning, second.Feedback.Level);

        // Both scans are on the ledger; custody moved exactly once.
        Assert.Equal(2, await h.Db.Context.ScanEvents.CountAsync());
        Assert.Equal(1, await h.Db.Context.PackageEvents.CountAsync(e => e.PackageId == h.PackageP1Id));
    }

    [Fact]
    public async Task LoadScan_WrongTripPackage_BlocksAndOpensExceptionOnce()
    {
        using var h = await SeedAsync();

        var first = await Scan(h, h.LoadStopAId, "PKG-00090-AAAA");
        Assert.Equal("WrongTrip", first.Feedback!.Package!.Outcome);
        Assert.Equal(ScanFeedbackLevel.Warning, first.Feedback.Level);
        Assert.Equal(PackageLifecycleStatus.Labelled, (await Reload(h, h.ForeignPackageId)).CurrentLifecycleStatus);

        var exception = await h.Db.Context.ExecutionExceptions.AsNoTracking().SingleAsync();
        Assert.Equal(ExecutionExceptionType.WrongRoutePackage, exception.Type);
        Assert.Equal(h.ForeignPackageId, exception.PackageId);

        // Scanning the same problem again must not flood dispatch.
        await Scan(h, h.LoadStopAId, "PKG-00090-AAAA");
        Assert.Equal(1, await h.Db.Context.ExecutionExceptions.CountAsync());
        Assert.Equal(2, await h.Db.Context.ScanEvents.CountAsync());
    }

    [Fact]
    public async Task LoadScan_PinnedToOtherStop_BlocksWithWrongLoadingStop()
    {
        using var h = await SeedAsync();

        var result = await Scan(h, h.LoadStopA2Id, "PKG-00001-AAAA");

        Assert.Equal("WrongLoadingStop", result.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.Labelled, (await Reload(h, h.PackageP1Id)).CurrentLifecycleStatus);
        var exception = await h.Db.Context.ExecutionExceptions.AsNoTracking().SingleAsync();
        Assert.Equal(ExecutionExceptionType.WrongStopPackage, exception.Type);
    }

    [Fact]
    public async Task LoadScan_Damaged_MovesToDamagedAndOpensException()
    {
        using var h = await SeedAsync();

        var result = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA", damaged: true, damageNote: "Hoek ingedeukt");

        Assert.Equal("DamagedPackage", result.Feedback!.Package!.Outcome);
        Assert.Equal(ScanResult.DamagedItem, result.Feedback.Result);
        var package = await Reload(h, h.PackageP1Id);
        Assert.Equal(PackageLifecycleStatus.Damaged, package.CurrentLifecycleStatus);
        Assert.Equal(PackageExceptionState.Open, package.CurrentExceptionStatus);

        var exception = await h.Db.Context.ExecutionExceptions.AsNoTracking().SingleAsync();
        Assert.Equal(ExecutionExceptionType.DamagedPackage, exception.Type);
        Assert.Contains("Hoek ingedeukt", exception.Description);
    }

    [Fact]
    public async Task UnloadScan_LoadedPackage_DeliversAtPinnedStop()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        var result = await Scan(h, h.UnloadStopAId, "PKG-00001-AAAA", ScanType.Unload);

        Assert.Equal("Delivered", result.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.Delivered, (await Reload(h, h.PackageP1Id)).CurrentLifecycleStatus);

        var duplicate = await Scan(h, h.UnloadStopAId, "PKG-00001-AAAA", ScanType.Unload);
        Assert.Equal("AlreadyDelivered", duplicate.Feedback!.Package!.Outcome);
        Assert.Equal(ScanResult.DuplicateScan, duplicate.Feedback.Result);
    }

    [Fact]
    public async Task UnloadScan_NeverLoadedPackage_BlocksAsNotLoaded()
    {
        using var h = await SeedAsync();

        var result = await Scan(h, h.UnloadStopAId, "PKG-00002-AAAA", ScanType.Unload);

        Assert.Equal("NotLoaded", result.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.Labelled, (await Reload(h, h.PackageP2Id)).CurrentLifecycleStatus);
    }

    [Fact]
    public async Task UnloadScan_Refused_RecordsRefusalWithException()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        var result = await Scan(h, h.UnloadStopAId, "PKG-00001-AAAA", ScanType.Unload,
            refused: true, note: "Klant weigert de levering");

        Assert.Equal("Refused", result.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.Refused, (await Reload(h, h.PackageP1Id)).CurrentLifecycleStatus);
        var exception = await h.Db.Context.ExecutionExceptions.AsNoTracking().SingleAsync();
        Assert.Equal(ExecutionExceptionType.RejectedDelivery, exception.Type);
        Assert.Contains("Klant weigert", exception.Description);
    }

    [Fact]
    public async Task GroupScan_AllChildrenValid_LoadsEveryChildAndParent()
    {
        using var h = await SeedAsync();

        var result = await Scan(h, h.LoadStopAId, "PKG-00003-AAAA");

        Assert.Equal("GroupProcessed", result.Feedback!.Package!.Outcome);
        Assert.Equal(ScanFeedbackLevel.Success, result.Feedback.Level);
        Assert.Equal(2, result.Feedback.Package.Children.Count);
        Assert.All(result.Feedback.Package.Children, c => Assert.True(c.Succeeded));

        Assert.Equal(PackageLifecycleStatus.Loaded, (await Reload(h, h.ChildAId)).CurrentLifecycleStatus);
        Assert.Equal(PackageLifecycleStatus.Loaded, (await Reload(h, h.ChildBId)).CurrentLifecycleStatus);
        Assert.Equal(PackageLifecycleStatus.Loaded, (await Reload(h, h.GroupParentId)).CurrentLifecycleStatus);

        // One physical scan on the ledger; per-child custody in package events.
        Assert.Equal(1, await h.Db.Context.ScanEvents.CountAsync());
        Assert.Equal(3, await h.Db.Context.PackageEvents.CountAsync());
    }

    [Fact]
    public async Task GroupScan_FailedChild_IsItemizedAndParentStays()
    {
        using var h = await SeedAsync();
        var childB = await h.Db.Context.Packages.FirstAsync(p => p.Id == h.ChildBId);
        childB.CurrentLifecycleStatus = PackageLifecycleStatus.Cancelled;
        await h.Db.Context.SaveChangesAsync();

        var result = await Scan(h, h.LoadStopAId, "PKG-00003-AAAA");

        Assert.Equal(ScanFeedbackLevel.Warning, result.Feedback!.Level);
        Assert.Contains("1 van 2", result.Feedback.Message + " " + result.Feedback.Package!.Children.Count);
        var failed = result.Feedback.Package.Children.Single(c => !c.Succeeded);
        Assert.Equal("CancelledPackage", failed.Outcome);
        Assert.Equal(h.ChildBId, failed.PackageId);

        Assert.Equal(PackageLifecycleStatus.Loaded, (await Reload(h, h.ChildAId)).CurrentLifecycleStatus);
        Assert.Equal(PackageLifecycleStatus.Labelled, (await Reload(h, h.GroupParentId)).CurrentLifecycleStatus);
    }

    [Fact]
    public async Task Submit_SameClientEventId_ReplaysWithoutSecondLedgerRow()
    {
        using var h = await SeedAsync();
        var key = Guid.NewGuid();

        var first = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA", clientEventId: key);
        var replay = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA", clientEventId: key);

        Assert.Equal(ScanOutcome.Success, replay.Outcome);
        Assert.True(replay.Feedback!.Replayed);
        Assert.Equal(first.Feedback!.ScanEventId, replay.Feedback.ScanEventId);
        Assert.Equal(1, await h.Db.Context.ScanEvents.CountAsync());
        Assert.Equal(1, await h.Db.Context.PackageEvents.CountAsync(e => e.PackageId == h.PackageP1Id));
    }

    [Fact]
    public async Task RetiredBarcode_StillIdentifiesPackage_ButBlocksMovement()
    {
        using var h = await SeedAsync();
        var barcode = await h.Db.Context.PackageBarcodes.FirstAsync(b => b.Value == "PKG-00001-AAAA");
        barcode.IsActive = false;
        var package = await h.Db.Context.Packages.FirstAsync(p => p.Id == h.PackageP1Id);
        package.BarcodeValue = "PKG-00001-BBBB";
        h.Db.Context.PackageBarcodes.Add(NewBarcode(h.TenantId, h.PackageP1Id, "PKG-00001-BBBB"));
        await h.Db.Context.SaveChangesAsync();

        var result = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        Assert.Equal("ReplacedBarcode", result.Feedback!.Package!.Outcome);
        Assert.Contains("PKG-00001-BBBB", result.Feedback.Message);
        Assert.Equal(PackageLifecycleStatus.Labelled, (await Reload(h, h.PackageP1Id)).CurrentLifecycleStatus);
    }

    [Fact]
    public async Task MissingFlow_MarkResolveFound_ReturnsPackageToLoadable()
    {
        using var h = await SeedAsync();

        var marked = await h.TripPackages.MarkMissingAsync(h.TripId, h.PackageP1Id,
            new MarkPackageMissingRequest(h.LoadStopAId, "Niet gevonden in magazijn"),
            restrictToOwnDriver: false, CancellationToken.None);
        Assert.Equal(TripPackageOutcome.Success, marked.Outcome);

        var package = await Reload(h, h.PackageP1Id);
        Assert.Equal(PackageLifecycleStatus.Missing, package.CurrentLifecycleStatus);
        Assert.Equal(PackageExceptionState.Open, package.CurrentExceptionStatus);
        var exception = await h.Db.Context.ExecutionExceptions.AsNoTracking().SingleAsync();
        Assert.Equal(ExecutionExceptionType.MissingPackage, exception.Type);

        // Scanning a missing package is blocked until the incident is resolved.
        var blockedScan = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");
        Assert.Equal("MissingPackage", blockedScan.Feedback!.Package!.Outcome);

        var resolved = await h.TripPackages.ResolveIncidentAsync(h.PackageP1Id,
            new ResolvePackageIncidentRequest(PackageIncidentAction.Found, "Stond op verkeerde dock"),
            CancellationToken.None);
        Assert.Equal(TripPackageOutcome.Success, resolved.Outcome);

        package = await Reload(h, h.PackageP1Id);
        Assert.Equal(PackageLifecycleStatus.AwaitingLoading, package.CurrentLifecycleStatus);
        Assert.Equal(PackageExceptionState.Resolved, package.CurrentExceptionStatus);
        Assert.Equal(ExecutionExceptionStatus.Resolved,
            (await h.Db.Context.ExecutionExceptions.AsNoTracking().SingleAsync()).Status);

        var loaded = await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");
        Assert.Equal("Success", loaded.Feedback!.Package!.Outcome);

        // The full trail stays: marked missing → resolved → load scan.
        var custody = await h.Db.Context.PackageEvents.AsNoTracking()
            .Where(e => e.PackageId == h.PackageP1Id).OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal(
            new[] { PackageEventType.MarkedMissing, PackageEventType.ExceptionResolved, PackageEventType.LoadScan },
            custody.Select(e => e.EventType).ToArray());
    }

    [Fact]
    public async Task Readiness_CountsMandatoryLeavesOnly_AndFollowsRule()
    {
        using var h = await SeedAsync(departureRule: "RequireOverride");
        await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        var readiness = await h.TripPackages.EvaluateReadinessAsync(h.Trip, CancellationToken.None);

        // P1 loaded; P2 + 2 group children outstanding; the parent is not counted.
        Assert.Equal(4, readiness.TotalPackages);
        Assert.Equal(4, readiness.MandatoryPackages);
        Assert.Equal(1, readiness.LoadedCount);
        Assert.Equal(3, readiness.NotLoadedCount);
        Assert.False(readiness.IsComplete);
        Assert.True(readiness.RequiresOverride);
        Assert.False(readiness.IsBlocked);
        Assert.Equal(PackageDepartureRule.RequireOverride, readiness.Rule);
        Assert.DoesNotContain(readiness.OutstandingPackages, p => p.PackageId == h.GroupParentId);
    }

    [Fact]
    public async Task Readiness_BlockRule_BlocksWithoutOverridePath()
    {
        using var h = await SeedAsync(departureRule: "Block");

        var readiness = await h.TripPackages.EvaluateReadinessAsync(h.Trip, CancellationToken.None);

        Assert.True(readiness.IsBlocked);
        Assert.False(readiness.RequiresOverride);
    }

    [Fact]
    public async Task DepartureOverride_StagesOverrideEventPerOutstandingPackage()
    {
        using var h = await SeedAsync(departureRule: "RequireOverride");
        await Scan(h, h.LoadStopAId, "PKG-00001-AAAA");

        await h.TripPackages.StageDepartureOverrideAsync(h.Trip, "Spoedvertrek goedgekeurd", CancellationToken.None);
        await h.Db.Context.SaveChangesAsync();

        var overrides = await h.Db.Context.PackageEvents.AsNoTracking()
            .Where(e => e.EventType == PackageEventType.DepartureOverride).ToListAsync();
        Assert.Equal(3, overrides.Count);
        Assert.All(overrides, e => Assert.True(e.IsOverride));
        Assert.All(overrides, e => Assert.Equal("Spoedvertrek goedgekeurd", e.Notes));
    }

    [Fact]
    public async Task Checklist_GroupsPackagesByResolvedStop()
    {
        using var h = await SeedAsync();

        var result = await h.TripPackages.GetChecklistAsync(h.TripId, null, restrictToOwnDriver: false, CancellationToken.None);

        Assert.Equal(TripPackageOutcome.Success, result.Outcome);
        var checklist = Assert.IsType<TripPackageChecklistDto>(result.Payload);
        var stopA = checklist.Stops.Single(s => s.StopId == h.LoadStopAId);
        var stopA2 = checklist.Stops.Single(s => s.StopId == h.LoadStopA2Id);
        var unload = checklist.Stops.Single(s => s.StopId == h.UnloadStopAId);

        // P1 pinned to stop A; the rest default to the first loading stop; nothing lands on A2.
        Assert.Contains(stopA.Packages, p => p.PackageId == h.PackageP1Id);
        Assert.Contains(stopA.Packages, p => p.PackageId == h.PackageP2Id);
        Assert.Empty(stopA2.Packages);
        Assert.Equal(5, stopA.Packages.Count);
        Assert.Equal(5, unload.Packages.Count);
        Assert.True(stopA.Packages.Single(p => p.PackageId == h.GroupParentId).IsGroup);
    }

    [Fact]
    public async Task CargoLadder_StaysUntouched_ForNonPackageBarcodes()
    {
        using var h = await SeedAsync();
        h.Db.Context.CargoItems.Add(new CargoItem
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = h.OrderAId,
            Sequence = 1, Description = "Los cargo item", Barcode = "CARGO-1", ExpectedQuantity = 2,
        });
        await h.Db.Context.SaveChangesAsync();

        var known = await Scan(h, h.LoadStopAId, "CARGO-1");
        Assert.Equal(ScanResult.Expected, known.Feedback!.Result);
        Assert.Null(known.Feedback.Package);

        var unknown = await Scan(h, h.LoadStopAId, "TOTAAL-ONBEKEND");
        Assert.Equal(ScanResult.UnexpectedItem, unknown.Feedback!.Result);
        Assert.Null(unknown.Feedback.Package);
    }
}
