using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// T-H3 (acceptance 2026-10-16): the CMR printed the tenant as sender and the ordering customer as
/// consignee, and had no carrier nor registration. The model handed to the renderer must carry:
/// sender = loading party, consignee = unloading party, carrier = the tenant's legal entity, and
/// the vehicle/trailer plates of the trip the order is on. Asserted on the snapshot (the data
/// layer), plus one real render that must not throw.
/// </summary>
public class CmrConsignmentPartyTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, TransportDocumentService Sut, Guid TenantId, Guid OrderingCustomerId, Guid ShipperLocationId, Guid OwnLocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var orderingId = Guid.NewGuid();
        var shipperId = Guid.NewGuid();
        var shipperLocationId = Guid.NewGuid();
        var ownLocationId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Development Transportbedrijf", Slug = "dev", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Development Transportbedrijf BV", VatNumber = "BE0123456749",
            Street = "Transportlaan", HouseNumber = "5", PostalCode = "2030", City = "Antwerpen", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.AddRange(
            new Customer
            {
                Id = orderingId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Bouwbedrijf Peeters",
                Street = "Kerkstraat", HouseNumber = "1", PostalCode = "3000", City = "Leuven", VatNumber = "BE0999999999", IsActive = true,
            },
            new Customer
            {
                Id = shipperId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Nexans NV",
                Street = "Kabelweg", HouseNumber = "9", PostalCode = "2000", City = "Antwerpen", VatNumber = "BE0888888888", IsActive = true,
            });
        db.Context.Locations.AddRange(
            new Location { Id = shipperLocationId, TenantId = tenantId, Code = "NEX-MAG", Name = "Nexans magazijn", CustomerId = shipperId, IsActive = true },
            new Location { Id = ownLocationId, TenantId = tenantId, Code = "PEE-MAG", Name = "Peeters depot", CustomerId = orderingId, IsActive = true });
        await db.Context.SaveChangesAsync();
        return new Harness(db, new TransportDocumentService(db.Context, new DevTenantContext(tenantId)), tenantId, orderingId, shipperLocationId, ownLocationId);
    }

    private static TransportOrder Order(Harness h, Guid? loadingLocationId, string? loadingName, string loadingStreet = "Kabelweg 9")
    {
        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.OrderingCustomerId,
            OrderNumber = "ORD-0044", OrderDate = new DateOnly(2026, 10, 14),
            GoodsDescription = "Kabelhaspels", Quantity = 4, QuantityUnit = "haspels", WeightKg = 3200, CustomerReference = "PO-77",
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, StopType = StopType.Loading, LocationId = loadingLocationId,
            LocationName = loadingName, Address = loadingStreet, PostalCode = "2000", City = "Antwerpen", CountryCode = "BE",
            PlannedFrom = new DateTime(2026, 10, 15, 6, 30, 0, DateTimeKind.Utc),
        });
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 2, StopType = StopType.Unloading,
            LocationName = "Bouwwerf Gent", Address = "Teststraat 22", PostalCode = "9000", City = "Gent", CountryCode = "BE",
        });
        h.Db.Context.TransportOrders.Add(order);
        return order;
    }

    private static async Task PlanAsync(Harness h, TransportOrder order)
    {
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        h.Db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssens",
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        h.Db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = h.TenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = h.TenantId, InternalNumber = "VRT-1", LicensePlate = "1-ABC-123", IsActive = true });
        h.Db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = h.TenantId, InternalNumber = "OPL-1", LicensePlate = "Q-AIX-42", IsActive = true });
        var trip = new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0001", TripDate = new DateOnly(2026, 10, 15),
            Status = TripStatus.Planned, DriverId = driverId, VehicleId = vehicleId, TrailerId = trailerId,
        };
        h.Db.Context.Trips.Add(trip);
        h.Db.Context.TripOrders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = order.Id, Sequence = 1 });
        await h.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Cmr_SenderIsTheLoadingParty_ConsigneeTheUnloadingParty_CarrierTheLegalEntity_WithTheTripPlates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = Order(h, h.ShipperLocationId, "Nexans magazijn");
        await h.Db.Context.SaveChangesAsync();
        await PlanAsync(h, order);

        var snapshot = (await h.Sut.BuildSnapshotAsync(order.Id, "Cmr", CancellationToken.None))!;
        var c = snapshot.Consignment!;

        // Box 1: the owner of the loading location — not the tenant, not the ordering customer.
        Assert.Equal(("Nexans NV", "Nexans magazijn", "Kabelweg 9 2000 Antwerpen", "BE0888888888", TransportDocumentPartySource.LocationOwner),
            (c.Sender.Name, c.Sender.Detail, c.Sender.AddressLine, c.Sender.VatNumber, c.Sender.Source));
        // Box 2: the unloading party (a free address with a name of its own).
        Assert.Equal(("Bouwwerf Gent", "Teststraat 22 9000 Gent", TransportDocumentPartySource.StopLocation),
            (c.Consignee.Name, c.Consignee.AddressLine, c.Consignee.Source));
        // Box 16: the transport company is the carrier.
        Assert.Equal(("Development Transportbedrijf BV", "Transportlaan 5 2030 Antwerpen", "BE0123456749"),
            (c.Carrier.Name, c.Carrier.AddressLine, c.Carrier.VatNumber));
        // Boxes 3/4 and 25.
        Assert.Equal(new TransportDocumentPlace("Nexans magazijn", "Kabelweg 9 2000 Antwerpen", "BE", new DateOnly(2026, 10, 15)), c.TakingOver);
        Assert.Equal(new TransportDocumentPlace("Bouwwerf Gent", "Teststraat 22 9000 Gent", "BE", null), c.Delivery);
        Assert.Equal(("1-ABC-123", "Q-AIX-42", "Jan Janssens"), (c.VehicleRegistration, c.TrailerRegistration, c.DriverName));
        // The ordering party is still known (delivery note "Klant", the CMR "Opdrachtgever" box).
        Assert.Equal("Bouwbedrijf Peeters", snapshot.Customer.Name);

        var pdf = TransportDocumentRenderer.Render(snapshot with { Reference = new TransportDocumentReference("CMR-2026-00001", "DOS-0044", null) });
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        var outputPath = Environment.GetEnvironmentVariable("CMR_CHECK_PDF");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            await File.WriteAllBytesAsync(outputPath, pdf);
        }
    }

    [Fact]
    public async Task Cmr_GoodsLines_PrintTheUnitName_NotTheUnitCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = Order(h, h.ShipperLocationId, "Nexans magazijn");
        h.Db.Context.UnitTypes.Add(new Modules.Reference.Entities.UnitType
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "EUROPALLET", Name = "Europallet", IsActive = true,
        });
        h.Db.Context.CargoItems.AddRange(
            new CargoItem { Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = order.Id, Sequence = 1, Description = "Kabelhaspels", ExpectedQuantity = 10, QuantityUnitCode = "EUROPALLET" },
            new CargoItem { Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = order.Id, Sequence = 2, Description = "Losse kabels", ExpectedQuantity = 3, QuantityUnit = "rollen" });
        await h.Db.Context.SaveChangesAsync();

        var snapshot = (await h.Sut.BuildSnapshotAsync(order.Id, "Cmr", CancellationToken.None))!;

        // The managed unit prints its name; a legacy free-text unit stays as typed.
        Assert.Equal(["Europallet", "rollen"], snapshot.Lines.Select(l => l.Unit));
    }

    [Fact]
    public async Task Cmr_LoadingAtTheOrderingCustomersOwnSite_MakesTheOrderingCustomerTheSender()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var owned = Order(h, h.OwnLocationId, "Peeters depot", "Industrieweg 3");
        await h.Db.Context.SaveChangesAsync();

        var c = (await h.Sut.BuildSnapshotAsync(owned.Id, "Cmr", CancellationToken.None))!.Consignment!;

        Assert.Equal(("Bouwbedrijf Peeters", "Peeters depot", "Industrieweg 3 2000 Antwerpen", "BE0999999999"),
            (c.Sender.Name, c.Sender.Detail, c.Sender.AddressLine, c.Sender.VatNumber));
        // Not planned: no registration — the document prints a blank line to fill in by hand.
        Assert.Null(c.VehicleRegistration);
        Assert.Null(c.TrailerRegistration);
    }

    [Theory]
    [InlineData(null, "Andere straat 4", "Bouwbedrijf Peeters", TransportDocumentPartySource.OrderingCustomer)] // no name at all
    [InlineData("Peeters hoofdzetel", "Kerkstraat 1", "Bouwbedrijf Peeters", TransportDocumentPartySource.OrderingCustomer)] // the customer's own address
    [InlineData("Zandgroeve De Smet", "Kerkstraat 1b", "Zandgroeve De Smet", TransportDocumentPartySource.StopLocation)]
    public void ResolveStopParty_FallsBackToTheOrderingCustomerOnlyForItsOwnAddressOrANamelessStop(
        string? stopName, string street, string expectedName, string expectedSource)
    {
        var ordering = new Customer { Name = "Bouwbedrijf Peeters", Street = "Kerkstraat", HouseNumber = "1", PostalCode = "3000", City = "Leuven" };
        var orderingParty = new TransportDocumentParty(ordering.Name, "Kerkstraat 1 3000 Leuven", null, Source: TransportDocumentPartySource.OrderingCustomer);
        var stop = new TransportOrderStop { StopType = StopType.Loading, LocationName = stopName, Address = street, PostalCode = "3000", City = "Leuven" };

        var party = TransportDocumentService.ResolveStopParty(stop, null, ordering, orderingParty);

        Assert.Equal((expectedName, expectedSource), (party.Name, party.Source));
    }

    [Fact]
    public async Task DeliveryNote_DeliversToTheUnloadingParty_AndKeepsTheOrderingCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = Order(h, h.ShipperLocationId, "Nexans magazijn");
        await h.Db.Context.SaveChangesAsync();
        await PlanAsync(h, order);

        var note = (await h.Sut.BuildSnapshotAsync(order.Id, "DeliveryNote", CancellationToken.None))!;

        // The delivery note keeps "Klant" = the ordering party and adds "Leveren aan" = the consignee.
        Assert.Equal(("Bouwbedrijf Peeters", "Bouwwerf Gent"), (note.Customer.Name, note.Consignment!.Consignee.Name));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(TransportDocumentRenderer.Render(note), 0, 5));
    }
}
