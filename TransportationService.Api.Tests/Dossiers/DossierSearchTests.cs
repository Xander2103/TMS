using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Dossier search sprint 2026-09-23 — one server-side, tenant-scoped, paged, sortable query
/// behind the dossier list: global search over dossier/order/customer/driver/plate/address
/// fields, primary + advanced filters that combine with AND, stable ordering.
/// </summary>
public class DossierSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 23, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerVcb, Guid CustomerOther,
        Guid DossierLive, Guid DossierManual, Guid DossierAuto, Guid DossierCancelled, Guid DossierOther, Guid DriverJan, Guid VehicleId, Guid TypeTransport, Guid TypePlateau)
    {
        public DossierService Sut(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierService(Db.Context, tenant, new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }

        public async Task<PagedResult<DossierListItemDto>> Search(DossierSearchQuery query, Guid? tenantId = null) =>
            await Sut(tenantId).SearchAsync(query, CancellationToken.None);

        public async Task<List<string>> Numbers(DossierSearchQuery query) => (await Search(query)).Items.Select(i => i.DossierNumber).ToList();
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var t = Guid.NewGuid();
        var vcb = Guid.NewGuid();
        var other = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var driverJan = Guid.NewGuid();
        var vehicle = Guid.NewGuid();
        var typeTransport = Guid.NewGuid();
        var typePlateau = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = t, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = vcb, TenantId = t, CustomerNumber = "KL-100", Name = "Van Caudenberg BV", IsActive = true },
            new Customer { Id = other, TenantId = t, CustomerNumber = "KL-200", Name = "Andere NV", IsActive = true });
        db.Context.Employees.Add(new Employee { Id = employee, TenantId = t, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Peeters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Context.Drivers.Add(new Driver { Id = driverJan, TenantId = t, DriverNumber = "CH-1", EmployeeId = employee, IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicle, TenantId = t, InternalNumber = "VRT-7", LicensePlate = "1-ABC-123", IsActive = true });
        db.Context.ActivityTypes.AddRange(
            new ActivityType { Id = typeTransport, TenantId = t, Code = "DIRECT_TRANSPORT", Name = "Direct transport", IsActive = true, HasStops = true, PlanningRelevant = true, IsBillable = true },
            new ActivityType { Id = typePlateau, TenantId = t, Code = "PLATEAU", Name = "Plateau", IsActive = true, HasStops = false, PlanningRelevant = true, IsBillable = true });

        TransportDossier Dossier(string number, Guid customer, DossierStatus status, DateOnly date, DateTime? confirmedAt = null,
            DossierConfirmationSource? source = null, string? reference = null, DateTime? created = null) => new()
        {
            Id = Guid.NewGuid(), TenantId = t, DossierNumber = number, Title = number, CustomerId = customer, Status = status,
            DossierDate = date, ClosedAt = confirmedAt, ConfirmationSource = source, CustomerReference = reference,
            CreatedAt = created ?? Now.UtcDateTime,
        };
        var live = Dossier("DOS-0001", vcb, DossierStatus.Open, new(2026, 9, 10), reference: "PO-4711");
        var manual = Dossier("DOS-0002", vcb, DossierStatus.Closed, new(2026, 9, 12), new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), DossierConfirmationSource.Manual, "PO-4712");
        var auto = Dossier("DOS-0003", vcb, DossierStatus.Closed, new(2026, 8, 20), new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc), DossierConfirmationSource.Automatic);
        var cancelled = Dossier("DOS-0004", other, DossierStatus.Cancelled, new(2026, 9, 1));
        var otherOpen = Dossier("DOS-0005", other, DossierStatus.Open, new(2026, 9, 22), reference: "REF/42 (b)");
        db.Context.TransportDossiers.AddRange(live, manual, auto, cancelled, otherOpen);

        // Orders: live → ORD-0100 (Antwerpen 2000 → Gent 9000, on Jan's trip 2026-09-11, priced, draft invoice);
        // manual → ORD-0200 (Hasselt → Luik, no trip, unpriced); auto → ORD-0300 (Brussel → Lille FR, sent invoice).
        (Guid Id, string Number) Order(TransportDossier d, string number, TransportOrderStatus status, decimal? agreed, string loadCity, string loadPostal, string unloadCity, string unloadPostal, string unloadCountry, Guid type)
        {
            var id = Guid.NewGuid();
            db.Context.TransportOrders.Add(new TransportOrder { Id = id, TenantId = t, CustomerId = d.CustomerId!.Value, OrderNumber = number, OrderDate = d.DossierDate!.Value, Status = status, AgreedPrice = agreed, PriceIsManual = agreed is not null, CustomerReference = d.CustomerReference });
            db.Context.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = t, DossierId = d.Id, TransportOrderId = id });
            db.Context.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = t, DossierId = d.Id, ActivityTypeId = type, Sequence = 1, LinkedTransportOrderId = id });
            db.Context.TransportOrderStops.AddRange(
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = t, TransportOrderId = id, Sequence = 1, StopType = StopType.Loading, LocationName = loadCity, City = loadCity, PostalCode = loadPostal, CountryCode = "BE" },
                new TransportOrderStop { Id = Guid.NewGuid(), TenantId = t, TransportOrderId = id, Sequence = 2, StopType = StopType.Unloading, LocationName = unloadCity, City = unloadCity, PostalCode = unloadPostal, CountryCode = unloadCountry });
            return (id, number);
        }
        var o100 = Order(live, "ORD-0100", TransportOrderStatus.Planned, 450m, "Antwerpen", "2000", "Gent", "9000", "BE", typeTransport);
        var o200 = Order(manual, "ORD-0200", TransportOrderStatus.Completed, null, "Hasselt", "3500", "Luik", "4000", "BE", typeTransport);
        var o300 = Order(auto, "ORD-0300", TransportOrderStatus.Invoiced, 900m, "Brussel", "1000", "Lille", "59000", "FR", typeTransport);
        // Plateau activity on the auto dossier (activity-type filter), planned 2026-08-24.
        db.Context.DossierActivities.Add(new DossierActivity { Id = Guid.NewGuid(), TenantId = t, DossierId = auto.Id, ActivityTypeId = typePlateau, Sequence = 2, PlannedDate = new(2026, 8, 24) });

        var trip = Guid.NewGuid();
        db.Context.Trips.Add(new Trip { Id = trip, TenantId = t, TripNumber = "RIT-1", TripDate = new(2026, 9, 11), DriverId = driverJan, VehicleId = vehicle, Status = TripStatus.Planned });
        db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = t, TripId = trip, TransportOrderId = o100.Id, Sequence = 1 });

        var draftInvoice = Guid.NewGuid();
        var sentInvoice = Guid.NewGuid();
        db.Context.Invoices.AddRange(
            new Invoice { Id = draftInvoice, TenantId = t, CustomerId = vcb, InvoiceNumber = "FAC-1", InvoiceDate = new(2026, 9, 20), DueDate = new(2026, 10, 20), Status = InvoiceStatus.Draft },
            new Invoice { Id = sentInvoice, TenantId = t, CustomerId = vcb, InvoiceNumber = "FAC-2", InvoiceDate = new(2026, 8, 30), DueDate = new(2026, 9, 30), Status = InvoiceStatus.Sent });
        db.Context.InvoiceLines.AddRange(
            new InvoiceLine { Id = Guid.NewGuid(), TenantId = t, InvoiceId = draftInvoice, TransportOrderId = o100.Id, Sequence = 1, Description = "x", Quantity = 1, UnitPrice = 450, VatRatePercent = 21 },
            new InvoiceLine { Id = Guid.NewGuid(), TenantId = t, InvoiceId = sentInvoice, TransportOrderId = o300.Id, Sequence = 1, Description = "y", Quantity = 1, UnitPrice = 900, VatRatePercent = 21 });
        db.Context.TransportOrderDocuments.Add(new TransportOrderDocument { Id = Guid.NewGuid(), TenantId = t, TransportOrderId = o300.Id, DossierId = auto.Id, DocumentType = TransportOrderDocumentType.Cmr, Title = "CMR", DocumentPath = "x/cmr.pdf" });

        // Another tenant with a look-alike dossier: must never leak.
        var foreign = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = foreign, Name = "F", Slug = "f", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TransportDossiers.Add(new TransportDossier { Id = Guid.NewGuid(), TenantId = foreign, DossierNumber = "DOS-0001", Title = "Elders", Status = DossierStatus.Open, DossierDate = new(2026, 9, 10) });
        await db.Context.SaveChangesAsync();

        return new Harness(db, t, vcb, other, live.Id, manual.Id, auto.Id, cancelled.Id, otherOpen.Id, driverJan, vehicle, typeTransport, typePlateau);
    }

    private static DossierSearchQuery Q(string? search = null, string? status = null, Guid? customerId = null) => new() { Search = search, Status = status, CustomerId = customerId };

    [Fact]
    public async Task Default_ListsEveryStatus_OfThisTenantOnly_NewestDossierDateFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var page = await h.Search(Q());

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["DOS-0005", "DOS-0002", "DOS-0001", "DOS-0004", "DOS-0003"], page.Items.Select(i => i.DossierNumber));
        Assert.All(page.Items, i => Assert.NotEqual("Elders", i.Title));
        Assert.Empty((await h.Search(Q(), tenantId: Guid.NewGuid())).Items);
    }

    [Fact]
    public async Task GlobalSearch_HitsDossierOrderCustomerReferenceDriverPlateAndAddressFields()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Equal(["DOS-0002"], await h.Numbers(Q("dos-0002")));
        Assert.Equal(["DOS-0001"], await h.Numbers(Q("ORD-0100")));
        Assert.Equal(3, (await h.Search(Q("caudenberg"))).TotalCount);
        Assert.Equal(2, (await h.Search(Q("KL-200"))).TotalCount);
        Assert.Equal(["DOS-0002"], await h.Numbers(Q("PO-4712")));
        Assert.Equal(["DOS-0001"], await h.Numbers(Q("peeters")));
        Assert.Equal(["DOS-0001"], await h.Numbers(Q("1-ABC-123")));
        Assert.Equal(["DOS-0003"], await h.Numbers(Q("lille")));
        Assert.Equal(["DOS-0002"], await h.Numbers(Q("3500")));
        Assert.Equal(["DOS-0005"], await h.Numbers(Q("REF/42 (b)")));
        Assert.Empty(await h.Numbers(Q("bestaat-niet")));
        Assert.Empty(await h.Numbers(Q("%_'\"")));
    }

    [Fact]
    public async Task StatusAndConfirmationFilters()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Equal(["DOS-0005", "DOS-0001"], await h.Numbers(Q(status: "Open")));
        Assert.Equal(["DOS-0002", "DOS-0003"], await h.Numbers(Q(status: "Closed")));
        Assert.Equal(["DOS-0004"], await h.Numbers(Q(status: "Cancelled")));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { ConfirmationSource = "Manual" }));
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { ConfirmationSource = "Automatic" }));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { ConfirmedFrom = new(2026, 9, 1), ConfirmedTo = new(2026, 9, 30) }));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Search(Q(status: "Weird")));
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Search(new DossierSearchQuery { ConfirmedFrom = new(2026, 9, 30), ConfirmedTo = new(2026, 9, 1) }));
    }

    [Fact]
    public async Task IdentificationCustomerAndDateFilters()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { DossierNumber = "0001" }));
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { OrderNumber = "ORD-0300" }));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { CustomerReference = "4712" }));
        Assert.Equal(2, (await h.Search(new DossierSearchQuery { CustomerNumber = "KL-200" })).TotalCount);
        Assert.Equal(3, (await h.Search(Q(customerId: h.CustomerVcb))).TotalCount);
        Assert.Equal(["DOS-0002", "DOS-0001"], await h.Numbers(new DossierSearchQuery { DateFrom = new(2026, 9, 5), DateTo = new(2026, 9, 15) }));
        Assert.Equal(5, (await h.Search(new DossierSearchQuery { CreatedFrom = new(2026, 9, 23), CreatedTo = new(2026, 9, 23) })).TotalCount);
    }

    [Fact]
    public async Task PlanningTransportAndCommercialFilters()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { DriverId = h.DriverJan }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { VehicleId = h.VehicleId }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { LicensePlate = "abc-123" }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { PlanningFrom = new(2026, 9, 11), PlanningTo = new(2026, 9, 11) }));
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { PlanningFrom = new(2026, 8, 24), PlanningTo = new(2026, 8, 24) }));
        Assert.Equal(3, (await h.Search(new DossierSearchQuery { ActivityTypeId = h.TypeTransport })).TotalCount);
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { ActivityTypeId = h.TypePlateau }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { LoadingCity = "antwerp" }));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { UnloadingCity = "Luik" }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { PostalCode = "9000" }));
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { CountryCode = "fr" }));
        Assert.Equal(["DOS-0001", "DOS-0003"], await h.Numbers(new DossierSearchQuery { PriceStatus = "Priced" }));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { PriceStatus = "Unpriced", Status = "Closed" }));
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { InvoiceStatus = "Sent" }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { InvoiceStatus = "Draft" }));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { InvoiceStatus = "NotInvoiced", Status = "Closed" }));
        Assert.Equal(["DOS-0003"], await h.Numbers(new DossierSearchQuery { HasCmr = true }));
        Assert.Equal(["DOS-0002"], await h.Numbers(new DossierSearchQuery { HasCmr = false, Status = "Closed" }));
    }

    [Fact]
    public async Task FiltersCombineWithAnd_AcceptanceFlowC()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var query = new DossierSearchQuery
        {
            CustomerId = h.CustomerVcb, Status = "Closed", ConfirmedFrom = new(2026, 9, 1), ConfirmedTo = new(2026, 9, 30), ActivityTypeId = h.TypeTransport,
        };
        Assert.Equal(["DOS-0002"], await h.Numbers(query));
        // Adding the driver filter (Jan only drove DOS-0001) narrows to nothing — combined, never OR-ed.
        Assert.Empty(await h.Numbers(query with { DriverId = h.DriverJan }));
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { CustomerId = h.CustomerVcb, Status = "Open", DriverId = h.DriverJan, Search = "gent" }));
    }

    [Fact]
    public async Task SortingAndPagination_AreStable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Equal(["DOS-0001", "DOS-0002", "DOS-0003", "DOS-0004", "DOS-0005"], await h.Numbers(new DossierSearchQuery { Sort = "number", Dir = "asc" }));
        Assert.Equal(["DOS-0004", "DOS-0005", "DOS-0001", "DOS-0002", "DOS-0003"], await h.Numbers(new DossierSearchQuery { Sort = "customer", Dir = "asc" }));
        Assert.Equal(["DOS-0002", "DOS-0003", "DOS-0001", "DOS-0004", "DOS-0005"], await h.Numbers(new DossierSearchQuery { Sort = "confirmedAt", Dir = "desc" }));
        Assert.Equal(["DOS-0004", "DOS-0002", "DOS-0003", "DOS-0001", "DOS-0005"], await h.Numbers(new DossierSearchQuery { Sort = "status", Dir = "asc" }));
        Assert.Equal(["DOS-0001", "DOS-0003", "DOS-0002", "DOS-0004", "DOS-0005"], await h.Numbers(new DossierSearchQuery { Sort = "planningDate", Dir = "desc" }));

        var page1 = await h.Search(new DossierSearchQuery { Sort = "number", Dir = "asc", Page = 1, PageSize = 2 });
        var page2 = await h.Search(new DossierSearchQuery { Sort = "number", Dir = "asc", Page = 2, PageSize = 2 });
        var page3 = await h.Search(new DossierSearchQuery { Sort = "number", Dir = "asc", Page = 3, PageSize = 2 });
        Assert.Equal((5, 2, 1), (page1.TotalCount, page1.Items.Count, page1.Page));
        Assert.Equal(["DOS-0001", "DOS-0002"], page1.Items.Select(i => i.DossierNumber));
        Assert.Equal(["DOS-0003", "DOS-0004"], page2.Items.Select(i => i.DossierNumber));
        Assert.Equal(["DOS-0005"], page3.Items.Select(i => i.DossierNumber));
        // An unknown sort key falls back to the default order; the page size is bounded.
        Assert.Equal(5, (await h.Search(new DossierSearchQuery { Sort = "drop table", PageSize = 5000 })).Items.Count);
        Assert.Equal(PageRequest.MaxPageSize, (await h.Search(new DossierSearchQuery { PageSize = 5000 })).PageSize);
    }

    [Fact]
    public async Task ListColumns_CarryConfirmationTransportAndAssignmentSummaries()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var items = (await h.Search(Q())).Items.ToDictionary(i => i.DossierNumber);

        var live = items["DOS-0001"];
        Assert.Equal((new DateOnly(2026, 9, 10), "PO-4711", "Antwerpen", "Gent", "Jan Peeters", "1-ABC-123", new DateOnly(2026, 9, 11), "Draft"),
            (live.DossierDate, live.CustomerReference, live.FirstLoadingCity, live.LastUnloadingCity, live.DriverSummary, live.VehicleSummary, live.PlanningDate, live.InvoiceStatus));
        Assert.Null(live.ConfirmedAt);

        var manual = items["DOS-0002"];
        Assert.Equal(("Closed", "Manual", "NotInvoiced"), (manual.Status, manual.ConfirmationSource, manual.InvoiceStatus));
        Assert.Equal(new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc), manual.ConfirmedAt);
        Assert.Null(manual.DriverSummary);

        var auto = items["DOS-0003"];
        Assert.Equal(("Automatic", "Sent", 2), (auto.ConfirmationSource, auto.InvoiceStatus, auto.ActivityCount));
        Assert.True(auto.HasCmr);
        Assert.Equal(new DateOnly(2026, 8, 24), auto.PlanningDate);
    }

    [Fact]
    public async Task TwoTripsWithDifferentDrivers_AreSummarised_NotOneArbitraryDriver()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee2 = Guid.NewGuid();
        var driver2 = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee { Id = employee2, TenantId = h.TenantId, EmployeeNumber = "MED-2", FirstName = "An", LastName = "Maes", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        h.Db.Context.Drivers.Add(new Driver { Id = driver2, TenantId = h.TenantId, DriverNumber = "CH-2", EmployeeId = employee2, IsActive = true });
        var orderId = (await h.Db.Context.DossierOrders.AsNoTracking().SingleAsync(l => l.DossierId == h.DossierLive)).TransportOrderId;
        var trip2 = Guid.NewGuid();
        h.Db.Context.Trips.Add(new Trip { Id = trip2, TenantId = h.TenantId, TripNumber = "RIT-2", TripDate = new(2026, 9, 12), DriverId = driver2, Status = TripStatus.Planned });
        h.Db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip2, TransportOrderId = orderId, Sequence = 1 });
        await h.Db.Context.SaveChangesAsync();

        var live = (await h.Search(Q())).Items.Single(i => i.DossierNumber == "DOS-0001");

        Assert.Equal("2 chauffeurs", live.DriverSummary);
        Assert.Equal(new DateOnly(2026, 9, 11), live.PlanningDate);
        Assert.Equal(["DOS-0001"], await h.Numbers(new DossierSearchQuery { DriverId = driver2 }));
    }
}
