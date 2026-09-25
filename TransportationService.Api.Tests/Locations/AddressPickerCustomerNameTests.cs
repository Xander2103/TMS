using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Locations;

/// <summary>
/// D3: the address picker names the owning customer per result, never leaks another tenant, and
/// the address master format-checks new/changed postal codes only.
/// </summary>
public class AddressPickerCustomerNameTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, CustomerAddressService Sut, LocationService Locations, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);
        return ForTenant(db, tenantId);
    }

    private static Harness ForTenant(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        return new Harness(db, new CustomerAddressService(db.Context, tenant, audit),
            new LocationService(db.Context, tenant, audit, new CountryCodeValidator(db.Context)), tenantId);
    }

    private static async Task<Guid> AddCustomerAsync(Harness h, string name, string number)
    {
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = h.TenantId, Name = name, CustomerNumber = number, IsActive = true };
        h.Db.Context.Customers.Add(customer);
        await h.Db.Context.SaveChangesAsync();
        return customer.Id;
    }

    private static CreateLocationRequest AddressRequest(
        string name, string houseNumber, string postalCode = "2030", string country = "BE", string street = "Noorderlaan") =>
        new(null, name, LocationType.CustomerLocation,
            Street: street, HouseNumber: houseNumber, PostalCode: postalCode, City: "Antwerpen", CountryCode: country,
            Latitude: null, Longitude: null, ContactName: null, ContactPhone: null, ContactEmail: null,
            OpeningHours: null, LoadingInstructions: null, UnloadingInstructions: null, AccessInstructions: null,
            AccessRestrictions: null, VehicleRestrictions: null, TrailerRestrictions: null,
            AlfapassRequired: false, AppointmentRequired: false, CustomerId: null, Notes: null);

    private static async Task<Guid> AddAddressAsync(Harness h, string name, string houseNumber)
    {
        var result = await h.Locations.CreateAsync(AddressRequest(name, houseNumber), CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, result.Outcome);
        return result.Location!.Id;
    }

    private static LinkCustomerAddressRequest Link(Guid locationId, bool defaultLoading = false) =>
        new(locationId, Alias: null, CustomerReference: null, Role: CustomerLocationRole.Both,
            IsDefaultLoading: defaultLoading, IsDefaultUnloading: false, IsDefaultBilling: false, Instructions: null);

    [Fact]
    public async Task Picker_ReturnsTheOwningCustomerName_AndNullWithoutALink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await AddCustomerAsync(h, "Haven BV", "KL-1");
        var linked = await AddAddressAsync(h, "Magazijn", "10");
        var unlinked = await AddAddressAsync(h, "Niemandsland", "20");
        await h.Sut.LinkAsync(customer, Link(linked), CancellationToken.None);

        var options = await h.Sut.PickerAsync(null, null, 50, null, CancellationToken.None);

        Assert.Equal("Haven BV", options.Single(o => o.LocationId == linked).CustomerName);
        Assert.Null(options.Single(o => o.LocationId == unlinked).CustomerName);
    }

    [Fact]
    public async Task Picker_SharedAddress_NamesTheDefaultHoldingLink_ElseTheOldest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await AddCustomerAsync(h, "Zeta NV", "KL-1");
        var second = await AddCustomerAsync(h, "Alfa BV", "KL-2");
        var oldestWins = await AddAddressAsync(h, "Gedeeld A", "10");
        var defaultWins = await AddAddressAsync(h, "Gedeeld B", "12");
        await h.Sut.LinkAsync(first, Link(oldestWins), CancellationToken.None);
        await h.Sut.LinkAsync(second, Link(oldestWins), CancellationToken.None);
        await h.Sut.LinkAsync(first, Link(defaultWins), CancellationToken.None);
        await h.Sut.LinkAsync(second, Link(defaultWins, defaultLoading: true), CancellationToken.None);
        // Make "oldest" unambiguous regardless of clock resolution.
        foreach (var link in h.Db.Context.CustomerLocationLinks.Where(l => l.CustomerId == second))
        {
            link.CreatedAt = link.CreatedAt.AddMinutes(5);
        }

        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        // Ranking param stays intact: the dossier customer's addresses still come first.
        var options = await h.Sut.PickerAsync(second, null, 50, null, CancellationToken.None);

        Assert.Equal("Zeta NV", options.Single(o => o.LocationId == oldestWins).CustomerName);
        Assert.Equal("Alfa BV", options.Single(o => o.LocationId == defaultWins).CustomerName);
        Assert.Equal("Alfa BV, Zeta NV", options.Single(o => o.LocationId == oldestWins).CustomerNames);
        Assert.All(options, o => Assert.Equal(AddressPickerGroup.CustomerAddress, o.Group));
    }

    [Fact]
    public async Task Picker_NeverReturnsAnotherTenantsAddress_OrCustomerName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        var other = ForTenant(h.Db, otherTenantId);
        var foreignCustomer = await AddCustomerAsync(other, "Geheime Klant", "KL-9");
        var foreignAddress = await AddAddressAsync(other, "Geheim Magazijn", "99");
        await other.Sut.LinkAsync(foreignCustomer, Link(foreignAddress), CancellationToken.None);
        var own = await AddAddressAsync(h, "Eigen Magazijn", "10");

        var options = await h.Sut.PickerAsync(null, "magazijn", 50, null, CancellationToken.None);

        var option = Assert.Single(options);
        Assert.Equal(own, option.LocationId);
        Assert.Null(option.CustomerName);
        // Asking with the foreign customer id does not help either.
        var asForeign = await h.Sut.PickerAsync(foreignCustomer, null, 50, null, CancellationToken.None);
        Assert.DoesNotContain(asForeign, o => o.LocationId == foreignAddress);
        Assert.DoesNotContain(asForeign, o => o.CustomerName == "Geheime Klant");
    }

    [Fact]
    public async Task CreateAddress_WithAnImpossibleBelgianPostalCode_IsAFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Locations.CreateAsync(AddressRequest("Fout", "1", postalCode: "30800"), CancellationToken.None));

        Assert.Equal("Een Belgische postcode heeft 4 cijfers.", exception.FieldErrors!["postalCode"][0]);
        Assert.Equal(LocationOperationOutcome.Success,
            (await h.Locations.CreateAsync(AddressRequest("Goed", "1", postalCode: "3080"), CancellationToken.None)).Outcome);
        Assert.Equal(LocationOperationOutcome.Success,
            (await h.Locations.CreateAsync(AddressRequest("Brits", "2", postalCode: "SW1A 2AA", country: "GB"), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task UpdateAddress_WithAnUnchangedLegacyPostalCode_IsNotBlocked_ButAChangeIsChecked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var id = await AddAddressAsync(h, "Oud adres", "10");
        var row = await h.Db.Context.Locations.FindAsync(id);
        row!.PostalCode = "20300"; // legacy value from before the check
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        UpdateLocationRequest Update(string name, string postalCode) => new(
            row.Code, name, LocationType.CustomerLocation, "Noorderlaan", "10", postalCode, "Antwerpen", "BE",
            null, null, null, null, null, null, null, null, null, null, null, null,
            AlfapassRequired: false, AppointmentRequired: false, IsActive: true, CustomerId: null, Notes: null);

        var renamed = await h.Locations.UpdateAsync(id, Update("Nieuwe naam", "20300"), CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, renamed.Outcome);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Locations.UpdateAsync(id, Update("Nieuwe naam", "20301"), CancellationToken.None));
        Assert.True(exception.FieldErrors!.ContainsKey("postalCode"));
    }
}
