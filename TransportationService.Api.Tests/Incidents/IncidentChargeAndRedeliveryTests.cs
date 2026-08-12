using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Incidents;

/// <summary>
/// Wave 6: responsibility attribution, the approval-gated charge decision (only Customer-
/// responsible problems, only problems.approve_charge decides, approval creates the Manual
/// sales line on the linked order), linked redelivery creation into the same dossier, and the
/// unified problem list.
/// </summary>
public class IncidentChargeAndRedeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 17, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, IncidentService Sut, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid OrderId, Guid DossierId)
    {
        public async Task<Guid> IncidentAsync(string responsibleParty = "Customer")
        {
            var incident = await Sut.CreateAsync(new SaveIncidentRequest(
                "Pallet beschadigd", "Omgevallen bij het lossen.", "Damage", "High",
                CustomerId: CustomerId, TransportOrderId: OrderId, DossierId: DossierId,
                ResponsibleParty: responsibleParty), CancellationToken.None);
            return incident.Id;
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 2,
        });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "b@acme.be", FirstName = "Bea", LastName = "Boekhouding", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV", IsActive = true });
        var order = new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new DateOnly(2026, 8, 10), Status = TransportOrderStatus.Completed,
            GoodsDescription = "20 paletten", AgreedPrice = 500m,
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen", CountryCode = "BE",
        });
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, City = "Hasselt", CountryCode = "BE",
        });
        db.Context.TransportOrders.Add(order);
        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0001", CustomerId = customerId,
            OriginTransportOrderId = orderId,
        });
        db.Context.Packages.Add(new Package
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = orderId,
            PackageNumber = "PKG-1", CurrentLifecycleStatus = PackageLifecycleStatus.DeliveryFailed,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var clock = new TestClock(Now);
        var permissions = new PermissionSet();
        var sut = new IncidentService(db.Context, tenant,
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, clock),
            clock, permissions, user);
        return new Harness(db, sut, permissions, tenantId, customerId, orderId, dossierId);
    }

    [Fact]
    public async Task Responsibility_RoundTrips_AndUnknownInputFallsBack()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await h.IncidentAsync("Customer");

        var detail = await h.Sut.GetAsync(id, CancellationToken.None);
        Assert.Equal("Customer", detail!.ResponsibleParty);

        var updated = await h.Sut.UpdateAsync(id, new SaveIncidentRequest(
            "Pallet beschadigd", "Omgevallen.", "Damage", "High",
            ResponsibleParty: "Alien"), CancellationToken.None);
        Assert.Equal("Unknown", updated!.ResponsibleParty);
    }

    [Fact]
    public async Task Charge_OnlyCustomerResponsibility_CanBeProposed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await h.IncidentAsync("Own");

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.ProposeChargeAsync(
            id, new ProposeIncidentChargeRequest(150m, "Herstelkosten"), CancellationToken.None));
    }

    [Fact]
    public async Task Charge_ApprovalNeedsTheRight_AndCreatesTheSalesLine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await h.IncidentAsync();
        await h.Sut.ProposeChargeAsync(id, new ProposeIncidentChargeRequest(150m, "Herstelkosten pallet"), CancellationToken.None);

        // Without the right: fail-closed.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.DecideChargeAsync(
            id, new DecideIncidentChargeRequest(true), CancellationToken.None));

        h.Permissions.Codes.Add(PermissionCodes.ProblemsApproveCharge);
        var approved = await h.Sut.DecideChargeAsync(id, new DecideIncidentChargeRequest(true), CancellationToken.None);

        Assert.Equal("Approved", approved!.ChargeDecision);
        var line = await h.Db.Context.TransportOrderPricingLines
            .SingleAsync(l => l.TransportOrderId == h.OrderId);
        Assert.Equal(150m, line.Amount);
        Assert.Equal(OrderPriceLineKind.Manual, line.Kind);
        Assert.Contains("Incident", line.AdjustReason);
        Assert.Equal(650m, (await h.Db.Context.TransportOrders.SingleAsync(o => o.Id == h.OrderId)).AgreedPrice);
    }

    [Fact]
    public async Task Charge_Rejection_RecordsWithoutALine()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await h.IncidentAsync();
        await h.Sut.ProposeChargeAsync(id, new ProposeIncidentChargeRequest(150m, "Herstelkosten"), CancellationToken.None);
        h.Permissions.Codes.Add(PermissionCodes.ProblemsApproveCharge);

        var rejected = await h.Sut.DecideChargeAsync(id, new DecideIncidentChargeRequest(false), CancellationToken.None);

        Assert.Equal("Rejected", rejected!.ChargeDecision);
        Assert.Empty(await h.Db.Context.TransportOrderPricingLines
            .Where(l => l.TransportOrderId == h.OrderId).ToListAsync());
    }

    [Fact]
    public async Task Redelivery_DuplicatesIntoTheSameDossier_AndFlipsPackages()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await h.IncidentAsync();

        var detail = await h.Sut.CreateRedeliveryAsync(id, CancellationToken.None);

        Assert.NotNull(detail!.LinkedRedeliveryOrderId);
        var redelivery = await h.Db.Context.TransportOrders
            .Include(o => o.Stops)
            .SingleAsync(o => o.Id == detail.LinkedRedeliveryOrderId);
        Assert.Equal(TransportOrderStatus.Draft, redelivery.Status);
        Assert.Equal("HERLEVERING ORD-0001", redelivery.CustomerReference);
        Assert.Equal(2, redelivery.Stops.Count);
        Assert.True(await h.Db.Context.DossierOrders.AnyAsync(
            l => l.DossierId == h.DossierId && l.TransportOrderId == redelivery.Id));
        Assert.Equal(PackageLifecycleStatus.RedeliveryPlanned,
            (await h.Db.Context.Packages.SingleAsync(p => p.TransportOrderId == h.OrderId)).CurrentLifecycleStatus);

        // A second redelivery for the same incident refuses.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateRedeliveryAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task ProblemList_MergesOpenIncidentsAndExceptions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.IncidentAsync();
        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0001", TripDate = new DateOnly(2026, 8, 12),
            Status = Modules.Planning.Entities.TripStatus.InProgress,
        });
        h.Db.Context.ExecutionExceptions.Add(new Modules.Exceptions.Entities.ExecutionException
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = h.OrderId,
            Type = Modules.Exceptions.Entities.ExecutionExceptionType.DamagedPackage,
            Status = Modules.Exceptions.Entities.ExecutionExceptionStatus.Open,
            Description = "Hoek ingedeukt", OccurredAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var problems = await h.Sut.ListProblemsAsync(CancellationToken.None);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Kind == "Incident" && p.OrderNumber == "ORD-0001");
        Assert.Contains(problems, p => p.Kind == "Exception" && p.TripNumber == "RIT-0001");
    }
}
