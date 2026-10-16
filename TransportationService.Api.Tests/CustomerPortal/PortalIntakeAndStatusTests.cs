using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>
/// M3 (portal windows + unit), M5 (customer-facing status/visibility) and M15 (dossier
/// ownership) acceptance findings.
/// </summary>
public class PortalIntakeAndStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime LoadFrom = new(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LoadTo = new(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UnloadFrom = new(2026, 7, 21, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UnloadTo = new(2026, 7, 21, 16, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid PortalUserId, Guid PlannerId, Guid LocationId) : IDisposable
    {
        public DevTenantContext Tenant => new(TenantId);

        public TransportOrderService Orders(Guid? userId)
        {
            var user = new DevCurrentUserContext(userId);
            return new TransportOrderService(Db.Context, Tenant, new AuditService(Db.Context, Tenant, user), new TestClock(Now),
                currentUser: user);
        }

        public CustomerPortalService Portal()
        {
            var user = new DevCurrentUserContext(PortalUserId);
            var audit = new AuditService(Db.Context, Tenant, user);
            return new CustomerPortalService(Db.Context, Tenant, user, Orders(PortalUserId),
                new LocationService(Db.Context, Tenant, audit, new CountryCodeValidator(Db.Context)), audit);
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var plannerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true },
            new User { Id = plannerId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Pia", LastName = "Planner", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "EIGEN-1", Name = "Magazijn Haven", Type = LocationType.CustomerLocation,
            City = "Antwerpen", CustomerId = customerId, IsActive = true,
        });
        db.Context.UnitTypes.AddRange(
            new UnitType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true },
            new UnitType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "COLLI", Name = "Colli", IsActive = true },
            new UnitType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "DRUM", Name = "Vat", IsActive = false });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);
        return new Harness(db, tenantId, customerId, portalUserId, plannerId, locationId);
    }

    private static PortalCreateOrderRequest Request(Harness h, params PortalCargoInput[] cargo) => new(
        CustomerReference: "PO-1", OrderDate: new DateOnly(2026, 7, 21), GoodsDescription: "Dranken", Remarks: null,
        Stops:
        [
            new PortalStopInput(StopType.Loading, h.LocationId, null, null, null, null, null, LoadFrom, LoadTo, null, null),
            new PortalStopInput(StopType.Unloading, null, "Eindklant", "Dorpsstraat 1", "9000", "Gent", "BE", UnloadFrom, UnloadTo, null, null),
        ],
        CargoItems: cargo.Length == 0 ? [new PortalCargoInput("Europalletten", 6, null, PackageUnitType.EuroPallet)] : cargo);

    private static async Task<Guid> SubmitAsync(Harness h, PortalCreateOrderRequest? request = null)
    {
        var result = await h.Portal().SubmitOrderAsync(request ?? Request(h), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, result.Outcome);
        return result.Value!.Id;
    }

    // ---- M3 ------------------------------------------------------------------------------

    [Fact]
    public async Task Submit_CustomerWindow_BecomesTheInitialPlanningWindow_AndStaysTheRequest()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h);

        var stops = h.Db.Context.TransportOrderStops.Where(s => s.TransportOrderId == orderId).OrderBy(s => s.Sequence).ToList();
        Assert.Equal(2, stops.Count);
        Assert.Equal(LoadFrom, stops[0].PlannedFrom);
        Assert.Equal(LoadTo, stops[0].PlannedTo);
        Assert.Equal(LoadFrom, stops[0].RequestedFrom);
        Assert.Equal(LoadTo, stops[0].RequestedTo);
        Assert.Equal(UnloadFrom, stops[1].PlannedFrom);
        Assert.Equal(UnloadTo, stops[1].RequestedTo);

        // The dossier's "planningsdatum ontbreekt" attention item no longer fires.
        var dossierId = h.Db.Context.DossierOrders.Single(l => l.TransportOrderId == orderId).DossierId;
        var issues = await new DossierReadinessService(h.Db.Context, h.Tenant).EvaluateAsync(dossierId, CancellationToken.None);
        Assert.DoesNotContain(issues, i => i.Code == "route.date_missing");
    }

    [Fact]
    public async Task Submit_WithoutAWindow_StillWarnsAboutTheMissingPlanningDate()
    {
        using var h = await SeedAsync();
        var request = Request(h) with
        {
            Stops =
            [
                new PortalStopInput(StopType.Loading, h.LocationId, null, null, null, null, null, null, null, null, null),
                new PortalStopInput(StopType.Unloading, null, "Eindklant", "Dorpsstraat 1", "9000", "Gent", "BE", null, null, null, null),
            ],
        };
        var orderId = await SubmitAsync(h, request);

        var dossierId = h.Db.Context.DossierOrders.Single(l => l.TransportOrderId == orderId).DossierId;
        var issues = await new DossierReadinessService(h.Db.Context, h.Tenant).EvaluateAsync(dossierId, CancellationToken.None);
        Assert.Contains(issues, i => i.Code == "route.date_missing");
    }

    [Fact]
    public async Task Submit_PackageType_MapsOntoTheActiveManagedUnit()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h, Request(h,
            new PortalCargoInput("Pallets", 6, null, PackageUnitType.EuroPallet),
            new PortalCargoInput("Dozen", 4, null, PackageUnitType.Colli),
            new PortalCargoInput("Vaten", 2, null, PackageUnitType.Drum),     // DRUM exists but is inactive
            new PortalCargoInput("Kratten", 3, null, PackageUnitType.Crate),  // CRATE does not exist here
            new PortalCargoInput("Los", 1, null, null)));

        var lines = h.Db.Context.CargoItems.Where(c => c.TransportOrderId == orderId).OrderBy(c => c.Sequence).ToList();
        Assert.Equal(new string?[] { "EUROPALLET", "COLLI", null, null, null }, lines.Select(l => l.QuantityUnitCode).ToArray());
        // The package type itself is kept for package generation.
        Assert.Equal(PackageUnitType.EuroPallet, lines[0].UnitType);
    }

    [Fact]
    public async Task Submit_SingleManagedUnit_SummarisesTheOrderHeader()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h);

        var order = h.Db.Context.TransportOrders.Single(o => o.Id == orderId);
        Assert.Equal("EUROPALLET", order.QuantityUnitCode);
        Assert.Equal(6m, order.Quantity);
    }

    [Theory]
    [InlineData(PackageUnitType.Package, "PARCEL")]
    [InlineData(PackageUnitType.Parcel, "PARCEL")]
    [InlineData(PackageUnitType.Colli, "COLLI")]
    [InlineData(PackageUnitType.Pallet, "PALLET")]
    [InlineData(PackageUnitType.EuroPallet, "EUROPALLET")]
    [InlineData(PackageUnitType.BlockPallet, "BLOCKPALLET")]
    [InlineData(PackageUnitType.Box, "BOX")]
    [InlineData(PackageUnitType.Crate, "CRATE")]
    [InlineData(PackageUnitType.RollContainer, "ROLLCONTAINER")]
    [InlineData(PackageUnitType.Drum, "DRUM")]
    [InlineData(PackageUnitType.Container, "CONTAINER")]
    [InlineData(PackageUnitType.Document, "DOCUMENT")]
    [InlineData(PackageUnitType.Other, "OTHER")]
    public void ManagedUnitCode_CoversEveryPackageType(PackageUnitType type, string code) =>
        Assert.Equal(code, CustomerPortalService.ManagedUnitCodeFor(type));

    // ---- M5 ------------------------------------------------------------------------------

    private async Task<Guid> AddInternalOrderAsync(Harness h, string number, TransportOrderStatus status,
        params (TransportOrderStatus From, TransportOrderStatus To)[] history)
    {
        var id = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = id, TenantId = h.TenantId, CustomerId = h.CustomerId, OrderNumber = number,
            OrderDate = new DateOnly(2026, 7, 20), Status = status,
            Stops =
            [
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, StopType = StopType.Loading, LocationName = "Depot", City = "Antwerpen" },
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 2, StopType = StopType.Unloading, LocationName = "Klant", City = "Gent" },
            ],
        });
        foreach (var (from, to) in history)
        {
            h.Db.Context.TransportOrderStatusHistories.Add(new TransportOrderStatusHistory
            {
                Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = id, FromStatus = from, ToStatus = to, ChangedAt = Now.UtcDateTime,
            });
        }

        await h.Db.Context.SaveChangesAsync();
        return id;
    }

    private static async Task AddInvoiceAsync(Harness h, Guid orderId, InvoiceStatus status, string number)
    {
        var invoiceId = Guid.NewGuid();
        h.Db.Context.Invoices.Add(new Invoice
        {
            Id = invoiceId, TenantId = h.TenantId, CustomerId = h.CustomerId, InvoiceNumber = number,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 7, InvoiceDate = new DateOnly(2026, 7, 22),
            DueDate = new DateOnly(2026, 8, 21), Status = status, Currency = "EUR",
        });
        h.Db.Context.InvoiceLines.Add(new InvoiceLine
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceId = invoiceId, TransportOrderId = orderId,
            Sequence = 1, Description = "Transport", Quantity = 1, UnitPrice = 100m, VatRatePercent = 21m,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task CustomerFacingStatus_AndVisibility_FollowTheRule()
    {
        using var h = await SeedAsync();
        var submitted = await SubmitAsync(h);
        var planned = await AddInternalOrderAsync(h, "INT-PLAN", TransportOrderStatus.Planned,
            (TransportOrderStatus.Draft, TransportOrderStatus.Confirmed), (TransportOrderStatus.Confirmed, TransportOrderStatus.Planned));
        var inProgress = await AddInternalOrderAsync(h, "INT-RUN", TransportOrderStatus.InProgress);
        var completed = await AddInternalOrderAsync(h, "INT-DONE", TransportOrderStatus.Completed);
        var onDraftInvoice = await AddInternalOrderAsync(h, "INT-DRAFTINV", TransportOrderStatus.Invoiced);
        await AddInvoiceAsync(h, onDraftInvoice, InvoiceStatus.Draft, "CONCEPT-1");
        var onSentInvoice = await AddInternalOrderAsync(h, "INT-SENT", TransportOrderStatus.Invoiced);
        await AddInvoiceAsync(h, onSentInvoice, InvoiceStatus.Sent, "2026070001");
        var cancelledAfterConfirm = await AddInternalOrderAsync(h, "INT-CANC", TransportOrderStatus.Cancelled,
            (TransportOrderStatus.Draft, TransportOrderStatus.Confirmed), (TransportOrderStatus.Confirmed, TransportOrderStatus.Cancelled));
        var internalDraft = await AddInternalOrderAsync(h, "INT-DRAFT", TransportOrderStatus.Draft);
        var cancelledDraft = await AddInternalOrderAsync(h, "INT-DRAFTCANC", TransportOrderStatus.Cancelled,
            (TransportOrderStatus.Draft, TransportOrderStatus.Cancelled));

        // A portal submission the planner rejected stays visible as Cancelled.
        var rejected = await SubmitAsync(h);
        await h.Orders(h.PlannerId).CancelAsync(rejected, "Geen capaciteit", CancellationToken.None);

        var list = (await h.Portal().ListMyOrdersAsync(CancellationToken.None)).Value!;
        var byId = list.ToDictionary(o => o.Id, o => o.Status);

        Assert.Equal(TransportOrderStatus.Submitted, byId[submitted]);
        Assert.Equal(TransportOrderStatus.Planned, byId[planned]);
        Assert.Equal(TransportOrderStatus.InProgress, byId[inProgress]);
        Assert.Equal(TransportOrderStatus.Completed, byId[completed]);
        Assert.Equal(TransportOrderStatus.Completed, byId[onDraftInvoice]); // "Afgerond", not "Gefactureerd"
        Assert.Equal(TransportOrderStatus.Invoiced, byId[onSentInvoice]);
        Assert.Equal(TransportOrderStatus.Cancelled, byId[cancelledAfterConfirm]);
        Assert.Equal(TransportOrderStatus.Cancelled, byId[rejected]);
        Assert.DoesNotContain(internalDraft, byId.Keys);
        Assert.DoesNotContain(cancelledDraft, byId.Keys);
        Assert.Equal(8, list.Count);

        // The detail tells the same story and hides internal drafts like foreign orders.
        var portal = h.Portal();
        Assert.Equal(TransportOrderStatus.Completed, (await portal.GetMyOrderAsync(onDraftInvoice, CancellationToken.None)).Value!.Status);
        Assert.Equal(TransportOrderStatus.Invoiced, (await portal.GetMyOrderAsync(onSentInvoice, CancellationToken.None)).Value!.Status);
        Assert.Equal(PortalOutcomeKind.NotFound, (await portal.GetMyOrderAsync(internalDraft, CancellationToken.None)).Outcome);
        Assert.Equal(PortalOutcomeKind.NotFound, (await portal.GetMyOrderAsync(cancelledDraft, CancellationToken.None)).Outcome);

        // Internal state is untouched.
        Assert.Equal(TransportOrderStatus.Invoiced, h.Db.Context.TransportOrders.Single(o => o.Id == onDraftInvoice).Status);
    }

    [Fact]
    public async Task MappingRule_IsPure()
    {
        Assert.Null(PortalOrderStatusRules.CustomerFacing(TransportOrderStatus.Draft, false, false, false));
        Assert.Equal(TransportOrderStatus.Submitted, PortalOrderStatusRules.CustomerFacing(TransportOrderStatus.Draft, true, false, false));
        Assert.Null(PortalOrderStatusRules.CustomerFacing(TransportOrderStatus.Cancelled, false, false, false));
        Assert.Equal(TransportOrderStatus.Cancelled, PortalOrderStatusRules.CustomerFacing(TransportOrderStatus.Cancelled, false, true, false));
        Assert.Equal(TransportOrderStatus.Completed, PortalOrderStatusRules.CustomerFacing(TransportOrderStatus.Invoiced, false, true, false));
        Assert.Equal(TransportOrderStatus.Invoiced, PortalOrderStatusRules.CustomerFacing(TransportOrderStatus.Invoiced, false, true, true));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ExpectedDelivery_IsHidden_OnceDelivered()
    {
        using var h = await SeedAsync();
        var orderId = await SubmitAsync(h);
        var unloadingStopId = h.Db.Context.TransportOrderStops
            .Single(s => s.TransportOrderId == orderId && s.StopType == StopType.Unloading).Id;
        var tripId = Guid.NewGuid();
        h.Db.Context.Trips.Add(new TransportationService.Api.Modules.Planning.Entities.Trip
        {
            Id = tripId, TenantId = h.TenantId, TripNumber = "TR-ETA", TripDate = new DateOnly(2026, 7, 21),
        });
        h.Db.Context.StopEtas.Add(new TransportationService.Api.Modules.Eta.Entities.StopEta
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = tripId, TransportOrderStopId = unloadingStopId, CurrentEta = UnloadFrom,
        });
        await h.Db.Context.SaveChangesAsync();

        var active = await h.Portal().GetMyOrderAsync(orderId, CancellationToken.None);
        Assert.Equal(UnloadFrom, active.Value!.ExpectedDeliveryEta);

        var order = h.Db.Context.TransportOrders.Single(o => o.Id == orderId);
        order.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();

        var delivered = await h.Portal().GetMyOrderAsync(orderId, CancellationToken.None);
        Assert.Equal(TransportOrderStatus.Completed, delivered.Value!.Status);
        Assert.Null(delivered.Value.ExpectedDeliveryEta);
    }

    // ---- M15 -----------------------------------------------------------------------------

    [Fact]
    public async Task InternalCreate_MakesTheCreatorResponsible_PortalIntakeStaysUnownedUntilAccepted()
    {
        using var h = await SeedAsync();

        // Internal order create → wrapper dossier owned by the planner.
        var internalOrder = await h.Orders(h.PlannerId).CreateAsync(new CreateTransportOrderRequest(
            h.CustomerId, null, new DateOnly(2026, 7, 21), "Intern", null, null, null, null, null, false, false, null, null,
            [
                new TransportOrderStopInput(StopType.Loading, h.LocationId, null, null, null, null, null, LoadFrom, LoadTo, null, null),
                new TransportOrderStopInput(StopType.Unloading, null, "Eindklant", "Dorpsstraat 1", "9000", "Gent", "BE", null, null, null, null),
            ]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, internalOrder.Outcome);
        Assert.Equal(h.PlannerId, DossierOf(h, internalOrder.Order!.Id).ResponsibleUserId);

        // Portal intake → no owner.
        var portalOrder = await SubmitAsync(h);
        Assert.Null(DossierOf(h, portalOrder).ResponsibleUserId);

        // Accepting it makes the accepting planner the owner.
        var planner = new DevCurrentUserContext(h.PlannerId);
        var audit = new AuditService(h.Db.Context, h.Tenant, planner);
        var review = new OrderPortalReviewService(h.Db.Context, h.Tenant, h.Orders(h.PlannerId),
            new CustomerMessageService(h.Db.Context, h.Tenant, planner, audit, new TestClock(Now)), audit,
            currentUser: planner);
        var accepted = await review.ReviewAsync(portalOrder, new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, accepted.Outcome);
        Assert.Equal(h.PlannerId, DossierOf(h, portalOrder).ResponsibleUserId);
    }

    [Fact]
    public async Task AcceptingDoesNotOverrideAnExistingOwner()
    {
        using var h = await SeedAsync();
        var portalOrder = await SubmitAsync(h);
        var otherPlanner = Guid.NewGuid();
        h.Db.Context.Users.Add(new User { Id = otherPlanner, TenantId = h.TenantId, Email = "o@acme.be", FirstName = "O", LastName = "Ander", IsActive = true });
        DossierOf(h, portalOrder).ResponsibleUserId = otherPlanner;
        await h.Db.Context.SaveChangesAsync();

        var planner = new DevCurrentUserContext(h.PlannerId);
        var audit = new AuditService(h.Db.Context, h.Tenant, planner);
        var review = new OrderPortalReviewService(h.Db.Context, h.Tenant, h.Orders(h.PlannerId),
            new CustomerMessageService(h.Db.Context, h.Tenant, planner, audit, new TestClock(Now)), audit,
            currentUser: planner);
        await review.ReviewAsync(portalOrder, new PortalReviewRequest(PortalReviewAction.Accept, null), CancellationToken.None);

        Assert.Equal(otherPlanner, DossierOf(h, portalOrder).ResponsibleUserId);
    }

    [Fact]
    public async Task DossierCreate_DefaultsTheResponsibleToTheCreatingInternalUser()
    {
        using var h = await SeedAsync();
        var planner = new DevCurrentUserContext(h.PlannerId);
        var sut = new DossierService(h.Db.Context, h.Tenant, new AuditService(h.Db.Context, h.Tenant, planner), new TestClock(Now),
            currentUser: planner);

        var defaulted = await sut.CreateAsync(new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None);
        Assert.Equal(h.PlannerId, defaulted.ResponsibleUserId);

        // An explicit choice wins; the "Mijn open" filter finds the defaulted one.
        var colleague = Guid.NewGuid();
        h.Db.Context.Users.Add(new User { Id = colleague, TenantId = h.TenantId, Email = "c@acme.be", FirstName = "C", LastName = "Collega", IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        var explicitChoice = await sut.CreateAsync(new SaveDossierRequest(CustomerId: h.CustomerId, ResponsibleUserId: colleague), CancellationToken.None);
        Assert.Equal(colleague, explicitChoice.ResponsibleUserId);

        // A customer-portal user never becomes an owner.
        var portalUser = new DevCurrentUserContext(h.PortalUserId);
        var asPortal = new DossierService(h.Db.Context, h.Tenant, new AuditService(h.Db.Context, h.Tenant, portalUser), new TestClock(Now),
            currentUser: portalUser);
        Assert.Null((await asPortal.CreateAsync(new SaveDossierRequest(CustomerId: h.CustomerId), CancellationToken.None)).ResponsibleUserId);
    }

    private static TransportationService.Api.Modules.Dossiers.Entities.TransportDossier DossierOf(Harness h, Guid orderId)
    {
        var dossierId = h.Db.Context.DossierOrders.AsNoTracking().Single(l => l.TransportOrderId == orderId).DossierId;
        var dossier = h.Db.Context.TransportDossiers.Single(d => d.Id == dossierId);
        h.Db.Context.Entry(dossier).Reload();
        return dossier;
    }
}
