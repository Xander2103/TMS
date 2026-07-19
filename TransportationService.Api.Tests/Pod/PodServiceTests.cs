using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Pod.Dtos;
using TransportationService.Api.Modules.Pod.Entities;
using TransportationService.Api.Modules.Pod.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Scanning.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Pod;

/// <summary>
/// Wave 4: immutable proof of delivery with a scan-summary snapshot, versioned corrections
/// (never destructive) and additive photo evidence.
/// </summary>
public class PodServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    // 1x1 transparent PNG.
    private const string SignatureDataUrl =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private sealed record Harness(
        SqliteTestDbContext Db, PodService Sut, string StorageRoot, Guid TenantId,
        Guid TripId, Guid OrderId, Guid StopId, Guid CargoItemId, Guid DriverUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var cargoItemId = Guid.NewGuid();
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
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "Paletten",
        });
        db.Context.TransportOrderStops.Add(new TransportOrderStop
        {
            Id = stopId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1,
            StopType = StopType.Unloading, City = "Gent",
        });
        db.Context.CargoItems.Add(new CargoItem
        {
            Id = cargoItemId, TenantId = tenantId, TransportOrderId = orderId, Sequence = 1,
            Description = "Pallet cement", Barcode = "BC-1", ExpectedQuantity = 5,
        });
        db.Context.Trips.Add(new Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-0001", TripDate = new(2026, 7, 21),
            DriverId = driverId, Status = TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderId, Sequence = 1 });
        await db.Context.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-pod-tests", Guid.NewGuid().ToString("N"));
        return new Harness(db, CreateSut(db, tenantId, userId, storageRoot), storageRoot,
            tenantId, tripId, orderId, stopId, cargoItemId, userId);
    }

    private static PodService CreateSut(SqliteTestDbContext db, Guid tenantId, Guid userId, string storageRoot)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        return new PodService(
            db.Context, tenant, user, audit,
            new ScanService(db.Context, tenant, user, audit, clock),
            new LocalFileStorageService(storageRoot),
            clock);
    }

    private static FinalizePodRequest Finalize(
        string recipient = "Magazijnier P. Peeters", PodOutcome outcome = PodOutcome.Complete,
        string? signature = null, bool damage = false, bool missing = false) =>
        new(recipient, "Magazijnier", outcome, damage, missing, "Alles in orde", signature, null, null);

    [Fact]
    public async Task Finalize_CreatesImmutableVersion1_WithScanSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Two scanned units become part of the frozen proof.
        var scanService = new ScanService(h.Db.Context, new DevTenantContext(h.TenantId),
            new DevCurrentUserContext(h.DriverUserId),
            new AuditService(h.Db.Context, new DevTenantContext(h.TenantId), new DevCurrentUserContext(h.DriverUserId)),
            new TestClock(Now));
        await scanService.SubmitAsync(h.TripId, h.StopId,
            new TransportationService.Api.Modules.Scanning.Dtos.SubmitScanRequest(ScanType.Unload, "BC-1", 2, false, null, null),
            false, CancellationToken.None);

        var result = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(signature: SignatureDataUrl), true, CancellationToken.None);

        Assert.Equal(PodOutcomeResult.Success, result.Outcome);
        var pod = result.Pod!;
        Assert.Equal(1, pod.Version);
        Assert.True(pod.IsCurrent);
        Assert.Equal("Magazijnier P. Peeters", pod.RecipientName);
        Assert.Equal(PodOutcome.Complete, pod.Outcome);
        Assert.Equal(Now.UtcDateTime, pod.DeliveredAt);
        Assert.True(pod.HasSignature);
        Assert.Equal("Jan Jansen", pod.FinalisedByName);
        var scanLine = Assert.Single(pod.ScannedSummary);
        Assert.Equal("Pallet cement", scanLine.Description);
        Assert.Equal(2, scanLine.ScannedQuantity);
        Assert.Equal(5, scanLine.ExpectedQuantity);

        // A second finalisation on the same stop is refused: corrections are the only path.
        var again = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(), true, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.InvalidState, again.Outcome);
    }

    [Fact]
    public async Task Finalize_Validation_AndGuards()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noRecipient = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(recipient: " "), true, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.ValidationFailed, noRecipient.Outcome);

        var unknownStop = await h.Sut.FinalizeAsync(h.TripId, Guid.NewGuid(), Finalize(), true, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.NotFound, unknownStop.Outcome);

        var foreign = CreateSut(h.Db, Guid.NewGuid(), Guid.NewGuid(), h.StorageRoot);
        var crossTenant = await foreign.FinalizeAsync(h.TripId, h.StopId, Finalize(), false, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.NotFound, crossTenant.Outcome);

        var strangerUser = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = strangerUser, TenantId = h.TenantId, Email = "ander@acme.be", PasswordHash = "x",
            FirstName = "Piet", LastName = "Peters", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();
        var stranger = CreateSut(h.Db, h.TenantId, strangerUser, h.StorageRoot);
        var notYours = await stranger.FinalizeAsync(h.TripId, h.StopId, Finalize(), true, CancellationToken.None);
        Assert.Equal(PodOutcomeResult.NotYourTrip, notYours.Outcome);
    }

    [Fact]
    public async Task Correct_CreatesNewVersion_OriginalStaysVisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var v1 = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(), true, CancellationToken.None);

        var noReason = await h.Sut.CorrectAsync(v1.Pod!.Id,
            new CorrectPodRequest("Andere naam", null, PodOutcome.Partial, true, false, null, null, null, null, " "),
            CancellationToken.None);
        Assert.Equal(PodOutcomeResult.ValidationFailed, noReason.Outcome);

        var corrected = await h.Sut.CorrectAsync(v1.Pod.Id,
            new CorrectPodRequest("K. Klaassen", "Receptie", PodOutcome.Partial, true, false,
                "2 colli geweigerd", null, null, null, "Verkeerde naam genoteerd"),
            CancellationToken.None);

        Assert.Equal(PodOutcomeResult.Success, corrected.Outcome);
        var v2 = corrected.Pod!;
        Assert.Equal(2, v2.Version);
        Assert.True(v2.IsCurrent);
        Assert.Equal("K. Klaassen", v2.RecipientName);
        Assert.Equal("Verkeerde naam genoteerd", v2.CorrectionReason);
        Assert.Equal(v1.Pod.Id, v2.CorrectedFromPodId);

        // The original version is preserved and visible in the chain.
        Assert.Equal(2, v2.Versions.Count);
        var original = v2.Versions.Single(x => x.Version == 1);
        Assert.False(original.IsCurrent);

        var v1Detail = await h.Sut.GetByIdAsync(v1.Pod.Id, CancellationToken.None);
        Assert.NotNull(v1Detail);
        Assert.Equal("Magazijnier P. Peeters", v1Detail!.RecipientName);
        Assert.False(v1Detail.IsCurrent);

        // Correcting the superseded version is refused; only the current one moves forward.
        var onOld = await h.Sut.CorrectAsync(v1.Pod.Id,
            new CorrectPodRequest("X", null, PodOutcome.Complete, false, false, null, null, null, null, "reden"),
            CancellationToken.None);
        Assert.Equal(PodOutcomeResult.InvalidState, onOld.Outcome);

        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "ProofOfDelivery" && a.Action == "Corrected");
    }

    [Fact]
    public async Task Photos_AdditiveOnCurrentVersion_ByCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var v1 = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(), true, CancellationToken.None);

        try
        {
            using var upload = new MemoryStream([1, 2, 3]);
            var attached = await h.Sut.AttachPhotoAsync(v1.Pod!.Id, PodPhotoCategory.Delivery,
                "levering.jpg", "image/jpeg", upload, true, CancellationToken.None);
            Assert.Equal(PodOutcomeResult.Success, attached.Outcome);
            var photo = Assert.Single(attached.Pod!.Photos);
            Assert.Equal(PodPhotoCategory.Delivery, photo.Category);

            var corrected = await h.Sut.CorrectAsync(v1.Pod.Id,
                new CorrectPodRequest("N", null, PodOutcome.Complete, false, false, null, null, null, null, "fix"),
                CancellationToken.None);

            using var upload2 = new MemoryStream([4, 5]);
            var onSuperseded = await h.Sut.AttachPhotoAsync(v1.Pod.Id, PodPhotoCategory.Document,
                "cmr.jpg", "image/jpeg", upload2, true, CancellationToken.None);
            Assert.Equal(PodOutcomeResult.InvalidState, onSuperseded.Outcome);

            using var upload3 = new MemoryStream([6]);
            var onCurrent = await h.Sut.AttachPhotoAsync(corrected.Pod!.Id, PodPhotoCategory.Document,
                "cmr.jpg", "image/jpeg", upload3, true, CancellationToken.None);
            Assert.Equal(PodOutcomeResult.Success, onCurrent.Outcome);
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Signature_RoundTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var result = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(signature: SignatureDataUrl), true, CancellationToken.None);

        try
        {
            var signature = await h.Sut.OpenSignatureAsync(result.Pod!.Id, CancellationToken.None);
            Assert.NotNull(signature);
            using var ms = new MemoryStream();
            await using (var content = signature!.Value.Content)
            {
                await content.CopyToAsync(ms);
            }
            Assert.True(ms.Length > 20);
            Assert.Equal("image/png", signature.Value.ContentType);
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task GetForStop_ReturnsCurrent_WithVersionChain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var v1 = await h.Sut.FinalizeAsync(h.TripId, h.StopId, Finalize(), true, CancellationToken.None);
        await h.Sut.CorrectAsync(v1.Pod!.Id,
            new CorrectPodRequest("Nieuw", null, PodOutcome.Refused, false, true, null, null, null, null, "Geweigerd achteraf"),
            CancellationToken.None);

        var current = await h.Sut.GetForStopAsync(h.TripId, h.StopId, false, CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(2, current!.Version);
        Assert.Equal(PodOutcome.Refused, current.Outcome);
        Assert.True(current.MissingReported);
        Assert.Equal(2, current.Versions.Count);

        // Foreign tenant sees nothing.
        var foreign = CreateSut(h.Db, Guid.NewGuid(), Guid.NewGuid(), h.StorageRoot);
        Assert.Null(await foreign.GetForStopAsync(h.TripId, h.StopId, false, CancellationToken.None));
    }
}
