using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tarification.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Incidents;

/// <summary>
/// Follow-up wave P4+P5: the failed-delivery automation (idempotent incident per stop, mode
/// Propose/Automatic, next-working-day dating) and the configurable charge policy (Never |
/// Propose | Auto, most-specific-first, customer-fault invariant untouched).
/// </summary>
public class FailedDeliveryAndChargePolicyTests
{
    // A Wednesday: next working day = Thursday; a Friday failure lands on Monday.
    private static readonly DateTimeOffset Now = new(2026, 08, 12, 17, 0, 0, TimeSpan.Zero);

    private sealed class PermissionSet : IPermissionAuthorizationService
    {
        public HashSet<string> Codes { get; } = new();
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Contains(permissionCode));
    }

    private sealed record Harness(
        SqliteTestDbContext Db, IncidentService Incidents, FailedDeliveryService Sut, PermissionSet Permissions,
        Guid TenantId, Guid CustomerId, Guid OrderId, Guid StopId, Guid TripId, TenantSettings Settings);

    private static async Task<Harness> SeedAsync(string redeliveryMode = "Manual", DateTimeOffset? now = null)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var stopId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        var settings = new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 2,
            RedeliveryMode = redeliveryMode,
        };
        db.Context.TenantSettings.Add(settings);
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "d@acme.be", FirstName = "Dana", LastName = "Dispatch", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV", IsActive = true });
        var order = new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = new DateOnly(2026, 8, 10), Status = TransportOrderStatus.InProgress,
            GoodsDescription = "20 paletten", AgreedPrice = 500m,
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = stopId, TenantId = tenantId, Sequence = 1, StopType = StopType.Unloading,
            City = "Hasselt", CountryCode = "BE", LocationName = "Klant magazijn",
        });
        db.Context.TransportOrders.Add(order);
        db.Context.Trips.Add(new Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = tenantId, TripNumber = "RIT-1", TripDate = new DateOnly(2026, 8, 12),
        });
        var dossierId = Guid.NewGuid();
        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-0001", CustomerId = customerId,
            OriginTransportOrderId = orderId,
        });
        db.Context.DossierOrders.Add(new DossierOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId, TransportOrderId = orderId,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var clock = new TestClock(now ?? Now);
        var permissions = new PermissionSet();
        var audit = new AuditService(db.Context, tenant, user);
        var incidents = new IncidentService(db.Context, tenant, audit,
            new NotificationService(db.Context, tenant, user, clock), clock, permissions, user);
        var sut = new FailedDeliveryService(db.Context, tenant, incidents, audit, clock);
        return new Harness(db, incidents, sut, permissions, tenantId, customerId, orderId, stopId, tripId, settings);
    }

    [Fact]
    public void NextWorkingDay_SkipsWeekendsAndHolidays()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 8, 17) }; // Monday = holiday
        // Friday 14th → skips Sat/Sun and the Monday holiday → Tuesday 18th.
        Assert.Equal(new DateOnly(2026, 8, 18),
            BusinessDayCalculator.NextWorkingDay(new DateOnly(2026, 8, 14), holidays));
        // Wednesday 12th → Thursday 13th.
        Assert.Equal(new DateOnly(2026, 8, 13),
            BusinessDayCalculator.NextWorkingDay(new DateOnly(2026, 8, 12), new HashSet<DateOnly>()));
    }

    [Fact]
    public async Task StopFailure_CreatesOneIncident_ReplaysAreNoOps()
    {
        var h = await SeedAsync("Manual");
        using var _ = h.Db;

        await h.Sut.HandleStopFailureAsync(h.TripId, h.StopId, "Klant gesloten", CancellationToken.None);
        await h.Sut.HandleStopFailureAsync(h.TripId, h.StopId, "Klant gesloten", CancellationToken.None);

        var incident = Assert.Single(h.Db.Context.Incidents.ToList());
        Assert.Equal(h.StopId, incident.SourceStopId);
        Assert.Equal(h.OrderId, incident.TransportOrderId);
        Assert.NotNull(incident.DossierId);
        Assert.Contains("Klant gesloten", incident.Description);
        Assert.False(incident.RedeliverySuggested); // Manual mode: incident only
        Assert.Null(incident.LinkedRedeliveryOrderId);
    }

    [Fact]
    public async Task ProposeMode_FlagsTheRedeliveryRecommendation()
    {
        var h = await SeedAsync("Propose");
        using var _ = h.Db;

        await h.Sut.HandleStopFailureAsync(h.TripId, h.StopId, "Klant gesloten", CancellationToken.None);

        var incident = Assert.Single(h.Db.Context.Incidents.ToList());
        Assert.True(incident.RedeliverySuggested);
        Assert.Null(incident.LinkedRedeliveryOrderId);
    }

    [Fact]
    public async Task AutomaticMode_CreatesTheRedelivery_OnTheNextWorkingDay()
    {
        // Friday 2026-08-14: the redelivery must land on Monday 17th — unless that Monday
        // is a tenant holiday, then Tuesday 18th.
        var friday = new DateTimeOffset(2026, 08, 14, 17, 0, 0, TimeSpan.Zero);
        var h = await SeedAsync("Automatic", friday);
        using var _ = h.Db;
        h.Db.Context.TenantHolidays.Add(new TenantHoliday
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Date = new DateOnly(2026, 8, 17), Name = "Feestdag",
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.HandleStopFailureAsync(h.TripId, h.StopId, "Klant gesloten", CancellationToken.None);

        var incident = Assert.Single(h.Db.Context.Incidents.ToList());
        Assert.NotNull(incident.LinkedRedeliveryOrderId);
        var redelivery = h.Db.Context.TransportOrders.Single(o => o.Id == incident.LinkedRedeliveryOrderId);
        Assert.Equal(new DateOnly(2026, 8, 18), redelivery.OrderDate);
        Assert.Equal(TransportOrderStatus.Draft, redelivery.Status);
        Assert.Equal($"HERLEVERING ORD-0001", redelivery.CustomerReference);
        // Same dossier as the original.
        Assert.Equal(2, h.Db.Context.DossierOrders.Count());

        // Replay: no second incident, no second redelivery.
        await h.Sut.HandleStopFailureAsync(h.TripId, h.StopId, "Klant gesloten", CancellationToken.None);
        Assert.Single(h.Db.Context.Incidents.ToList());
    }

    // --- P5: charge policy ---

    private static SaveIncidentRequest Request(Harness h, string responsibleParty) => new(
        "Pallet geweigerd", "Klant weigerde de levering.", "CustomerComplaint", "Medium",
        CustomerId: h.CustomerId, TransportOrderId: h.OrderId, ResponsibleParty: responsibleParty);

    [Fact]
    public async Task NeverPolicy_BlocksProposing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.IncidentChargePolicies.Add(new IncidentChargePolicy
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Mode = "Never",
        });
        await h.Db.Context.SaveChangesAsync();

        var incident = await h.Incidents.CreateAsync(Request(h, "Customer"), CancellationToken.None);
        Assert.Equal("None", incident.ChargeDecision); // policy Never: nothing auto-proposed

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Incidents.ProposeChargeAsync(incident.Id,
                new ProposeIncidentChargeRequest(100m, "Toch proberen"), CancellationToken.None));
    }

    [Fact]
    public async Task ProposePolicy_AutoProposesOnCustomerResponsibility()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.IncidentChargePolicies.Add(new IncidentChargePolicy
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Mode = "Propose",
            DefaultAmount = 75m, DefaultDescription = "Herleveringskost",
        });
        await h.Db.Context.SaveChangesAsync();

        var incident = await h.Incidents.CreateAsync(Request(h, "Customer"), CancellationToken.None);

        Assert.Equal("Proposed", incident.ChargeDecision);
        Assert.Equal(75m, incident.ChargeAmount);
        Assert.Equal("Herleveringskost", incident.ChargeDescription);
    }

    [Fact]
    public async Task AutoPolicy_ApprovesAndCreatesTheLine_ButNeverForOwnFault()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.IncidentChargePolicies.Add(new IncidentChargePolicy
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Mode = "Auto",
            DefaultAmount = 120m, DefaultDescription = "Automatische doorrekening",
        });
        await h.Db.Context.SaveChangesAsync();

        // Own fault: policy must NOT fire — the invariant sits above configuration.
        var ownFault = await h.Incidents.CreateAsync(Request(h, "Own"), CancellationToken.None);
        Assert.Equal("None", ownFault.ChargeDecision);
        Assert.Empty(h.Db.Context.TransportOrderPricingLines.ToList());

        // Customer fault: auto-approved, line lands on the order.
        var customerFault = await h.Incidents.CreateAsync(Request(h, "Customer"), CancellationToken.None);
        Assert.Equal("Approved", customerFault.ChargeDecision);
        var line = Assert.Single(h.Db.Context.TransportOrderPricingLines.ToList());
        Assert.Equal(120m, line.Amount);
        var order = h.Db.Context.TransportOrders.Single(o => o.Id == h.OrderId);
        Assert.Equal(620m, order.AgreedPrice); // 500 + 120
    }

    [Fact]
    public async Task PolicyResolution_MostSpecificWins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.IncidentChargePolicies.AddRange(
            new IncidentChargePolicy { Id = Guid.NewGuid(), TenantId = h.TenantId, Mode = "Never" },
            new IncidentChargePolicy
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
                IncidentType = "CustomerComplaint", Mode = "Propose", DefaultAmount = 50m,
            });
        await h.Db.Context.SaveChangesAsync();

        // The customer+type policy beats the tenant-wide Never.
        var incident = await h.Incidents.CreateAsync(Request(h, "Customer"), CancellationToken.None);
        Assert.Equal("Proposed", incident.ChargeDecision);
        Assert.Equal(50m, incident.ChargeAmount);
    }
}
