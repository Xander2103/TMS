using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Locations;

/// <summary>
/// Sprint 2 — central address master. One physical address, many customer relationships.
/// </summary>
public class CustomerAddressServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 27, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, CustomerAddressService Sut, LocationService Locations, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        return new Harness(
            db,
            new CustomerAddressService(db.Context, tenant, audit),
            new LocationService(db.Context, tenant, audit, new CountryCodeValidator(db.Context)),
            tenantId);
    }

    private static async Task<Guid> AddCustomerAsync(Harness h, string name, string number)
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = h.TenantId, Name = name, CustomerNumber = number };
        h.Db.Context.Customers.Add(customer);
        await h.Db.Context.SaveChangesAsync();
        return customer.Id;
    }

    /// <summary>Creates a physical address through the real service so the derived keys are set.</summary>
    private static async Task<Guid> AddAddressAsync(
        Harness h, string code, string name,
        string street = "Noorderlaan", string houseNumber = "10", string postalCode = "2030",
        string city = "Antwerpen", string country = "BE")
    {
        var result = await h.Locations.CreateAsync(
            new CreateLocationRequest(
                code, name, LocationType.CustomerLocation,
                Street: street, HouseNumber: houseNumber, PostalCode: postalCode, City: city, CountryCode: country,
                Latitude: null, Longitude: null,
                ContactName: null, ContactPhone: null, ContactEmail: null,
                OpeningHours: null, LoadingInstructions: null, UnloadingInstructions: null, AccessInstructions: null,
                AccessRestrictions: null, VehicleRestrictions: null, TrailerRestrictions: null,
                AlfapassRequired: false, AppointmentRequired: false, CustomerId: null, Notes: null),
            CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, result.Outcome);
        return result.Location!.Id;
    }

    private static LinkCustomerAddressRequest LinkRequest(Guid locationId, bool defaultLoading = false) =>
        new(locationId, Alias: null, CustomerReference: null, Role: CustomerLocationRole.Both,
            IsDefaultLoading: defaultLoading, IsDefaultUnloading: false, IsDefaultBilling: false, Instructions: null);

    // ---------------------------------------------------------- scenario A

    [Fact]
    public async Task DuplicateCheck_OffersTheExistingAddress_WhenASecondCustomerEntersTheSamePlace()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await AddCustomerAsync(h, "Klant A", "KL-1");
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");
        await h.Sut.LinkAsync(customerA, LinkRequest(locationId), CancellationToken.None);

        // Customer B types the same address, differently cased and spaced.
        var check = await h.Sut.CheckDuplicatesAsync(
            new AddressDuplicateCheckRequest("  noorderlaan ", "10", "2030", "ANTWERPEN", "be", null),
            CancellationToken.None);

        Assert.True(check.HasExactMatch);
        var candidate = Assert.Single(check.Candidates);
        Assert.Equal(locationId, candidate.LocationId);
        Assert.Equal(AddressDuplicateMatch.Exact, candidate.Match);
        // The reason to reuse instead of re-create: it is already in use.
        Assert.Equal(["Klant A"], candidate.LinkedCustomers);
    }

    [Fact]
    public async Task DuplicateCheck_IgnoresPunctuationAndDiacritics()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddAddressAsync(h, "ADR-1", "Site", street: "Sint-Niklaasstraat", houseNumber: "10 A", city: "Gent", postalCode: "9000");

        var check = await h.Sut.CheckDuplicatesAsync(
            new AddressDuplicateCheckRequest("sint niklaasstraat", "10a", "9000", "gent", "BE", null),
            CancellationToken.None);

        Assert.True(check.HasExactMatch);
    }

    // ---------------------------------------------------------- scenario E

    [Fact]
    public async Task DuplicateCheck_SameStreetDifferentHouseNumber_IsNotAnExactDuplicate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddAddressAsync(h, "ADR-1", "Nummer 10", houseNumber: "10");

        var check = await h.Sut.CheckDuplicatesAsync(
            new AddressDuplicateCheckRequest("Noorderlaan", "12", "2030", "Antwerpen", "BE", null),
            CancellationToken.None);

        Assert.False(check.HasExactMatch);
        // Still worth showing — same street — but creating it is a normal, allowed action.
        Assert.Equal(AddressDuplicateMatch.SameStreet, Assert.Single(check.Candidates).Match);
    }

    [Fact]
    public async Task DuplicateCheck_WithoutStreetOrCity_ReportsNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await AddAddressAsync(h, "ADR-1", "Site");

        var check = await h.Sut.CheckDuplicatesAsync(
            new AddressDuplicateCheckRequest(null, null, null, null, "BE", null), CancellationToken.None);

        Assert.False(check.HasExactMatch);
        Assert.Empty(check.Candidates);
    }

    // ------------------------------------------------------- scenarios B/C

    [Fact]
    public async Task OnePhysicalAddress_CarriesTwoCustomerRelationships()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await AddCustomerAsync(h, "Klant A", "KL-1");
        var customerB = await AddCustomerAsync(h, "Klant B", "KL-2");
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");

        await h.Sut.LinkAsync(customerA, LinkRequest(locationId), CancellationToken.None);
        await h.Sut.LinkAsync(customerB, LinkRequest(locationId), CancellationToken.None);

        // Still ONE physical address.
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync(l => l.Id == locationId));

        var forA = Assert.Single(await h.Sut.ListForCustomerAsync(customerA, false, CancellationToken.None));
        var forB = Assert.Single(await h.Sut.ListForCustomerAsync(customerB, false, CancellationToken.None));
        Assert.Equal(locationId, forA.LocationId);
        Assert.Equal(locationId, forB.LocationId);
        Assert.Equal(2, forA.LinkedCustomerCount);
    }

    [Fact]
    public async Task Link_RejectsASecondRelationshipForTheSameCustomerAndAddress()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await AddCustomerAsync(h, "Klant A", "KL-1");
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");

        await h.Sut.LinkAsync(customerA, LinkRequest(locationId), CancellationToken.None);
        var again = await h.Sut.LinkAsync(customerA, LinkRequest(locationId), CancellationToken.None);

        Assert.Equal(CustomerAddressOutcome.AlreadyLinked, again.Outcome);
    }

    [Fact]
    public async Task Unlink_RemovesOnlyTheRelationship_AddressAndOtherCustomerSurvive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await AddCustomerAsync(h, "Klant A", "KL-1");
        var customerB = await AddCustomerAsync(h, "Klant B", "KL-2");
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");
        var linkA = await h.Sut.LinkAsync(customerA, LinkRequest(locationId), CancellationToken.None);
        await h.Sut.LinkAsync(customerB, LinkRequest(locationId), CancellationToken.None);

        Assert.True(await h.Sut.UnlinkAsync(customerA, linkA.Address!.LinkId, CancellationToken.None));

        Assert.Empty(await h.Sut.ListForCustomerAsync(customerA, true, CancellationToken.None));
        Assert.Single(await h.Sut.ListForCustomerAsync(customerB, false, CancellationToken.None));
        // The physical address is untouched.
        var address = await h.Db.Context.Locations.FirstAsync(l => l.Id == locationId);
        Assert.False(address.IsDeleted);
        Assert.True(address.IsActive);
    }

    // ---------------------------------------------------------- scenario D

    [Fact]
    public async Task Picker_RanksTheCustomersOwnSharedAddressAboveTheRest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerB = await AddCustomerAsync(h, "Klant B", "KL-2");
        var shared = await AddAddressAsync(h, "ADR-1", "Gedeeld magazijn");
        var other = await AddAddressAsync(h, "ADR-2", "Ander adres", street: "Zuidlaan", houseNumber: "5", city: "Gent", postalCode: "9000");
        await h.Sut.LinkAsync(customerB, LinkRequest(shared), CancellationToken.None);

        var options = await h.Sut.PickerAsync(customerB, null, 50, CancellationToken.None);

        Assert.Equal(shared, options[0].LocationId);
        Assert.Equal(AddressPickerGroup.CustomerAddress, options[0].Group);
        Assert.Equal(AddressPickerGroup.All, options.Single(o => o.LocationId == other).Group);
    }

    // ---------------------------------------------------------- scenario F

    [Fact]
    public async Task EditingTheCentralAddress_DoesNotRewriteAHistoricalStopSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await AddCustomerAsync(h, "Klant A", "KL-1");
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");

        var order = new TransportOrder
        {
            Id = Guid.NewGuid(),
            TenantId = h.TenantId,
            OrderNumber = "TO-1",
            CustomerId = customerId,
            OrderDate = new DateOnly(2026, 8, 1),
        };
        h.Db.Context.Add(order);
        await h.Db.Context.SaveChangesAsync();

        // A historical stop that froze the address as it was agreed at order time.
        var stop = new TransportOrderStop
        {
            Id = Guid.NewGuid(),
            TenantId = h.TenantId,
            TransportOrderId = order.Id,
            Sequence = 1,
            StopType = StopType.Loading,
            LocationId = locationId,
            LocationName = "Magazijn Noord",
            Address = "Noorderlaan 10",
            PostalCode = "2030",
            City = "Antwerpen",
            CountryCode = "BE",
            SnapshotAt = Now.UtcDateTime,
        };
        h.Db.Context.Add(stop);
        await h.Db.Context.SaveChangesAsync();

        // The master address moves.
        var detail = await h.Locations.GetByIdAsync(locationId, CancellationToken.None, true);
        var update = await h.Locations.UpdateAsync(locationId, new UpdateLocationRequest(
            detail!.Code, detail.Name, detail.Type,
            Street: "Zuidlaan", HouseNumber: "99", PostalCode: "9000", City: "Gent", CountryCode: "BE",
            Latitude: null, Longitude: null,
            ContactName: null, ContactPhone: null, ContactEmail: null,
            OpeningHours: null, LoadingInstructions: null, UnloadingInstructions: null, AccessInstructions: null,
            AccessRestrictions: null, VehicleRestrictions: null, TrailerRestrictions: null,
            AlfapassRequired: false, AppointmentRequired: false, IsActive: true, CustomerId: null, Notes: null),
            CancellationToken.None, true);
        Assert.Equal(LocationOperationOutcome.Success, update.Outcome);

        var frozen = await h.Db.Context.Set<TransportOrderStop>().AsNoTracking().FirstAsync(s => s.Id == stop.Id);
        Assert.Equal("Noorderlaan 10", frozen.Address);
        Assert.Equal("Antwerpen", frozen.City);
        Assert.Equal("2030", frozen.PostalCode);
    }

    // ------------------------------------------------ defaults + legacy sync

    [Fact]
    public async Task DefaultLoading_IsUniquePerCustomer_AndDemotesThePreviousHolder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await AddCustomerAsync(h, "Klant A", "KL-1");
        var first = await AddAddressAsync(h, "ADR-1", "Eerste");
        var second = await AddAddressAsync(h, "ADR-2", "Tweede", street: "Zuidlaan", houseNumber: "5", city: "Gent", postalCode: "9000");

        await h.Sut.LinkAsync(customerA, LinkRequest(first, defaultLoading: true), CancellationToken.None);
        await h.Sut.LinkAsync(customerA, LinkRequest(second, defaultLoading: true), CancellationToken.None);

        var links = await h.Sut.ListForCustomerAsync(customerA, true, CancellationToken.None);
        Assert.Single(links, l => l.IsDefaultLoading);
        Assert.Equal(second, links.Single(l => l.IsDefaultLoading).LocationId);
    }

    [Fact]
    public async Task LegacyOwnerColumn_TracksTheOldestRelationship_AndClearsWhenTheLastGoes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await AddCustomerAsync(h, "Klant A", "KL-1");
        var customerB = await AddCustomerAsync(h, "Klant B", "KL-2");
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");

        var linkA = await h.Sut.LinkAsync(customerA, LinkRequest(locationId), CancellationToken.None);
        await h.Sut.LinkAsync(customerB, LinkRequest(locationId), CancellationToken.None);

        // Two customers share it; the legacy column keeps pointing at the original owner.
        Assert.Equal(customerA, (await h.Db.Context.Locations.AsNoTracking().FirstAsync(l => l.Id == locationId)).CustomerId);

        await h.Sut.UnlinkAsync(customerA, linkA.Address!.LinkId, CancellationToken.None);
        Assert.Equal(customerB, (await h.Db.Context.Locations.AsNoTracking().FirstAsync(l => l.Id == locationId)).CustomerId);

        var linkB = Assert.Single(await h.Sut.ListForCustomerAsync(customerB, true, CancellationToken.None));
        await h.Sut.UnlinkAsync(customerB, linkB.LinkId, CancellationToken.None);
        Assert.Null((await h.Db.Context.Locations.AsNoTracking().FirstAsync(l => l.Id == locationId)).CustomerId);
    }

    [Fact]
    public async Task Link_RejectsACustomerFromAnotherTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var locationId = await AddAddressAsync(h, "ADR-1", "Magazijn Noord");

        var foreignTenant = Guid.NewGuid();
        var foreign = new Customer { Id = Guid.NewGuid(), TenantId = foreignTenant, Name = "Vreemde", CustomerNumber = "X-1" };
        h.Db.Context.Customers.Add(foreign);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.LinkAsync(foreign.Id, LinkRequest(locationId), CancellationToken.None);

        Assert.Equal(CustomerAddressOutcome.InvalidReference, result.Outcome);
    }
}
