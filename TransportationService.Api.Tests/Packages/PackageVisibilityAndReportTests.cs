using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Scanning.Dtos;
using TransportationService.Api.Modules.Scanning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

/// <summary>
/// Wave P10: notification producers fire once per problem (never per scan), the customer
/// summary is redacted by construction, and the XLSX reports build with text-safe cells.
/// </summary>
public class PackageVisibilityAndReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WrongTripScan_NotifiesDispatchOnce_NotPerScan()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderOnTrip = Guid.NewGuid();
        var orderElsewhere = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var dispatcherId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven", IsActive = true });
        db.Context.TransportOrders.AddRange(
            new TransportOrder { Id = orderOnTrip, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1", OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "A" },
            new TransportOrder { Id = orderElsewhere, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-2", OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.Confirmed, GoodsDescription = "B" });
        db.Context.TransportOrderStops.Add(new TransportOrderStop { Id = stopId, TenantId = tenantId, TransportOrderId = orderOnTrip, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" });
        db.Context.Trips.Add(new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-1", TripDate = new(2026, 7, 21),
            Status = TransportationService.Api.Modules.Planning.Entities.TripStatus.InProgress,
        });
        db.Context.TripOrders.Add(new TransportationService.Api.Modules.Planning.Entities.TripOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripId = tripId, TransportOrderId = orderOnTrip, Sequence = 1,
        });
        db.Context.Packages.Add(new Package
        {
            Id = packageId, TenantId = tenantId, TransportOrderId = orderElsewhere,
            PackageNumber = "PKG-1", BarcodeValue = "PKG-1-X", Description = "Vreemde doos",
            CurrentLifecycleStatus = PackageLifecycleStatus.Labelled,
        });
        db.Context.PackageBarcodes.Add(new PackageBarcode
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageId = packageId, Value = "PKG-1-X",
            Type = PackageBarcodeType.Code128, IsActive = true,
        });

        // A dispatcher (planning.edit holder) who should hear about the problem exactly once.
        var role = new TransportationService.Api.Modules.Identity.Entities.Role
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Dispatch", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        };
        var permission = new TransportationService.Api.Modules.Identity.Entities.Permission
        {
            Id = Guid.NewGuid(), Code = TransportationService.Api.Modules.Identity.PermissionCodes.PlanningEdit,
            Module = "planning", Action = "edit", Description = "Planning bewerken",
        };
        var dispatcher = new TransportationService.Api.Modules.Identity.Entities.User
        {
            Id = dispatcherId, TenantId = tenantId, Email = "dispatch@acme.be", PasswordHash = "x",
            FirstName = "Dora", LastName = "Dispatch", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        };
        db.Context.Roles.Add(role);
        db.Context.Permissions.Add(permission);
        db.Context.Users.Add(dispatcher);
        db.Context.RolePermissions.Add(new TransportationService.Api.Modules.Identity.Entities.RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        db.Context.UserRoles.Add(new TransportationService.Api.Modules.Identity.Entities.UserRole { UserId = dispatcherId, RoleId = role.Id });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = Scanning.ScanServiceTests.CreateService(db, tenant, null, new TestClock(Now));

        await sut.SubmitAsync(tripId, stopId, new SubmitScanRequest(ScanType.Load, "PKG-1-X"), false, CancellationToken.None);
        await sut.SubmitAsync(tripId, stopId, new SubmitScanRequest(ScanType.Load, "PKG-1-X"), false, CancellationToken.None);

        var notifications = await db.Context.Set<Notification>()
            .Where(n => n.UserId == dispatcherId && n.Type == "package_incident")
            .ToListAsync();
        Assert.Single(notifications);
        Assert.Contains("PKG-1", notifications[0].Message);
    }

    [Fact]
    public async Task CustomerSummary_IsRedacted_NeutralLabels_NoBarcodes()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 20), Status = TransportOrderStatus.InProgress, GoodsDescription = "A",
        });
        void AddPackage(string number, PackageLifecycleStatus status, string? notes = null) =>
            db.Context.Packages.Add(new Package
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
                PackageNumber = number, BarcodeValue = number + "-GEHEIM", Description = "Doos " + number,
                CurrentLifecycleStatus = status, Notes = notes,
            });
        AddPackage("PKG-1", PackageLifecycleStatus.Delivered);
        AddPackage("PKG-2", PackageLifecycleStatus.InTransit);
        AddPackage("PKG-3", PackageLifecycleStatus.Refused, notes: "Chauffeur meldt agressieve klant");
        AddPackage("PKG-4", PackageLifecycleStatus.Labelled);
        AddPackage("PKG-5", PackageLifecycleStatus.Cancelled);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var currentUser = new DevCurrentUserContext(null);
        var service = new PackageService(db.Context, tenant,
            new AuditService(db.Context, tenant, currentUser),
            new PackageBarcodeService(db.Context, tenant, currentUser, clock),
            new PackageEventWriter(db.Context, tenant, currentUser, clock));

        var summary = await service.GetCustomerSummaryAsync(orderId, CancellationToken.None);

        Assert.NotNull(summary);
        // Cancelled is not part of the customer's expectation.
        Assert.Equal(4, summary!.Total);
        Assert.Equal(1, summary.Delivered);
        Assert.Equal(1, summary.InTransit);
        Assert.Equal(1, summary.Pending);
        Assert.Equal(1, summary.InHandling);
        Assert.Equal("In behandeling", summary.Packages.Single(p => p.PackageNumber == "PKG-3").StatusLabel);

        // Redaction by construction: the serialized payload contains no barcode and no notes.
        var json = JsonSerializer.Serialize(summary);
        Assert.DoesNotContain("GEHEIM", json);
        Assert.DoesNotContain("agressieve", json);
        Assert.DoesNotContain("Refused", json);
    }

    [Fact]
    public async Task Reports_AllKeysBuild_AndFormulaTextStaysText()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 18), Status = TransportOrderStatus.InProgress, GoodsDescription = "A",
        });
        db.Context.Packages.Add(new Package
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
            PackageNumber = "PKG-1", BarcodeValue = "PKG-1-X",
            Description = "=SUM(A1:A9)", CurrentLifecycleStatus = PackageLifecycleStatus.Missing,
            CreatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();

        var service = new PackageReportService(db.Context, new DevTenantContext(tenantId),
            new DevCurrentUserContext(null), new TestClock(Now));

        foreach (var key in service.ReportKeys)
        {
            var built = await service.BuildAsync(key, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
            Assert.NotNull(built);
            Assert.True(built!.Value.Content.Length > 500, $"{key} produced no workbook");
        }

        Assert.Null(await service.BuildAsync("bestaat-niet", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None));

        // The hostile description must be stored as TEXT, never as a formula.
        var missingReport = await service.BuildAsync("missing-packages", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), CancellationToken.None);
        using var stream = new MemoryStream(missingReport!.Value.Content);
        using var workbook = new XLWorkbook(stream);
        var cell = workbook.Worksheets.First().Cell(2, 2);
        Assert.False(cell.HasFormula);
        Assert.Equal("=SUM(A1:A9)", cell.GetString());
    }
}
