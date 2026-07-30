using System.Text;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Exceptions.Dtos;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Exceptions.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Exceptions;

/// <summary>
/// Wave 3: structured exception reporting — driver reports with full context, controlled
/// resolution workflow with mandatory decision notes, dispatcher notifications, photos.
/// </summary>
public class ExceptionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, ExecutionExceptionService Sut, string StorageRoot, Guid TenantId,
        Guid TripId, Guid OrderId, Guid StopId, Guid CargoItemId, Guid DriverUserId, Guid DispatcherUserId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var driverUserId = Guid.NewGuid();
        var dispatcherUserId = Guid.NewGuid();
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
        db.Context.Users.AddRange(
            new User
            {
                Id = driverUserId, TenantId = tenantId, Email = "chauffeur@acme.be", PasswordHash = "x",
                FirstName = "Jan", LastName = "Jansen", EmployeeId = employeeId, IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new User
            {
                Id = dispatcherUserId, TenantId = tenantId, Email = "dispatch@acme.be", PasswordHash = "x",
                FirstName = "Dora", LastName = "Dispatch", IsActive = true,
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });

        // Grant the dispatcher the resolve permission so reported exceptions notify them.
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Dispatch", IsActive = true });
        db.Context.Permissions.Add(new Permission
        {
            Id = permissionId, Code = PermissionCodes.ExceptionsResolve, Module = "exceptions", Action = "resolve",
        });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = dispatcherUserId, RoleId = roleId });

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

        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-exception-tests", Guid.NewGuid().ToString("N"));
        var sut = CreateSut(db, tenantId, driverUserId, storageRoot);
        return new Harness(db, sut, storageRoot, tenantId, tripId, orderId, stopId, cargoItemId, driverUserId, dispatcherUserId);
    }

    private static ExecutionExceptionService CreateSut(SqliteTestDbContext db, Guid tenantId, Guid userId, string storageRoot)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        return new ExecutionExceptionService(
            db.Context, tenant, user,
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, new TestClock(Now)),
            new LocalFileStorageService(storageRoot),
            new TestClock(Now));
    }

    private static ReportExceptionRequest Report(
        ExecutionExceptionType type = ExecutionExceptionType.DamagedPackage,
        string description = "Pallet zwaar beschadigd bij lossen",
        Guid? stopId = null, Guid? cargoItemId = null, decimal? quantity = null) =>
        new(type, ExceptionSeverity.High, description, stopId, cargoItemId, quantity, null, null);

    [Fact]
    public async Task Report_FromStopContext_LinksEverything_AndNotifiesResolvers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReportAsync(h.TripId,
            Report(stopId: h.StopId, cargoItemId: h.CargoItemId, quantity: 2), true, CancellationToken.None);

        Assert.Equal(ExceptionOutcome.Success, result.Outcome);
        var detail = result.Exception!;
        Assert.Equal(ExecutionExceptionStatus.Open, detail.Status);
        Assert.Equal("RIT-0001", detail.TripNumber);
        Assert.Equal("ORD-0001", detail.OrderNumber);
        Assert.Equal(h.StopId, detail.TransportOrderStopId);
        Assert.Equal("Pallet cement", detail.CargoDescription);
        Assert.Equal("Jan Jansen", detail.ReportedByName);
        Assert.Equal("Jan Jansen", detail.DriverName);
        Assert.Equal(2, detail.Quantity);

        // The dispatcher (exceptions.resolve holder) got an in-app notification.
        Assert.Contains(h.Db.Context.Notifications,
            n => n.UserId == h.DispatcherUserId && n.Type == "exception_reported");
    }

    [Fact]
    public async Task Report_TripLevel_WithoutStop_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ReportAsync(h.TripId,
            Report(ExecutionExceptionType.VehicleIssue, "Lekke band op de E17"), true, CancellationToken.None);

        Assert.Equal(ExceptionOutcome.Success, result.Outcome);
        Assert.Null(result.Exception!.TransportOrderStopId);
        Assert.Null(result.Exception.OrderNumber);
    }

    [Fact]
    public async Task Report_Validation_And_Guards()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var noDescription = await h.Sut.ReportAsync(h.TripId, Report(description: " "), true, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.ValidationFailed, noDescription.Outcome);

        var unknownStop = await h.Sut.ReportAsync(h.TripId, Report(stopId: Guid.NewGuid()), true, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.NotFound, unknownStop.Outcome);

        var unknownTrip = await h.Sut.ReportAsync(Guid.NewGuid(), Report(), true, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.NotFound, unknownTrip.Outcome);

        var foreign = CreateSut(h.Db, Guid.NewGuid(), Guid.NewGuid(), h.StorageRoot);
        var crossTenant = await foreign.ReportAsync(h.TripId, Report(), false, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.NotFound, crossTenant.Outcome);
    }

    [Fact]
    public async Task StatusFlow_GuardsTransitions_AndRequiresTerminalNote()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.ReportAsync(h.TripId, Report(), true, CancellationToken.None);
        var id = created.Exception!.Id;

        var resolver = CreateSut(h.Db, h.TenantId, h.DispatcherUserId, h.StorageRoot);

        // Terminal without note refused.
        var noNote = await resolver.ChangeStatusAsync(id,
            new ChangeExceptionStatusRequest(ExecutionExceptionStatus.Resolved, null), CancellationToken.None);
        Assert.Equal(ExceptionOutcome.ValidationFailed, noNote.Outcome);

        var investigating = await resolver.ChangeStatusAsync(id,
            new ChangeExceptionStatusRequest(ExecutionExceptionStatus.Investigating, null), CancellationToken.None);
        Assert.Equal(ExceptionOutcome.Success, investigating.Outcome);

        var resolved = await resolver.ChangeStatusAsync(id,
            new ChangeExceptionStatusRequest(ExecutionExceptionStatus.Resolved, "Vervangende pallet geleverd"), CancellationToken.None);
        Assert.Equal(ExceptionOutcome.Success, resolved.Outcome);
        Assert.Equal("Vervangende pallet geleverd", resolved.Exception!.ResolutionNote);
        Assert.Equal("Dora Dispatch", resolved.Exception.ResolvedByName);
        Assert.NotNull(resolved.Exception.ResolvedAt);

        // Terminal state refuses further moves.
        var reopen = await resolver.ChangeStatusAsync(id,
            new ChangeExceptionStatusRequest(ExecutionExceptionStatus.Investigating, null), CancellationToken.None);
        Assert.Equal(ExceptionOutcome.InvalidState, reopen.Outcome);

        // The reporter was notified about the decision.
        Assert.Contains(h.Db.Context.Notifications,
            n => n.UserId == h.DriverUserId && n.Type == "exception_decided");

        Assert.Contains(h.Db.Context.AuditLogs,
            a => a.EntityType == "ExecutionException" && a.Action == "StatusChanged");
    }

    [Fact]
    public async Task Update_DispatcherFields_Audited()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.ReportAsync(h.TripId, Report(), true, CancellationToken.None);

        var resolver = CreateSut(h.Db, h.TenantId, h.DispatcherUserId, h.StorageRoot);
        var updated = await resolver.UpdateAsync(created.Exception!.Id,
            new UpdateExceptionRequest(ExceptionSeverity.Critical, "Klant belt elk uur", CustomerVisible: true),
            CancellationToken.None);

        Assert.Equal(ExceptionOutcome.Success, updated.Outcome);
        Assert.Equal(ExceptionSeverity.Critical, updated.Exception!.Severity);
        Assert.Equal("Klant belt elk uur", updated.Exception.DispatcherNotes);
        Assert.True(updated.Exception.CustomerVisible);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "ExecutionException" && a.Action == "Updated");
    }

    [Fact]
    public async Task Search_Filters_AndIsolatesTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.ReportAsync(h.TripId, Report(ExecutionExceptionType.Delay, "File", stopId: null), true, CancellationToken.None);
        await h.Sut.ReportAsync(h.TripId, Report(ExecutionExceptionType.DamagedPackage, "Schade aan pallet", stopId: h.StopId), true, CancellationToken.None);

        var all = await h.Sut.SearchAsync(null, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(2, all.TotalCount);

        var damaged = await h.Sut.SearchAsync(null, ExecutionExceptionType.DamagedPackage, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(1, damaged.TotalCount);

        var open = await h.Sut.SearchAsync(ExecutionExceptionStatus.Open, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(2, open.TotalCount);

        var foreign = CreateSut(h.Db, Guid.NewGuid(), Guid.NewGuid(), h.StorageRoot);
        var foreignList = await foreign.SearchAsync(null, null, null, null, null, null, 1, 25, CancellationToken.None);
        Assert.Equal(0, foreignList.TotalCount);
    }

    [Fact]
    public async Task TripList_RestrictsToOwnDriver()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.ReportAsync(h.TripId, Report(), true, CancellationToken.None);

        var own = await h.Sut.ListForTripAsync(h.TripId, true, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.Success, own.Outcome);
        Assert.Single(own.Exceptions!);

        // A same-tenant user without a driver link is refused when restricted.
        var stranger = CreateSut(h.Db, h.TenantId, h.DispatcherUserId, h.StorageRoot);
        var restricted = await stranger.ListForTripAsync(h.TripId, true, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.NotYourTrip, restricted.Outcome);

        var unrestricted = await stranger.ListForTripAsync(h.TripId, false, CancellationToken.None);
        Assert.Equal(ExceptionOutcome.Success, unrestricted.Outcome);
    }

    [Fact]
    public async Task Photos_AttachDownloadDelete_RoundTrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.ReportAsync(h.TripId, Report(), true, CancellationToken.None);
        var id = created.Exception!.Id;

        try
        {
            using var upload = new MemoryStream(Encoding.UTF8.GetBytes("fake-jpeg-bytes"));
            var attached = await h.Sut.AttachPhotoAsync(id, "schade.jpg", "image/jpeg", upload, true, CancellationToken.None);
            Assert.Equal(ExceptionOutcome.Success, attached.Outcome);
            var photo = Assert.Single(attached.Exception!.Photos);
            Assert.Equal("schade.jpg", photo.FileName);

            // restrictToOwnDriver: false = a staff caller holding exceptions.view.
            var open = await h.Sut.OpenPhotoAsync(id, photo.Id, false, CancellationToken.None);
            Assert.NotNull(open);
            using (var reader = new StreamReader(open!.Value.Content))
            {
                Assert.Equal("fake-jpeg-bytes", await reader.ReadToEndAsync());
            }
            Assert.Equal("image/jpeg", open.Value.ContentType);

            var resolver = CreateSut(h.Db, h.TenantId, h.DispatcherUserId, h.StorageRoot);
            var deleted = await resolver.DeletePhotoAsync(id, photo.Id, CancellationToken.None);
            Assert.Equal(ExceptionOutcome.Success, deleted.Outcome);
            Assert.Empty(deleted.Exception!.Photos);
        }
        finally
        {
            try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
