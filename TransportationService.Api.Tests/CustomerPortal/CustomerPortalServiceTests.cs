using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

public class CustomerPortalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid OtherCustomerId,
        Guid PortalUserId, Guid UnlinkedUserId, Guid OwnLocationId, Guid ForeignLocationId,
        TransportOrderService Orders)
    {
        public CustomerPortalService For(Guid userId)
        {
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(userId);
            var audit = new AuditService(Db.Context, tenant, user);
            var locations = new LocationService(Db.Context, tenant, audit, new CountryCodeValidator(Db.Context));
            return new CustomerPortalService(Db.Context, tenant, user, Orders, locations, audit);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var unlinkedUserId = Guid.NewGuid();
        var ownLocationId = Guid.NewGuid();
        var foreignLocationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true },
            new User { Id = unlinkedUserId, TenantId = tenantId, Email = "los@acme.be", FirstName = "Los", LastName = "Zonder", IsActive = true });
        db.Context.Locations.AddRange(
            new Location { Id = ownLocationId, TenantId = tenantId, Code = "EIGEN-1", Name = "Magazijn Haven", Type = LocationType.CustomerLocation, City = "Antwerpen", CustomerId = customerId, IsActive = true },
            new Location { Id = foreignLocationId, TenantId = tenantId, Code = "VREEMD-1", Name = "Andermans site", Type = LocationType.CustomerLocation, City = "Gent", CustomerId = otherCustomerId, IsActive = true });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var orders = new TransportOrderService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return new Harness(db, tenantId, customerId, otherCustomerId, portalUserId, unlinkedUserId, ownLocationId, foreignLocationId, orders);
    }

    private static PortalCreateOrderRequest Request(Harness h, Guid? loadingLocationId = null) => new(
        CustomerReference: "PO-KLANT-1",
        OrderDate: new DateOnly(2026, 7, 21),
        GoodsDescription: "12 europalletten dranken",
        Remarks: "Graag laden vóór 10u",
        Stops:
        [
            new PortalStopInput(StopType.Loading, loadingLocationId ?? h.OwnLocationId, null, null, null, null, null,
                new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc), null, null),
            new PortalStopInput(StopType.Unloading, null, "Klant eindbestemming", "Dorpsstraat 1", "9000", "Gent", "BE",
                null, null, null, null),
        ],
        CargoItems: [new PortalCargoInput("Europalletten dranken", 12, "paletten", TransportationService.Api.Modules.Packages.Entities.PackageUnitType.EuroPallet, TotalWeightKg: 7200)]);

    [Fact]
    public async Task Submit_ForcesOwnCustomer_EntersSubmitted_AndNeverCarriesAPrice()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.PortalUserId).SubmitOrderAsync(Request(h), CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        Assert.Equal(TransportOrderStatus.Submitted, result.Value!.Status);

        var stored = h.Db.Context.TransportOrders.Single();
        Assert.Equal(h.CustomerId, stored.CustomerId);
        Assert.Null(stored.AgreedPrice);
        Assert.Equal("ORD-0001", stored.OrderNumber);

        // The immutable status trail records the portal submission with its marker.
        var history = h.Db.Context.TransportOrderStatusHistories.Single();
        Assert.Equal(TransportOrderStatus.Draft, history.FromStatus);
        Assert.Equal(TransportOrderStatus.Submitted, history.ToStatus);
        Assert.Equal("Ingediend via het klantportaal", history.Reason);

        // The planner can accept the submission through the normal transition map.
        var confirmed = await h.Orders.ChangeStatusAsync(stored.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, confirmed.Outcome);
    }

    [Fact]
    public async Task GetMyOrder_IncludesTimeline_AndOnlyCustomerVisibleExceptions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var submitted = await h.For(h.PortalUserId).SubmitOrderAsync(Request(h), CancellationToken.None);
        var orderId = submitted.Value!.Id;
        await h.Orders.ChangeStatusAsync(orderId, TransportOrderStatus.Confirmed, CancellationToken.None);

        h.Db.Context.Trips.Add(new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "TR-1", TripDate = new DateOnly(2026, 7, 21),
        });
        var tripId = h.Db.Context.Trips.Local.First().Id;
        h.Db.Context.ExecutionExceptions.AddRange(
            new TransportationService.Api.Modules.Exceptions.Entities.ExecutionException
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = orderId,
                Type = TransportationService.Api.Modules.Exceptions.Entities.ExecutionExceptionType.Delay,
                Status = TransportationService.Api.Modules.Exceptions.Entities.ExecutionExceptionStatus.Open,
                CustomerVisible = true, Description = "Vertraging door verkeer", OccurredAt = Now.UtcDateTime,
            },
            new TransportationService.Api.Modules.Exceptions.Entities.ExecutionException
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderId = orderId,
                Type = TransportationService.Api.Modules.Exceptions.Entities.ExecutionExceptionType.Other,
                Status = TransportationService.Api.Modules.Exceptions.Entities.ExecutionExceptionStatus.Open,
                CustomerVisible = false, Description = "Interne notitie", OccurredAt = Now.UtcDateTime,
            });
        await h.Db.Context.SaveChangesAsync();

        var detail = await h.For(h.PortalUserId).GetMyOrderAsync(orderId, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, detail.Outcome);

        // Draft->Submitted->Confirmed: both customer-visible transitions on the timeline.
        Assert.Equal(2, detail.Value!.Timeline.Count);
        Assert.Contains(detail.Value.Timeline, e => e.Status == TransportOrderStatus.Submitted);
        Assert.Contains(detail.Value.Timeline, e => e.Status == TransportOrderStatus.Confirmed);

        var exception = Assert.Single(detail.Value.Exceptions);
        Assert.Equal("Vertraging door verkeer", exception.Description);
    }

    [Fact]
    public async Task Submit_WithAnotherCustomersLocation_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.PortalUserId).SubmitOrderAsync(Request(h, loadingLocationId: h.ForeignLocationId), CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.ValidationFailed, result.Outcome);
        Assert.Empty(h.Db.Context.TransportOrders.ToList());
    }

    [Fact]
    public async Task OtherCustomersOrders_AreInvisible_AndUnlinkedUsersAreRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var submitted = await h.For(h.PortalUserId).SubmitOrderAsync(Request(h), CancellationToken.None);

        // A second portal user linked to the OTHER customer sees nothing of it.
        var otherUserId = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = otherUserId, TenantId = h.TenantId, Email = "x@andere.be", FirstName = "An", LastName = "Dere",
            CustomerId = h.OtherCustomerId, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var foreignView = await h.For(otherUserId).GetMyOrderAsync(submitted.Value!.Id, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, foreignView.Outcome);
        var foreignList = await h.For(otherUserId).ListMyOrdersAsync(CancellationToken.None);
        Assert.Empty(foreignList.Value!);

        var unlinked = await h.For(h.UnlinkedUserId).ListMyOrdersAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, unlinked.Outcome);
    }

    [Fact]
    public async Task Locations_AreScopedToOwnCustomer_AndPortalCreationLinksThem()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.PortalUserId);

        var list = await sut.ListMyLocationsAsync(CancellationToken.None);
        var visible = Assert.Single(list.Value!);
        Assert.Equal("Magazijn Haven", visible.Name);

        var created = await sut.CreateMyLocationAsync(
            new PortalCreateLocationRequest("Nieuw filiaal", "Kade 12", null, "2000", "Antwerpen", "BE"), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, created.Outcome);

        var stored = h.Db.Context.Locations.Single(l => l.Name == "Nieuw filiaal");
        Assert.Equal(h.CustomerId, stored.CustomerId);
        Assert.Equal(LocationType.CustomerLocation, stored.Type);
    }

    [Fact]
    public async Task Locations_ASharedAddressLinkedToMyCustomer_IsListedAndAccepted_WithMyOwnDefaults()
    {
        // D4: membership and defaults come from the customer↔address relationship, not from the
        // legacy owner column — a shared address (legacy owner = the other customer) is mine too.
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.CustomerLocationLinks.Add(new CustomerLocationLink
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, LocationId = h.ForeignLocationId,
            Role = CustomerLocationRole.Both, IsActive = true, IsDefaultUnloading = true,
        });
        await h.Db.Context.SaveChangesAsync();
        var sut = h.For(h.PortalUserId);

        var list = await sut.ListMyLocationsAsync(CancellationToken.None);

        Assert.Equal(2, list.Value!.Count);
        var shared = list.Value.Single(l => l.Id == h.ForeignLocationId);
        Assert.True(shared.IsDefaultUnloadingLocation);
        Assert.False(shared.IsDefaultLoadingLocation);
        // Still listed: the single-owner legacy address without any link row.
        Assert.Contains(list.Value, l => l.Id == h.OwnLocationId);

        var submitted = await sut.SubmitOrderAsync(Request(h, loadingLocationId: h.ForeignLocationId), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, submitted.Outcome);
    }

    [Fact]
    public async Task Locations_AnInactiveLinkDoesNotGrantAccess()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.CustomerLocationLinks.Add(new CustomerLocationLink
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId, LocationId = h.ForeignLocationId,
            Role = CustomerLocationRole.Both, IsActive = false,
        });
        await h.Db.Context.SaveChangesAsync();
        var sut = h.For(h.PortalUserId);

        Assert.Single((await sut.ListMyLocationsAsync(CancellationToken.None)).Value!);
        var refused = await sut.SubmitOrderAsync(Request(h, loadingLocationId: h.ForeignLocationId), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.ValidationFailed, refused.Outcome);
    }

    [Fact]
    public async Task NotificationPreferences_DefaultsThenRoundTrip_FilteringUnknownKinds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.PortalUserId);

        // No profile yet: sensible defaults, full kind catalog exposed.
        var defaults = await sut.GetNotificationPreferencesAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, defaults.Outcome);
        Assert.True(defaults.Value!.EmailEnabled);
        Assert.False(defaults.Value.SmsEnabled);
        Assert.Null(defaults.Value.EnabledKinds);
        Assert.NotEmpty(defaults.Value.AvailableKinds);

        var saved = await sut.SaveNotificationPreferencesAsync(new SavePortalNotificationPreferencesRequest(
            EmailEnabled: true, SmsEnabled: true, PreferredLanguage: "fr",
            EnabledKinds: [defaults.Value.AvailableKinds[0], "totally-internal-kind"]), CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.Success, saved.Outcome);
        Assert.True(saved.Value!.SmsEnabled);
        Assert.Equal("fr", saved.Value.PreferredLanguage);
        // The unknown/internal kind is silently dropped; only the customer-facing kind survives.
        var kind = Assert.Single(saved.Value.EnabledKinds!);
        Assert.Equal(defaults.Value.AvailableKinds[0], kind);

        // The stored profile is the customer's messaging profile — the same one the dispatcher sees.
        var profile = h.Db.Context.Set<TransportationService.Api.Modules.Messaging.Entities.MessagingProfile>()
            .Single(p => p.OwnerId == h.CustomerId);
        Assert.Equal(TransportationService.Api.Modules.Messaging.Entities.MessageOwnerType.Customer, profile.OwnerType);
        Assert.Equal("fr", profile.PreferredLanguage);

        var unlinked = await h.For(h.UnlinkedUserId).GetNotificationPreferencesAsync(CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, unlinked.Outcome);
    }

    [Fact]
    public async Task GetMyOrder_ShowsPodSummary_OnlyWhenCurrentAndCustomerVisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.PortalUserId);

        var submitted = await sut.SubmitOrderAsync(Request(h), CancellationToken.None);
        var orderId = submitted.Value!.Id;
        var stopId = h.Db.Context.TransportOrderStops.First(s => s.TransportOrderId == orderId).Id;

        var trip = new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "TR-POD", TripDate = new DateOnly(2026, 7, 21),
        };
        h.Db.Context.Trips.Add(trip);
        var pod = new TransportationService.Api.Modules.Pod.Entities.ProofOfDelivery
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id,
            TransportOrderId = orderId, TransportOrderStopId = stopId,
            RecipientName = "R. Ontvanger", Outcome = TransportationService.Api.Modules.Pod.Entities.PodOutcome.Complete,
            DeliveredAt = Now.UtcDateTime, IsCurrent = true, CustomerVisible = true,
        };
        h.Db.Context.ProofsOfDelivery.Add(pod);
        await h.Db.Context.SaveChangesAsync();

        var detail = await sut.GetMyOrderAsync(orderId, CancellationToken.None);
        Assert.NotNull(detail.Value!.Pod);
        Assert.Equal("R. Ontvanger", detail.Value.Pod!.RecipientName);
        Assert.Equal("Complete", detail.Value.Pod.Outcome);

        // A proof hidden from the customer disappears from the portal view entirely.
        pod.CustomerVisible = false;
        await h.Db.Context.SaveChangesAsync();
        var hidden = await sut.GetMyOrderAsync(orderId, CancellationToken.None);
        Assert.Null(hidden.Value!.Pod);
    }
}
