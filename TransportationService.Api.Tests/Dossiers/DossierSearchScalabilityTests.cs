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
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// Performance final 2026-10-21 (PERF-BLOCKER 1): the dossier search was rewritten from per-row
/// correlated OR-of-EXISTS predicates to uncorrelated id-set subqueries and one LEFT JOINed
/// planning set. These tests pin the SEMANTICS of every filter and of the planning sort on a
/// small but complete graph, so the rewrite can be judged on shape alone:
/// orders linked through dossier_orders only (DOS-1001, DOS-1006), through an activity only
/// (DOS-1002) and through both (DOS-1003); a cancelled trip that must never count; standalone
/// activities with and without a planned date; stops of every type; invoices in every status
/// (including a cancelled one); CMRs by all three routes; and a look-alike tenant B.
/// </summary>
public class DossierSearchScalabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private const string D1 = "DOS-1001", D2 = "DOS-1002", D3 = "DOS-1003", D4 = "DOS-1004", D5 = "DOS-1005", D6 = "DOS-1006", D7 = "DOS-1007";

    private sealed class Graph : IDisposable
    {
        public SqliteTestDbContext Db { get; } = new();
        public Guid TenantA { get; } = Guid.NewGuid();
        public Guid TenantB { get; } = Guid.NewGuid();
        public Guid CustomerCaudenberg { get; } = Guid.NewGuid();
        public Guid CustomerBrussels { get; } = Guid.NewGuid();
        public Guid DriverJan { get; } = Guid.NewGuid();
        public Guid DriverAn { get; } = Guid.NewGuid();
        public Guid VehicleOne { get; } = Guid.NewGuid();
        public Guid VehicleTwo { get; } = Guid.NewGuid();
        public Guid TrailerOne { get; } = Guid.NewGuid();
        public Guid TypeTransport { get; } = Guid.NewGuid();
        public Guid TypePlateau { get; } = Guid.NewGuid();
        public Guid TypeAdmin { get; } = Guid.NewGuid();
        public Guid ResponsibleUser { get; } = Guid.NewGuid();
        public Dictionary<string, Guid> DossierIds { get; } = new();

        public DossierService Sut(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantA);
            return new DossierService(Db.Context, tenant, new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }

        public async Task<PagedResult<DossierListItemDto>> Search(DossierSearchQuery query, Guid? tenantId = null) =>
            await Sut(tenantId).SearchAsync(query, CancellationToken.None);

        /// <summary>Dossier numbers in the order returned.</summary>
        public async Task<List<string>> Numbers(DossierSearchQuery query, Guid? tenantId = null) =>
            (await Search(query, tenantId)).Items.Select(i => i.DossierNumber).ToList();

        /// <summary>Dossier numbers sorted by number (for filters whose order is not under test), asserting TotalCount agrees.</summary>
        public async Task<List<string>> Set(DossierSearchQuery query, Guid? tenantId = null)
        {
            var page = await Search(query with { Sort = "number", Dir = "asc" }, tenantId);
            Assert.Equal(page.Items.Count, page.TotalCount);
            return page.Items.Select(i => i.DossierNumber).ToList();
        }

        public void Dispose() => Db.Dispose();
    }

    private static async Task<Graph> SeedAsync()
    {
        var g = new Graph();
        var db = g.Db.Context;
        var a = g.TenantA;
        var b = g.TenantB;

        db.Tenants.AddRange(
            new Tenant { Id = a, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime },
            new Tenant { Id = b, Name = "Elders", Slug = "elders", IsActive = true, CreatedAt = Now.UtcDateTime });

        var customerB = Guid.NewGuid();
        db.Customers.AddRange(
            new Customer { Id = g.CustomerCaudenberg, TenantId = a, CustomerNumber = "KL-301", Name = "Van Caudenberg NV", IsActive = true },
            new Customer { Id = g.CustomerBrussels, TenantId = a, CustomerNumber = "KL-302", Name = "Brussels Cargo BV", IsActive = true },
            // Tenant B: the same name and number as tenant A's first customer.
            new Customer { Id = customerB, TenantId = b, CustomerNumber = "KL-301", Name = "Van Caudenberg NV", IsActive = true });

        var employeeJan = Guid.NewGuid();
        var employeeAn = Guid.NewGuid();
        var employeeJanB = Guid.NewGuid();
        var driverJanB = Guid.NewGuid();
        var vehicleB = Guid.NewGuid();
        db.Employees.AddRange(
            new Employee { Id = employeeJan, TenantId = a, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Peeters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime },
            new Employee { Id = employeeAn, TenantId = a, EmployeeNumber = "MED-2", FirstName = "An", LastName = "Maes", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime },
            new Employee { Id = employeeJanB, TenantId = b, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Peeters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        db.Drivers.AddRange(
            new Driver { Id = g.DriverJan, TenantId = a, DriverNumber = "CH-1", EmployeeId = employeeJan, IsActive = true },
            new Driver { Id = g.DriverAn, TenantId = a, DriverNumber = "CH-2", EmployeeId = employeeAn, IsActive = true },
            new Driver { Id = driverJanB, TenantId = b, DriverNumber = "CH-1", EmployeeId = employeeJanB, IsActive = true });
        db.Vehicles.AddRange(
            new Vehicle { Id = g.VehicleOne, TenantId = a, InternalNumber = "VRT-1", LicensePlate = "1-ABC-123", IsActive = true },
            new Vehicle { Id = g.VehicleTwo, TenantId = a, InternalNumber = "VRT-2", LicensePlate = "2-DEF-456", IsActive = true },
            new Vehicle { Id = vehicleB, TenantId = b, InternalNumber = "VRT-1", LicensePlate = "1-ABC-123", IsActive = true });
        db.Trailers.Add(new Trailer { Id = g.TrailerOne, TenantId = a, InternalNumber = "OPL-1", LicensePlate = "Q-TRL-001", IsActive = true });
        db.ActivityTypes.AddRange(
            new ActivityType { Id = g.TypeTransport, TenantId = a, Code = "DIRECT_TRANSPORT", Name = "Direct transport", IsActive = true, HasStops = true, PlanningRelevant = true, IsBillable = true },
            new ActivityType { Id = g.TypePlateau, TenantId = a, Code = "PLATEAU", Name = "Plateau", IsActive = true, HasStops = false, PlanningRelevant = true, IsBillable = true },
            new ActivityType { Id = g.TypeAdmin, TenantId = a, Code = "ADMIN", Name = "Administratie", IsActive = true, HasStops = false, PlanningRelevant = false, IsBillable = false });

        Guid Dossier(Guid tenant, string number, string title, Guid customer, DossierStatus status, DateOnly date,
            string? reference = null, DateTime? closedAt = null, DossierConfirmationSource? source = null, Guid? responsible = null)
        {
            var id = Guid.NewGuid();
            db.TransportDossiers.Add(new TransportDossier
            {
                Id = id, TenantId = tenant, DossierNumber = number, Title = title, CustomerId = customer, Status = status, DossierDate = date,
                CustomerReference = reference, ClosedAt = closedAt, ConfirmationSource = source, ResponsibleUserId = responsible,
            });
            if (tenant == a) g.DossierIds[number] = id;
            return id;
        }

        var d1 = Dossier(a, D1, "Levering Gent", g.CustomerCaudenberg, DossierStatus.Open, new(2026, 10, 1));
        var d2 = Dossier(a, D2, "Rit Lille", g.CustomerBrussels, DossierStatus.Open, new(2026, 10, 2));
        var d3 = Dossier(a, D3, "Hasselt-Luik", g.CustomerCaudenberg, DossierStatus.Closed, new(2026, 9, 20),
            closedAt: new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc), source: DossierConfirmationSource.Manual);
        var d4 = Dossier(a, D4, "Plateau op werf", g.CustomerBrussels, DossierStatus.Open, new(2026, 10, 4), responsible: g.ResponsibleUser);
        var d5 = Dossier(a, D5, "Nog te plannen", g.CustomerCaudenberg, DossierStatus.Open, new(2026, 10, 5));
        var d6 = Dossier(a, D6, "Rotterdam", g.CustomerBrussels, DossierStatus.Cancelled, new(2026, 9, 15));
        var d7 = Dossier(a, D7, "Kraanwerk", g.CustomerCaudenberg, DossierStatus.Open, new(2026, 10, 6), reference: "REF-ANTWERP-7");
        var dB = Dossier(b, D1, "Elders Antwerpen", customerB, DossierStatus.Open, new(2026, 10, 1));

        Guid Order(Guid tenant, Guid customer, string number, TransportOrderStatus status, string? reference, params (StopType Type, string City, string Postal, string Country)[] stops)
        {
            var id = Guid.NewGuid();
            db.TransportOrders.Add(new TransportOrder { Id = id, TenantId = tenant, CustomerId = customer, OrderNumber = number, OrderDate = new(2026, 9, 30), Status = status, CustomerReference = reference });
            var sequence = 1;
            foreach (var s in stops)
            {
                db.TransportOrderStops.Add(new TransportOrderStop { Id = Guid.NewGuid(), TenantId = tenant, TransportOrderId = id, Sequence = sequence++, StopType = s.Type, LocationName = s.City, City = s.City, PostalCode = s.Postal, CountryCode = s.Country });
            }
            return id;
        }
        void LinkByOrder(Guid tenant, Guid dossier, Guid order) =>
            db.DossierOrders.Add(new DossierOrder { Id = Guid.NewGuid(), TenantId = tenant, DossierId = dossier, TransportOrderId = order });
        Guid Activity(Guid dossier, Guid type, int sequence, Guid? linkedOrder = null, DateOnly? plannedDate = null, DossierActivityExecutionStatus execution = DossierActivityExecutionStatus.Planned)
        {
            var id = Guid.NewGuid();
            db.DossierActivities.Add(new DossierActivity { Id = id, TenantId = a, DossierId = dossier, ActivityTypeId = type, Sequence = sequence, LinkedTransportOrderId = linkedOrder, PlannedDate = plannedDate, ExecutionStatus = execution });
            return id;
        }
        Guid Trip(Guid tenant, string number, DateOnly date, TripStatus status, Guid? driver, Guid? vehicle, Guid? trailer, params Guid[] orders)
        {
            var id = Guid.NewGuid();
            db.Trips.Add(new Trip { Id = id, TenantId = tenant, TripNumber = number, TripDate = date, Status = status, DriverId = driver, VehicleId = vehicle, TrailerId = trailer });
            var sequence = 1;
            foreach (var order in orders)
            {
                db.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = tenant, TripId = id, TransportOrderId = order, Sequence = sequence++ });
            }
            return id;
        }
        void Invoice(string number, InvoiceStatus status, Guid? order = null, Guid? activity = null)
        {
            var id = Guid.NewGuid();
            db.Invoices.Add(new Invoice { Id = id, TenantId = a, CustomerId = g.CustomerCaudenberg, InvoiceNumber = number, InvoiceDate = new(2026, 10, 15), DueDate = new(2026, 11, 15), Status = status });
            db.InvoiceLines.Add(new InvoiceLine { Id = Guid.NewGuid(), TenantId = a, InvoiceId = id, TransportOrderId = order, DossierActivityId = activity, Sequence = 1, Description = number, Quantity = 1, UnitPrice = 100, VatRatePercent = 21 });
        }

        // DOS-1001: order linked through dossier_orders ONLY (no activity). Antwerpen → Gent, Jan's live trip on 10-05
        // with the trailer; a CANCELLED invoice (never counts); a CMR uploaded on the order without a dossier.
        var o1 = Order(a, g.CustomerCaudenberg, "ORD-1001", TransportOrderStatus.Planned, "PO-A1",
            (StopType.Loading, "Antwerpen", "2000", "BE"), (StopType.Unloading, "Gent", "9000", "BE"));
        LinkByOrder(a, d1, o1);
        Trip(a, "RIT-1", new(2026, 10, 5), TripStatus.Planned, g.DriverJan, g.VehicleOne, g.TrailerOne, o1);
        Invoice("FAC-C", InvoiceStatus.Cancelled, order: o1);
        db.TransportOrderDocuments.Add(new TransportOrderDocument { Id = Guid.NewGuid(), TenantId = a, TransportOrderId = o1, DossierId = null, DocumentType = TransportOrderDocumentType.Cmr, Title = "CMR", DocumentPath = "x/cmr-1.pdf" });

        // DOS-1002: order linked through an ACTIVITY only (no dossier_orders row). Brussel → Lille (Site stop, FR);
        // An's trip in progress on 10-03 AND a cancelled trip of Jan on 09-01 (must never count); a standalone
        // plateau planned 10-10; a draft invoice.
        var o2 = Order(a, g.CustomerBrussels, "ORD-1002", TransportOrderStatus.Completed, "PO-B2",
            (StopType.Loading, "Brussel", "1000", "BE"), (StopType.Site, "Lille", "59000", "FR"));
        Activity(d2, g.TypeTransport, 1, linkedOrder: o2);
        Activity(d2, g.TypePlateau, 2, plannedDate: new(2026, 10, 10));
        Trip(a, "RIT-2", new(2026, 10, 3), TripStatus.InProgress, g.DriverAn, g.VehicleTwo, null, o2);
        Trip(a, "RIT-X", new(2026, 9, 1), TripStatus.Cancelled, g.DriverJan, g.VehicleOne, g.TrailerOne, o2);
        Invoice("FAC-D", InvoiceStatus.Draft, order: o2);

        // DOS-1003 (closed, manual): order linked through BOTH paths. Hasselt → Luik, Jan's completed trip on 09-25;
        // a sent invoice; an issued CMR; a resolved incident.
        var o3 = Order(a, g.CustomerCaudenberg, "ORD-1003", TransportOrderStatus.Completed, "PO-C3",
            (StopType.Loading, "Hasselt", "3500", "BE"), (StopType.Unloading, "Luik", "4000", "BE"));
        LinkByOrder(a, d3, o3);
        Activity(d3, g.TypeTransport, 1, linkedOrder: o3);
        Trip(a, "RIT-3", new(2026, 9, 25), TripStatus.Completed, g.DriverJan, g.VehicleOne, null, o3);
        Invoice("FAC-S", InvoiceStatus.Sent, order: o3);
        db.IssuedTransportDocuments.Add(new IssuedTransportDocument { Id = Guid.NewGuid(), TenantId = a, TransportOrderId = o3, DossierId = d3, Kind = IssuedTransportDocumentKind.Cmr, DocumentNumber = "CMR-2026-0001", RequestId = Guid.NewGuid(), IssuedAt = Now.UtcDateTime });
        db.Incidents.Add(new Incident { Id = Guid.NewGuid(), TenantId = a, DossierId = d3, Title = "Opgelost", Description = "x", Status = IncidentStatus.Resolved });

        // DOS-1004: no orders. Standalone plateaus: completed on 10-05 (billable, not invoiced → ready to invoice),
        // planned 10-12, and one without a date. Owned by the responsible user.
        Activity(d4, g.TypePlateau, 1, plannedDate: new(2026, 10, 5), execution: DossierActivityExecutionStatus.Completed);
        Activity(d4, g.TypePlateau, 2, plannedDate: new(2026, 10, 12));
        Activity(d4, g.TypePlateau, 3);

        // DOS-1005: nothing planned — a plateau without a date and a completed NON-billable admin activity; an open incident;
        // a non-CMR document.
        Activity(d5, g.TypePlateau, 1);
        Activity(d5, g.TypeAdmin, 2, execution: DossierActivityExecutionStatus.Completed);
        db.Incidents.Add(new Incident { Id = Guid.NewGuid(), TenantId = a, DossierId = d5, Title = "Schade", Description = "Pallet beschadigd", Status = IncidentStatus.New });
        db.TransportOrderDocuments.Add(new TransportOrderDocument { Id = Guid.NewGuid(), TenantId = a, DossierId = d5, DocumentType = TransportOrderDocumentType.Other, Title = "Foto", DocumentPath = "x/foto.jpg" });

        // DOS-1006 (cancelled): order through dossier_orders, Antwerpen → Rotterdam (NL), never on a trip;
        // a paid AND a draft invoice (Paid wins).
        var o6 = Order(a, g.CustomerBrussels, "ORD-1006", TransportOrderStatus.Completed, null,
            (StopType.Loading, "Antwerpen", "2018", "BE"), (StopType.Unloading, "Rotterdam", "3011", "NL"));
        LinkByOrder(a, d6, o6);
        Invoice("FAC-P", InvoiceStatus.Paid, order: o6);
        Invoice("FAC-D2", InvoiceStatus.Draft, order: o6);

        // DOS-1007: no orders; a completed billable plateau that IS invoiced (sent, through the activity line);
        // a CMR uploaded on the dossier itself; the dossier's own reference mentions Antwerp.
        var a7 = Activity(d7, g.TypePlateau, 1, execution: DossierActivityExecutionStatus.Completed);
        Invoice("FAC-S2", InvoiceStatus.Sent, activity: a7);
        db.TransportOrderDocuments.Add(new TransportOrderDocument { Id = Guid.NewGuid(), TenantId = a, DossierId = d7, DocumentType = TransportOrderDocumentType.Cmr, Title = "CMR", DocumentPath = "x/cmr-7.pdf" });

        // Tenant B: a look-alike graph that must never surface for tenant A — same dossier number, same customer
        // name/number, an Antwerpen loading stop, a trip with "Jan Peeters" and plate 1-ABC-123.
        var oB = Order(b, customerB, "ORD-1001", TransportOrderStatus.Planned, "PO-A1", (StopType.Loading, "Antwerpen", "2000", "BE"), (StopType.Unloading, "Gent", "9000", "BE"));
        LinkByOrder(b, dB, oB);
        Trip(b, "RIT-1", new(2026, 10, 5), TripStatus.Planned, driverJanB, vehicleB, null, oB);

        await db.SaveChangesAsync();
        return g;
    }

    private static List<string> All => [D1, D2, D3, D4, D5, D6, D7];

    // ------------------------------------------------------------ listing and global search

    [Fact]
    public async Task DefaultList_IsTheTenantsDossiers_NewestDossierDateFirst()
    {
        using var g = await SeedAsync();

        var page = await g.Search(new DossierSearchQuery());

        Assert.Equal(7, page.TotalCount);
        Assert.Equal([D7, D5, D4, D2, D1, D3, D6], page.Items.Select(i => i.DossierNumber));
        Assert.DoesNotContain(page.Items, i => i.Title == "Elders Antwerpen");
    }

    [Fact]
    public async Task GlobalSearch_ReachesEveryField_ThroughBothLinkPaths_NeverThroughACancelledTrip()
    {
        using var g = await SeedAsync();

        // Direct columns: number, title, the dossier's own reference.
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { Search = "dos-1003" }));
        Assert.Equal([D7], await g.Set(new DossierSearchQuery { Search = "kraanwerk" }));
        // "antwerp": a stop city through dossier_orders (DOS-1001, DOS-1006) and the dossier reference (DOS-1007) — default order.
        Assert.Equal([D7, D1, D6], await g.Numbers(new DossierSearchQuery { Search = "antwerp" }));
        // Customer name and number.
        Assert.Equal([D1, D3, D5, D7], await g.Set(new DossierSearchQuery { Search = "caudenberg" }));
        Assert.Equal([D2, D4, D6], await g.Set(new DossierSearchQuery { Search = "kl-302" }));
        // Order number and order reference: through dossier_orders (DOS-1003) and through the activity link alone (DOS-1002).
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { Search = "ord-1002" }));
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { Search = "po-c3" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { Search = "po-b2" }));
        // Stops: city contains, postal code STARTS WITH.
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { Search = "lille" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { Search = "590" }));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { Search = "9000" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { Search = "000" }));
        // Trips: driver name and vehicle plate — Jan's cancelled trip on DOS-1002 does not count.
        Assert.Equal([D1, D3], await g.Set(new DossierSearchQuery { Search = "peeters" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { Search = "maes" }));
        Assert.Equal([D1, D3], await g.Set(new DossierSearchQuery { Search = "abc-123" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { Search = "2-def" }));
        // Nothing, and never the other tenant.
        Assert.Empty(await g.Set(new DossierSearchQuery { Search = "bestaat-niet" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { Search = "elders" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { Search = "%_'\"" }));
    }

    // ------------------------------------------------------------ planning sort and filters

    [Fact]
    public async Task PlanningDateSort_IsTheMinOfLiveTripsAndStandaloneDates_NullsLast_StableSecondaryKey()
    {
        using var g = await SeedAsync();

        // DOS-1001 10-05 (trip) · DOS-1002 10-03 (trip; the 09-01 trip is cancelled, the 10-10 plateau is later)
        // · DOS-1003 09-25 (trip) · DOS-1004 10-05 (standalone) · DOS-1005/1006/1007 none.
        Assert.Equal([D1, D4, D2, D3, D5, D6, D7], await g.Numbers(new DossierSearchQuery { Sort = "planningDate", Dir = "desc" }));
        Assert.Equal([D3, D2, D1, D4, D5, D6, D7], await g.Numbers(new DossierSearchQuery { Sort = "planningDate", Dir = "asc" }));
        // No direction = descending for a date key.
        Assert.Equal([D1, D4, D2, D3, D5, D6, D7], await g.Numbers(new DossierSearchQuery { Sort = "planningdate" }));

        // Pagination over the joined sort is stable and counts the whole set.
        var pages = new List<List<string>>();
        for (var p = 1; p <= 3; p++)
        {
            var page = await g.Search(new DossierSearchQuery { Sort = "planningDate", Dir = "desc", Page = p, PageSize = 3 });
            Assert.Equal(7, page.TotalCount);
            pages.Add(page.Items.Select(i => i.DossierNumber).ToList());
        }
        Assert.Equal([[D1, D4, D2], [D3, D5, D6], [D7]], pages);

        // The sort key agrees with the Planning column of the rows.
        var rows = (await g.Search(new DossierSearchQuery())).Items.ToDictionary(i => i.DossierNumber, i => i.PlanningDate);
        Assert.Equal(new DateOnly(2026, 10, 5), rows[D1]);
        Assert.Equal(new DateOnly(2026, 10, 3), rows[D2]);
        Assert.Equal(new DateOnly(2026, 9, 25), rows[D3]);
        Assert.Equal(new DateOnly(2026, 10, 5), rows[D4]);
        Assert.All(new[] { D5, D6, D7 }, n => Assert.Null(rows[n]));
    }

    [Fact]
    public async Task PlannedUnplanned_AndPlanningWindow_SeeEveryLiveDate_NeverACancelledTrip()
    {
        using var g = await SeedAsync();

        Assert.Equal([D1, D2, D3, D4], await g.Set(new DossierSearchQuery { Planning = "planned" }));
        Assert.Equal([D5, D6, D7], await g.Set(new DossierSearchQuery { Planning = "unplanned" }));
        Assert.Equal([D5, D6, D7], await g.Set(new DossierSearchQuery { Planning = " Unplanned " }));

        // Any live date in the window counts — DOS-1002 through its 10-03 trip, DOS-1004 through its 10-05 plateau.
        Assert.Equal([D1, D2, D4], await g.Set(new DossierSearchQuery { PlanningFrom = new(2026, 10, 1), PlanningTo = new(2026, 10, 6) }));
        // From only: DOS-1002's later plateau (10-10) and DOS-1004's (10-12).
        Assert.Equal([D2, D4], await g.Set(new DossierSearchQuery { PlanningFrom = new(2026, 10, 10) }));
        // To only: the cancelled 09-01 trip of DOS-1002 is invisible, DOS-1003's 09-25 trip is not.
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { PlanningTo = new(2026, 9, 30) }));
        Assert.Empty(await g.Set(new DossierSearchQuery { PlanningFrom = new(2026, 9, 1), PlanningTo = new(2026, 9, 1) }));

        await Assert.ThrowsAsync<DomainValidationException>(() => g.Search(new DossierSearchQuery { Planning = "later" }));
        await Assert.ThrowsAsync<DomainValidationException>(() => g.Search(new DossierSearchQuery { PlanningFrom = new(2026, 10, 2), PlanningTo = new(2026, 10, 1) }));
    }

    [Fact]
    public async Task TripFilters_DriverVehicleTrailerPlate_IgnoreCancelledTrips()
    {
        using var g = await SeedAsync();

        Assert.Equal([D1, D3], await g.Set(new DossierSearchQuery { DriverId = g.DriverJan }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { DriverId = g.DriverAn }));
        Assert.Empty(await g.Set(new DossierSearchQuery { DriverId = Guid.NewGuid() }));
        Assert.Equal([D1, D3], await g.Set(new DossierSearchQuery { VehicleId = g.VehicleOne }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { VehicleId = g.VehicleTwo }));
        // The trailer also sits on the cancelled trip of DOS-1002: only DOS-1001 counts.
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { TrailerId = g.TrailerOne }));
        // Plate: vehicle or trailer, contains, case-insensitive.
        Assert.Equal([D1, D3], await g.Set(new DossierSearchQuery { LicensePlate = "ABC" }));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { LicensePlate = "trl" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { LicensePlate = "def-456" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { LicensePlate = "zzz" }));
    }

    // ------------------------------------------------------------ orders and stops

    [Fact]
    public async Task OrderNumber_AndCustomerReference_Filters()
    {
        using var g = await SeedAsync();

        // Order number: both link paths (DOS-1002 is activity-linked only).
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { OrderNumber = "1002" }));
        Assert.Equal([D1, D2, D3, D6], await g.Set(new DossierSearchQuery { OrderNumber = "ord-100" }));
        // Customer reference: the dossier's own reference, or an order reference through dossier_orders.
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { CustomerReference = "po-a1" }));
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { CustomerReference = "po-c3" }));
        Assert.Equal([D7], await g.Set(new DossierSearchQuery { CustomerReference = "antwerp" }));
        // As before the rewrite, an order linked through an activity alone is not reached by this filter
        // (the global search does reach it — see GlobalSearch_…).
        Assert.Empty(await g.Set(new DossierSearchQuery { CustomerReference = "po-b2" }));
    }

    [Fact]
    public async Task StopFilters_LoadingUnloadingPostalCountry()
    {
        using var g = await SeedAsync();

        Assert.Equal([D1, D6], await g.Set(new DossierSearchQuery { LoadingCity = "antwerp" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { LoadingCity = "brussel" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { LoadingCity = "gent" }));
        // Unloading = Unloading or Site stops.
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { UnloadingCity = "gent" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { UnloadingCity = "lille" }));
        Assert.Equal([D6], await g.Set(new DossierSearchQuery { UnloadingCity = "rotterdam" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { UnloadingCity = "antwerp" }));
        // Postal code starts with.
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { PostalCode = "90" }));
        Assert.Equal([D1, D6], await g.Set(new DossierSearchQuery { PostalCode = "20" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { PostalCode = "59" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { PostalCode = "000" }));
        // Country code equals.
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { CountryCode = "FR" }));
        Assert.Equal([D6], await g.Set(new DossierSearchQuery { CountryCode = "nl" }));
        Assert.Equal([D1, D2, D3, D6], await g.Set(new DossierSearchQuery { CountryCode = "be" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { CountryCode = "b" }));
    }

    // ------------------------------------------------------------ lifecycle, identification, quick views

    [Fact]
    public async Task LifecycleIdentificationAndQuickViewFilters()
    {
        using var g = await SeedAsync();

        Assert.Equal([D1, D2, D4, D5, D7], await g.Set(new DossierSearchQuery { Status = "Open" }));
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { Status = "Closed" }));
        Assert.Equal([D6], await g.Set(new DossierSearchQuery { Status = "Cancelled" }));
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { ConfirmationSource = "Manual" }));
        Assert.Empty(await g.Set(new DossierSearchQuery { ConfirmationSource = "Automatic" }));
        Assert.Equal([D3], await g.Set(new DossierSearchQuery { ConfirmedFrom = new(2026, 10, 1), ConfirmedTo = new(2026, 10, 31) }));
        Assert.Empty(await g.Set(new DossierSearchQuery { ConfirmedFrom = new(2026, 10, 9) }));
        Assert.Equal([D1, D2, D4], await g.Set(new DossierSearchQuery { DateFrom = new(2026, 10, 1), DateTo = new(2026, 10, 4) }));
        // CreatedAt is stamped by the auditing interceptor at save time (wall clock), not by the fixture.
        var createdOn = DateOnly.FromDateTime(await g.Db.Context.TransportDossiers.AsNoTracking().Where(d => d.Id == g.DossierIds[D1]).Select(d => d.CreatedAt).SingleAsync());
        Assert.Equal(All, await g.Set(new DossierSearchQuery { CreatedFrom = createdOn, CreatedTo = createdOn }));
        Assert.Empty(await g.Set(new DossierSearchQuery { CreatedTo = createdOn.AddDays(-1) }));

        Assert.Equal([D4], await g.Set(new DossierSearchQuery { DossierNumber = "1004" }));
        Assert.Equal(All, await g.Set(new DossierSearchQuery { DossierNumber = "dos-100" }));
        Assert.Equal([D1, D3, D5, D7], await g.Set(new DossierSearchQuery { CustomerNumber = "kl-301" }));
        Assert.Equal([D2, D4, D6], await g.Set(new DossierSearchQuery { CustomerId = g.CustomerBrussels }));

        Assert.Equal([D4], await g.Set(new DossierSearchQuery { ResponsibleUserId = g.ResponsibleUser }));
        Assert.Empty(await g.Set(new DossierSearchQuery { ResponsibleUserId = Guid.NewGuid() }));
        // Open incident = New or InProgress; the resolved one on DOS-1003 does not count.
        Assert.Equal([D5], await g.Set(new DossierSearchQuery { HasOpenIncident = true }));
        Assert.Equal([D1, D2, D3, D4, D6, D7], await g.Set(new DossierSearchQuery { HasOpenIncident = false }));

        Assert.Equal([D2, D4, D5, D7], await g.Set(new DossierSearchQuery { ActivityTypeId = g.TypePlateau }));
        Assert.Equal([D2, D3], await g.Set(new DossierSearchQuery { ActivityTypeId = g.TypeTransport }));
        Assert.Equal([D5], await g.Set(new DossierSearchQuery { ActivityTypeId = g.TypeAdmin }));
        Assert.Equal([D2, D3], await g.Set(new DossierSearchQuery { TransportTypeId = g.TypeTransport }));
        Assert.Empty(await g.Set(new DossierSearchQuery { TransportTypeId = g.TypePlateau }));
    }

    // ------------------------------------------------------------ commercial

    [Fact]
    public async Task InvoiceStatusFilters_RollUpLikeTheRow_IgnoreCancelledInvoices_ReachActivityLines()
    {
        using var g = await SeedAsync();

        // DOS-1001 has only a cancelled invoice; DOS-1007 is invoiced through an activity line.
        Assert.Equal([D1, D4, D5], await g.Set(new DossierSearchQuery { InvoiceStatus = "NotInvoiced" }));
        Assert.Equal([D2], await g.Set(new DossierSearchQuery { InvoiceStatus = "Draft" }));
        Assert.Equal([D3, D7], await g.Set(new DossierSearchQuery { InvoiceStatus = "Sent" }));
        // DOS-1006 has a draft AND a paid invoice: Paid wins, so it is neither "Draft" nor "NotInvoiced".
        Assert.Equal([D6], await g.Set(new DossierSearchQuery { InvoiceStatus = "Paid" }));
        // Ready to invoice: a completed linked order (either path: DOS-1002, DOS-1003, DOS-1006) or a completed
        // billable standalone activity not yet invoiced (DOS-1004) — not the invoiced one (DOS-1007), not the
        // non-billable one (DOS-1005).
        Assert.Equal([D2, D3, D4, D6], await g.Set(new DossierSearchQuery { InvoiceStatus = "ReadyToInvoice" }));
        await Assert.ThrowsAsync<DomainValidationException>(() => g.Search(new DossierSearchQuery { InvoiceStatus = "Weird" }));

        // The filter agrees with the InvoiceStatus column of every row.
        var rows = (await g.Search(new DossierSearchQuery())).Items.ToDictionary(i => i.DossierNumber, i => i.InvoiceStatus);
        Assert.Equal(("NotInvoiced", "Draft", "Sent", "NotInvoiced", "NotInvoiced", "Paid", "Sent"), (rows[D1], rows[D2], rows[D3], rows[D4], rows[D5], rows[D6], rows[D7]));
    }

    [Fact]
    public async Task HasCmr_SeesDossierDocuments_OrderDocumentsThroughDossierOrders_AndIssuedCmrs()
    {
        using var g = await SeedAsync();

        Assert.Equal([D1, D3, D7], await g.Set(new DossierSearchQuery { HasCmr = true }));
        Assert.Equal([D2, D4, D5, D6], await g.Set(new DossierSearchQuery { HasCmr = false }));

        var rows = (await g.Search(new DossierSearchQuery())).Items.ToDictionary(i => i.DossierNumber, i => i.HasCmr);
        Assert.Equal([D1, D3, D7], rows.Where(r => r.Value).Select(r => r.Key).Order());
    }

    [Fact]
    public async Task PriceStatusFilter_StillAgreesWithThePriceCountsOfEveryRow()
    {
        using var g = await SeedAsync();

        static string? Classify(DossierListItemDto i) =>
            i.BillableActivityCount == 0 ? null
            : i.PricedActivityCount == 0 ? "Unpriced"
            : i.PricedActivityCount < i.BillableActivityCount ? "Partial"
            : "Priced";
        var all = (await g.Search(new DossierSearchQuery())).Items;
        foreach (var status in new[] { "Priced", "Partial", "Unpriced" })
        {
            var expected = all.Where(i => Classify(i) == status).Select(i => i.DossierNumber).Order().ToList();
            Assert.Equal(expected, await g.Set(new DossierSearchQuery { PriceStatus = status }));
        }
    }

    // ------------------------------------------------------------ combinations, counts, tenants

    [Fact]
    public async Task CombinedFilters_AndWithEachOther_WithTotalCountMatchingTheItems()
    {
        using var g = await SeedAsync();

        async Task Expect(DossierSearchQuery query, params string[] numbers)
        {
            var page = await g.Search(query);
            Assert.Equal(numbers, page.Items.Select(i => i.DossierNumber));
            Assert.Equal(numbers.Length, page.TotalCount);
        }

        await Expect(new DossierSearchQuery { Search = "antwerp", Status = "Open" }, D7, D1);
        await Expect(new DossierSearchQuery { Planning = "unplanned", HasCmr = false, Sort = "number", Dir = "asc" }, D5, D6);
        await Expect(new DossierSearchQuery { DriverId = g.DriverJan, InvoiceStatus = "Sent" }, D3);
        await Expect(new DossierSearchQuery { CountryCode = "be", PlanningFrom = new(2026, 10, 1), PlanningTo = new(2026, 10, 6), Sort = "number", Dir = "asc" }, D1, D2);
        await Expect(new DossierSearchQuery { Search = "caudenberg", Sort = "planningDate", Dir = "asc" }, D3, D1, D5, D7);
        await Expect(new DossierSearchQuery { LoadingCity = "antwerp", UnloadingCity = "gent", PostalCode = "20", OrderNumber = "1001", LicensePlate = "abc" }, D1);
        // Two relational filters AND-ed down to nothing.
        await Expect(new DossierSearchQuery { DriverId = g.DriverAn, HasCmr = true });

        // Paging keeps the full count: Brussels' planned dossiers are DOS-1004 (10-05) then DOS-1002 (10-03).
        var page2 = await g.Search(new DossierSearchQuery { CustomerId = g.CustomerBrussels, Planning = "planned", Sort = "planningDate", Dir = "desc", Page = 2, PageSize = 1 });
        Assert.Equal((2, 2, 1), (page2.TotalCount, page2.Page, page2.Items.Count));
        Assert.Equal([D2], page2.Items.Select(i => i.DossierNumber));
    }

    [Fact]
    public async Task TenantB_SeesOnlyItsOwnGraph_AndTenantANeverSeesIt()
    {
        using var g = await SeedAsync();
        var b = g.TenantB;

        // Tenant B's own look-alike dossier is found through every path that exists for it…
        Assert.Equal(["Elders Antwerpen"], (await g.Search(new DossierSearchQuery { Search = "antwerp" }, b)).Items.Select(i => i.Title));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { Search = "peeters" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { LicensePlate = "abc" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { LoadingCity = "antwerp" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { CustomerNumber = "kl-301" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { Planning = "planned" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { PlanningFrom = new(2026, 10, 5), PlanningTo = new(2026, 10, 5) }, b));
        Assert.Equal([D1], await g.Numbers(new DossierSearchQuery { Sort = "planningDate", Dir = "desc" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { InvoiceStatus = "NotInvoiced" }, b));
        Assert.Equal([D1], await g.Set(new DossierSearchQuery { HasCmr = false }, b));
        // …but never tenant A's ids, dossiers or filters' matches.
        Assert.Empty(await g.Set(new DossierSearchQuery { DriverId = g.DriverJan }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { VehicleId = g.VehicleOne }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { CustomerId = g.CustomerCaudenberg }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { OrderNumber = "1002" }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { Search = "lille" }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { HasCmr = true }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { Planning = "unplanned" }, b));
        Assert.Empty(await g.Set(new DossierSearchQuery { Status = "Closed" }, b));

        // Tenant A sees exactly its seven, whatever the filter.
        Assert.Equal(All, await g.Set(new DossierSearchQuery()));
        Assert.Equal(1, (await g.Search(new DossierSearchQuery { DossierNumber = D1 })).TotalCount);
        Assert.Empty(await g.Set(new DossierSearchQuery { Search = "elders" }));
    }
}
