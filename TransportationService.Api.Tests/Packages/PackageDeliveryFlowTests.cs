using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Pod.Dtos;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Pod.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.Scanning;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

/// <summary>
/// Wave P6: the delivery side of the package pipeline. Stop-completion gate with override
/// custody, POD package snapshot (frozen, acknowledged, copied verbatim on correction) and
/// the full return chain: disposition → return scan → depot scan → sender, appended forever.
/// </summary>
public class PackageDeliveryFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness : IDisposable
    {
        public required SqliteTestDbContext Db { get; init; }
        public required TripExecutionService Execution { get; init; }
        public required PodService Pods { get; init; }
        public required TripPackageService TripPackages { get; init; }
        public required TransportationService.Api.Modules.Scanning.Services.ScanService Scans { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid TripId { get; init; }
        public required Guid OrderId { get; init; }
        public required Guid LoadStopId { get; init; }
        public required Guid UnloadStopId { get; init; }
        public required Guid PackageId { get; init; }
        public required string StorageRoot { get; init; }

        public void Dispose()
        {
            Db.Dispose();
            try { Directory.Delete(StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var loadStopId = Guid.NewGuid();
        var unloadStopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var packageId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
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
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.AddRange(
            new TransportOrderStop { Id = loadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
            new TransportOrderStop { Id = unloadStopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 2, StopType = StopType.Unloading, City = "Gent" });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            DriverId = driverId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        db.Context.Packages.Add(new Package
        {
            Id = packageId, TenantId = tenantId, TransportOrderId = orderId,
            PackageNumber = "PKG-00001", BarcodeValue = "PKG-00001-AAAA",
            Description = "Doos elektronica", CurrentLifecycleStatus = PackageLifecycleStatus.Labelled,
        });
        db.Context.PackageBarcodes.Add(new PackageBarcode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageId = packageId,
            Value = "PKG-00001-AAAA", Type = PackageBarcodeType.Code128, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var currentUser = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, currentUser);
        var planningSync = new TripPlanningSyncService(db.Context, tenant);
        var tripService = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(db.Context, tenant, currentUser, clock),
            planningSync, CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        var execution = new TripExecutionService(db.Context, tenant, currentUser, audit, tripService, planningSync,
            TripPackageTestFactory.Create(db.Context, tenant, clock), clock);
        var storageRoot = Path.Combine(Path.GetTempPath(), $"pod-tests-{Guid.NewGuid():N}");
        var pods = new PodService(
            db.Context, tenant, currentUser, audit,
            ScanServiceTests.CreateService(db, tenant, userId, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock),
            new TransportationService.Api.Modules.Qualifications.Services.LocalFileStorageService(storageRoot),
            new TransportationService.Api.Modules.Notifications.Services.NotificationService(db.Context, tenant, currentUser, clock),
            new TransportationService.Api.Modules.Messaging.Services.MessageOutboxService(db.Context, tenant, clock),
            clock);

        return new Harness
        {
            Db = db, Execution = execution, Pods = pods,
            TripPackages = TripPackageTestFactory.Create(db.Context, tenant, clock),
            Scans = ScanServiceTests.CreateService(db, tenant, userId, clock),
            TenantId = tenantId, TripId = tripId, OrderId = orderId,
            LoadStopId = loadStopId, UnloadStopId = unloadStopId, PackageId = packageId,
            StorageRoot = storageRoot,
        };
    }

    private static Task<ScanOperationResult> Scan(
        Harness h, Guid stopId, ScanType type, bool refused = false, string? note = null) =>
        h.Scans.SubmitAsync(h.TripId, stopId,
            new SubmitScanRequest(type, "PKG-00001-AAAA", 1, false, null, "test", null, refused, false, note),
            restrictToOwnDriver: false, CancellationToken.None);

    private static FinalizePodRequest Finalize(bool acknowledged) => new(
        "Ontvanger R. Peeters", null, PodOutcome.Complete, false, false, null, null, null, null, acknowledged);

    private static async Task<Package> Reload(Harness h) =>
        await h.Db.Context.Packages.AsNoTracking().FirstAsync(p => p.Id == h.PackageId);

    [Fact]
    public async Task CompleteUnloadingStop_WithUnresolvedPackage_IsBlockedUntilOverride()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopId, ScanType.Load);
        await h.Execution.ArriveAsync(h.TripId, h.UnloadStopId, false, CancellationToken.None);

        var blocked = await h.Execution.CompleteAsync(h.TripId, h.UnloadStopId,
            new CompleteStopRequest(null, null), restrictToOwnDriver: false, allowPackageOverride: false,
            CancellationToken.None);
        Assert.Equal(ExecutionOutcome.ValidationFailed, blocked.Outcome);
        Assert.Contains("PKG-00001", blocked.Error);

        // Permission alone is not enough: the override needs a reason.
        var noReason = await h.Execution.CompleteAsync(h.TripId, h.UnloadStopId,
            new CompleteStopRequest(null, null), restrictToOwnDriver: false, allowPackageOverride: true,
            CancellationToken.None);
        Assert.Equal(ExecutionOutcome.ValidationFailed, noReason.Outcome);

        var overridden = await h.Execution.CompleteAsync(h.TripId, h.UnloadStopId,
            new CompleteStopRequest(null, null, null, "Colli blijft aan boord voor retour"),
            restrictToOwnDriver: false, allowPackageOverride: true, CancellationToken.None);
        Assert.Equal(ExecutionOutcome.Success, overridden.Outcome);

        var custody = await h.Db.Context.PackageEvents.AsNoTracking()
            .Where(e => e.PackageId == h.PackageId && e.EventType == PackageEventType.CompletionOverride)
            .ToListAsync();
        var overrideEvent = Assert.Single(custody);
        Assert.True(overrideEvent.IsOverride);
        Assert.Equal("Colli blijft aan boord voor retour", overrideEvent.Notes);
    }

    [Fact]
    public async Task CompleteLoadingStop_NeverGatesOnPackages()
    {
        using var h = await SeedAsync();
        await h.Execution.ArriveAsync(h.TripId, h.LoadStopId, false, CancellationToken.None);

        var completed = await h.Execution.CompleteAsync(h.TripId, h.LoadStopId,
            new CompleteStopRequest(null, null), restrictToOwnDriver: false, allowPackageOverride: false,
            CancellationToken.None);
        Assert.Equal(ExecutionOutcome.Success, completed.Outcome);
    }

    [Fact]
    public async Task FinalizePod_GatesOnOutcomesAndAcknowledgment_ThenFreezesSnapshot()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopId, ScanType.Load);

        // Still on the vehicle: no proof possible.
        var unresolved = await h.Pods.FinalizeAsync(h.TripId, h.UnloadStopId, Finalize(acknowledged: true), false, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.ValidationFailed, unresolved.Outcome);
        Assert.Contains("PKG-00001", unresolved.Error);

        await Scan(h, h.UnloadStopId, ScanType.Unload);

        // Outcome exists, but the recipient must confirm the package list.
        var notAcknowledged = await h.Pods.FinalizeAsync(h.TripId, h.UnloadStopId, Finalize(acknowledged: false), false, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.ValidationFailed, notAcknowledged.Outcome);

        var finalized = await h.Pods.FinalizeAsync(h.TripId, h.UnloadStopId, Finalize(acknowledged: true), false, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.Success, finalized.Outcome);
        var line = Assert.Single(finalized.Pod!.PackageSummary);
        Assert.Equal("PKG-00001", line.PackageNumber);
        Assert.Equal("Delivered", line.Outcome);
        Assert.True(finalized.Pod.PackagesAcknowledged);

        // Custody records the proof on the package.
        Assert.Equal(1, await h.Db.Context.PackageEvents.CountAsync(
            e => e.PackageId == h.PackageId && e.EventType == PackageEventType.PodFinalized));

        // The snapshot is frozen: later package changes never leak into the proof.
        var package = await h.Db.Context.Packages.FirstAsync(p => p.Id == h.PackageId);
        package.Description = "Hernoemd";
        await h.Db.Context.SaveChangesAsync();
        var reread = await h.Pods.GetByIdAsync(finalized.Pod.Id, CancellationToken.None);
        Assert.Equal("Doos elektronica", Assert.Single(reread!.PackageSummary).Description);
    }

    [Fact]
    public async Task CorrectPod_CopiesPackageSnapshotVerbatim()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopId, ScanType.Load);
        await Scan(h, h.UnloadStopId, ScanType.Unload);
        var v1 = await h.Pods.FinalizeAsync(h.TripId, h.UnloadStopId, Finalize(acknowledged: true), false, CancellationToken.None);

        var corrected = await h.Pods.CorrectAsync(v1.Pod!.Id,
            new CorrectPodRequest("R. Peeters-Janssens", null, PodOutcome.Complete, false, false, null,
                null, null, null, "Naam verkeerd gespeld"),
            CancellationToken.None);

        Assert.Equal(PodOutcomeResult.Success, corrected.Outcome);
        Assert.Equal(2, corrected.Pod!.Version);
        var line = Assert.Single(corrected.Pod.PackageSummary);
        Assert.Equal("PKG-00001", line.PackageNumber);
        Assert.Equal("Delivered", line.Outcome);
        Assert.True(corrected.Pod.PackagesAcknowledged);
    }

    [Fact]
    public async Task ReturnChain_RefusedToSender_AppendsFullCustody()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopId, ScanType.Load);
        var refusedScan = await Scan(h, h.UnloadStopId, ScanType.Unload, refused: true, note: "Klant weigert");
        Assert.Equal("Refused", refusedScan.Feedback!.Package!.Outcome);

        // Dispatch dispositions the refusal to a return.
        var disposition = await h.TripPackages.ResolveIncidentAsync(h.PackageId,
            new ResolvePackageIncidentRequest(PackageIncidentAction.Return, "Retour naar depot"), CancellationToken.None);
        Assert.Equal(TripPackageOutcome.Success, disposition.Outcome);
        Assert.Equal(PackageLifecycleStatus.ReturnPending, (await Reload(h)).CurrentLifecycleStatus);

        // Driver takes it back on the vehicle, depot receives it, sender gets it back.
        var returnScan = await Scan(h, h.UnloadStopId, ScanType.Return);
        Assert.Equal("Success", returnScan.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.ReturnLoaded, (await Reload(h)).CurrentLifecycleStatus);

        var depotScan = await Scan(h, h.LoadStopId, ScanType.Depot);
        Assert.Equal(PackageLifecycleStatus.ReturnedToDepot, (await Reload(h)).CurrentLifecycleStatus);
        Assert.Equal("Success", depotScan.Feedback!.Package!.Outcome);

        var toSender = await h.TripPackages.ResolveIncidentAsync(h.PackageId,
            new ResolvePackageIncidentRequest(PackageIncidentAction.ReturnToSender, "Teruggestuurd"), CancellationToken.None);
        Assert.Equal(TripPackageOutcome.Success, toSender.Outcome);
        Assert.Equal(PackageLifecycleStatus.ReturnedToSender, (await Reload(h)).CurrentLifecycleStatus);

        // The whole journey is appended, nothing rewritten.
        var custody = await h.Db.Context.PackageEvents.AsNoTracking()
            .Where(e => e.PackageId == h.PackageId)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => e.EventType)
            .ToListAsync();
        Assert.Equal(
            new[]
            {
                PackageEventType.LoadScan, PackageEventType.Refused, PackageEventType.DispositionSet,
                PackageEventType.ReturnLoaded, PackageEventType.ReturnedToDepot, PackageEventType.ReturnedToSender,
            },
            custody.ToArray());
    }

    [Fact]
    public async Task RedeliveryChain_RefusedToDeliveredOnSecondAttempt()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopId, ScanType.Load);
        await Scan(h, h.UnloadStopId, ScanType.Unload, refused: true, note: "Niemand aanwezig");

        var redeliver = await h.TripPackages.ResolveIncidentAsync(h.PackageId,
            new ResolvePackageIncidentRequest(PackageIncidentAction.Redeliver, "Nieuwe poging morgen"), CancellationToken.None);
        Assert.Equal(TripPackageOutcome.Success, redeliver.Outcome);
        Assert.Equal(PackageLifecycleStatus.RedeliveryPlanned, (await Reload(h)).CurrentLifecycleStatus);

        // Second attempt: normal load scan picks the package up again, delivery completes.
        var reload = await Scan(h, h.LoadStopId, ScanType.Load);
        Assert.Equal("Success", reload.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.Loaded, (await Reload(h)).CurrentLifecycleStatus);
        Assert.Equal(1, await h.Db.Context.PackageEvents.CountAsync(
            e => e.PackageId == h.PackageId && e.EventType == PackageEventType.RedeliveryLoaded));

        var delivered = await Scan(h, h.UnloadStopId, ScanType.Unload);
        Assert.Equal("Delivered", delivered.Feedback!.Package!.Outcome);
    }

    [Fact]
    public async Task Timeline_ResolvesContext_InAppendOrder_AndIsolatesTenants()
    {
        using var h = await SeedAsync();
        await Scan(h, h.LoadStopId, ScanType.Load);
        await Scan(h, h.UnloadStopId, ScanType.Unload, refused: true, note: "Geweigerd");

        var tenant = new DevTenantContext(h.TenantId);
        var clock = new TestClock(Now);
        var currentUser = new DevCurrentUserContext(null);
        var packageService = new TransportationService.Api.Modules.Packages.Services.PackageService(
            h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, currentUser),
            new TransportationService.Api.Modules.Packages.Services.PackageBarcodeService(h.Db.Context, tenant, currentUser, clock),
            new TransportationService.Api.Modules.Packages.Services.PackageEventWriter(h.Db.Context, tenant, currentUser, clock));

        var timeline = await packageService.GetTimelineAsync(h.PackageId, CancellationToken.None);

        Assert.NotNull(timeline);
        Assert.Equal(new[] { "LoadScan", "Refused" }, timeline!.Select(e => e.EventType).ToArray());
        Assert.All(timeline, e => Assert.Equal("RIT-0001", e.TripNumber));
        Assert.Equal("Jan Jansen", timeline[0].UserName);
        Assert.NotNull(timeline[1].ExceptionId);

        // A foreign tenant sees nothing — not even that the package exists.
        var foreignService = new TransportationService.Api.Modules.Packages.Services.PackageService(
            h.Db.Context, new DevTenantContext(Guid.NewGuid()),
            new AuditService(h.Db.Context, tenant, currentUser),
            new TransportationService.Api.Modules.Packages.Services.PackageBarcodeService(h.Db.Context, tenant, currentUser, clock),
            new TransportationService.Api.Modules.Packages.Services.PackageEventWriter(h.Db.Context, tenant, currentUser, clock));
        Assert.Null(await foreignService.GetTimelineAsync(h.PackageId, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnScan_OnUnshippedPackage_IsBlocked()
    {
        using var h = await SeedAsync();

        var result = await Scan(h, h.LoadStopId, ScanType.Return);

        Assert.Equal("NotScannable", result.Feedback!.Package!.Outcome);
        Assert.Equal(PackageLifecycleStatus.Labelled, (await Reload(h)).CurrentLifecycleStatus);
    }
}
