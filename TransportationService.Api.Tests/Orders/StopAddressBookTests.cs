using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
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

namespace TransportationService.Api.Tests.Orders;

/// <summary>
/// D3 (2026-09-21): the stop snapshot is the dossier-level address override
/// (<c>AddressOverridden</c>), "Opslaan in adresboek" rides in the order's own save, and the
/// postal-code/country checks only ever apply to new or changed values.
/// </summary>
public class StopAddressBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync(bool mayCreateLocations = true, bool seedCountries = false)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Magazijn Antwerpen",
            Street = "Noorderlaan", HouseNumber = "10", PostalCode = "2030", City = "Antwerpen", CountryCode = "BE",
            AddressExactKey = AddressNormalizer.ExactKey("BE", "2030", "Antwerpen", "Noorderlaan", "10"),
            AddressStreetKey = AddressNormalizer.StreetKey("BE", "2030", "Antwerpen", "Noorderlaan"),
            Type = LocationType.Warehouse, IsActive = true, ContactName = "Magazijnier Piet", Gate = "Poort B",
        });
        await db.Context.SaveChangesAsync();
        if (seedCountries)
        {
            await CountrySeeder.SyncAsync(db.Context);
        }

        var tenant = new DevTenantContext(tenantId);
        var sut = new TransportOrderService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new TestClock(Now),
            currentUser: new DevCurrentUserContext(Guid.NewGuid()),
            permissionService: mayCreateLocations
                ? new InventoryTestFactory.AllowAllPermissionService()
                : new InventoryTestFactory.DenyAllPermissionService(),
            countryValidator: seedCountries ? new CountryCodeValidator(db.Context) : null);
        return new Harness(db, sut, tenantId, customerId, locationId);
    }

    private static TransportOrderStopInput FreeStop(
        StopType type, string? name, string? address, string? postalCode, string? city, string? country = "BE",
        bool saveToAddressBook = false) =>
        new(type, null, name, address, postalCode, city, country, null, null, null, null,
            SaveToAddressBook: saveToAddressBook);

    private static CreateTransportOrderRequest Request(Guid customerId, params TransportOrderStopInput[] stops) => new(
        customerId, "PO-1", new DateOnly(2026, 9, 22), "10 paletten", 10, "paletten", 5000, null, 10, false, false, 900m, null, stops);

    private static UpdateTransportOrderRequest UpdateFrom(TransportOrderDetailDto d, IReadOnlyList<TransportOrderStopInput> stops) => new(
        d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
        d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
        d.AgreedPrice, d.Notes, stops, QuantityUnitCode: d.QuantityUnitCode);

    /// <summary>What the frontend echoes for an existing stop (id included, flags as stored).</summary>
    private static TransportOrderStopInput Echo(TransportOrderStopDto s) => new(
        s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
        s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions,
        Id: s.Id, AddressOverridden: s.AddressOverridden);

    private static Task<Location> LocationRowAsync(Harness h) =>
        h.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == h.LocationId);

    // ------------------------------------------------------------ override

    [Fact]
    public async Task Override_StoresTheEditedAddressOnTheStop_AndLeavesTheLocationUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var before = await LocationRowAsync(h);

        var overridden = new TransportOrderStopInput(
            StopType.Loading, h.LocationId, "Magazijn Antwerpen (poort C)", "Noorderlaan 12", "2030", "Antwerpen", "BE",
            null, null, null, null, AddressOverridden: true);
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, overridden, FreeStop(StopType.Unloading, null, null, null, "Gent")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var stop = created.Order!.Stops[0];
        Assert.True(stop.AddressOverridden);
        Assert.Equal(h.LocationId, stop.LocationId);
        Assert.Equal("Magazijn Antwerpen (poort C)", stop.LocationName);
        Assert.Equal("Noorderlaan 12", stop.Address);
        // The rest of the snapshot still comes from the location.
        Assert.Equal("Magazijnier Piet", stop.ContactName);
        Assert.Equal("Poort B", stop.Gate);

        var after = await LocationRowAsync(h);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.HouseNumber, after.HouseNumber);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync());
    }

    [Fact]
    public async Task Override_IsStillThere_WhenTheOrderIsReopenedAndSavedAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, h.LocationId, "Magazijn Antwerpen", "Noorderlaan 12", "2030", "Antwerpen", "BE",
                null, null, null, null, AddressOverridden: true),
            FreeStop(StopType.Unloading, null, null, null, "Gent")), CancellationToken.None);

        // Reopen: the hint "afwijkend van adresboek" must come back from a plain GET …
        var reopened = (await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None))!;
        Assert.True(reopened.Stops[0].AddressOverridden);
        Assert.Equal("Noorderlaan 12", reopened.Stops[0].Address);

        // … and an unchanged save (the client echoes the quintet + the flag) keeps it.
        var saved = await h.Sut.UpdateAsync(reopened.Id, UpdateFrom(reopened, reopened.Stops.Select(Echo).ToList()), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, saved.Outcome);
        Assert.True(saved.Order!.Stops[0].AddressOverridden);
        Assert.Equal("Noorderlaan 12", saved.Order.Stops[0].Address);
        Assert.Equal("10", (await LocationRowAsync(h)).HouseNumber);
    }

    [Fact]
    public async Task SwitchingTheOverrideOff_LetsTheAddressBookWinAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, h.LocationId, "Eigen naam", "Noorderlaan 12", "2030", "Antwerpen", "BE",
                null, null, null, null, AddressOverridden: true),
            FreeStop(StopType.Unloading, null, null, null, "Gent")), CancellationToken.None);

        // addressOverridden: false while the client still echoes the edited quintet (no RefreshSnapshot).
        var echoed = created.Order!.Stops.Select(s => Echo(s) with { AddressOverridden = false }).ToList();
        var updated = await h.Sut.UpdateAsync(created.Order.Id, UpdateFrom(created.Order, echoed), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var stop = updated.Order!.Stops[0];
        Assert.False(stop.AddressOverridden);
        Assert.Equal("Magazijn Antwerpen", stop.LocationName);
        Assert.Equal("Noorderlaan 10", stop.Address);
    }

    [Fact]
    public async Task RefreshSnapshot_RecopiesTheLocationAddress_AndClearsTheFlag()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, h.LocationId, "Eigen naam", "Noorderlaan 12", "2030", "Antwerpen", "BE",
                null, null, null, null, AddressOverridden: true),
            FreeStop(StopType.Unloading, null, null, null, "Gent")), CancellationToken.None);

        var stops = created.Order!.Stops.Select(Echo).ToList();
        stops[0] = stops[0] with { RefreshSnapshot = true }; // still says AddressOverridden = true: refresh wins
        var updated = await h.Sut.UpdateAsync(created.Order.Id, UpdateFrom(created.Order, stops), CancellationToken.None);

        var stop = updated.Order!.Stops[0];
        Assert.False(stop.AddressOverridden);
        Assert.Equal("Magazijn Antwerpen", stop.LocationName);
        Assert.Equal("Noorderlaan 10", stop.Address);
    }

    [Fact]
    public async Task NotOverridden_KeepsTodaysBehaviour_TheLocationAddressWins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, h.LocationId, "Iets anders", "Elders 99", "9000", "Gent", "BE",
                null, null, null, null),
            FreeStop(StopType.Unloading, null, null, null, "Gent")), CancellationToken.None);

        var stop = created.Order!.Stops[0];
        Assert.False(stop.AddressOverridden);
        Assert.Equal("Magazijn Antwerpen", stop.LocationName);
        Assert.Equal("Noorderlaan 10", stop.Address);
    }

    // --------------------------------------------------- save to address book

    [Fact]
    public async Task SaveToAddressBookOff_CreatesNoLocation_AndKeepsTheAddressOnTheStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf Peeters", "Kaai 12", "9000", "Gent"),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync()); // only the seeded one
        var stop = created.Order!.Stops[0];
        Assert.Null(stop.LocationId);
        Assert.Equal("Kaai 12", stop.Address);
        Assert.Equal(AddressBookOutcome.None, stop.AddressBookOutcome);
    }

    [Fact]
    public async Task SaveToAddressBookOn_CreatesLocationAndCustomerLink_AndLinksTheStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf Peeters", "Kaai 12 bus 3", "9000", "Gent", saveToAddressBook: true),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var stop = created.Order!.Stops[0];
        Assert.Equal(AddressBookOutcome.Created, stop.AddressBookOutcome);
        Assert.NotNull(stop.LocationId);
        Assert.False(stop.AddressOverridden);
        Assert.NotNull(stop.SnapshotAt);
        Assert.Equal("Kaai 12 bus 3", stop.Address);

        var location = await h.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == stop.LocationId);
        Assert.Equal("Werf Peeters", location.Name);
        Assert.Equal("Kaai", location.Street);
        Assert.Equal("12 bus 3", location.HouseNumber);
        Assert.Equal("9000", location.PostalCode);
        Assert.Equal("BE", location.CountryCode);
        Assert.StartsWith("LOC-", location.Code);
        Assert.True(location.IsActive);
        Assert.Equal(h.CustomerId, location.CustomerId);
        Assert.Equal(AddressNormalizer.ExactKey("BE", "9000", "Gent", "Kaai", "12 bus 3"), location.AddressExactKey);

        var link = await h.Db.Context.CustomerLocationLinks.AsNoTracking().SingleAsync(l => l.LocationId == location.Id);
        Assert.Equal(h.CustomerId, link.CustomerId);
        Assert.True(link.IsActive);
    }

    [Fact]
    public async Task SavingAgain_CreatesNothingNew_Idempotent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf Peeters", "Kaai 12", "9000", "Gent", saveToAddressBook: true),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);
        var locationId = created.Order!.Stops[0].LocationId;

        // The client saves the same form again, flag still ticked.
        var stops = created.Order.Stops.Select(Echo).ToList();
        stops[0] = stops[0] with { SaveToAddressBook = true };
        var updated = await h.Sut.UpdateAsync(created.Order.Id, UpdateFrom(created.Order, stops), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(locationId, updated.Order!.Stops[0].LocationId);
        Assert.Equal(AddressBookOutcome.None, updated.Order.Stops[0].AddressBookOutcome);
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync(l => l.Street == "Kaai"));
        Assert.Equal(1, await h.Db.Context.CustomerLocationLinks.CountAsync(l => l.LocationId == locationId));
    }

    [Fact]
    public async Task ExactDuplicate_LinksTheExistingAddress_InsteadOfCreatingOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Same front door as the seeded location, typed differently.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Magazijn (getypt)", "noorderlaan  10", "B-2030", "ANTWERPEN", saveToAddressBook: true),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var stop = created.Order!.Stops[0];
        Assert.Equal(AddressBookOutcome.LinkedExisting, stop.AddressBookOutcome);
        Assert.Equal(h.LocationId, stop.LocationId);
        Assert.Equal("Magazijn Antwerpen", stop.LocationName); // fresh snapshot of the existing address
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync());

        var link = await h.Db.Context.CustomerLocationLinks.AsNoTracking().SingleAsync();
        Assert.Equal((h.CustomerId, h.LocationId), (link.CustomerId, link.LocationId));
        // The central address row itself is never modified by an order save.
        Assert.Null((await LocationRowAsync(h)).CustomerId);
    }

    [Fact]
    public async Task TwoStopsWithTheSameNewAddress_CreateOneLocation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf", "Kaai 12", "9000", "Gent", saveToAddressBook: true),
            FreeStop(StopType.Unloading, "Werf", "Kaai 12", "9000", "Gent", saveToAddressBook: true)), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal(created.Order!.Stops[0].LocationId, created.Order.Stops[1].LocationId);
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync(l => l.Street == "Kaai"));
    }

    [Fact]
    public async Task OverriddenStop_SavedToAddressBook_BecomesItsOwnAddress()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, h.LocationId, "Magazijn poort C", "Noorderlaan 14", "2030", "Antwerpen", "BE",
                null, null, null, null, AddressOverridden: true, SaveToAddressBook: true),
            FreeStop(StopType.Unloading, null, null, null, "Gent")), CancellationToken.None);

        var stop = created.Order!.Stops[0];
        Assert.Equal(AddressBookOutcome.Created, stop.AddressBookOutcome);
        Assert.NotEqual(h.LocationId, stop.LocationId);
        Assert.False(stop.AddressOverridden);
        Assert.Equal("10", (await LocationRowAsync(h)).HouseNumber); // the original address is untouched
    }

    [Fact]
    public async Task MissingPermission_IsRefused_AndNothingIsCreated()
    {
        var h = await SeedAsync(mayCreateLocations: false);
        using var _ = h.Db;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf Peeters", "Kaai 12", "9000", "Gent", saveToAddressBook: true),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None));

        Assert.True(exception.FieldErrors!.ContainsKey("stops[0].saveToAddressBook"));
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync());
        Assert.Equal(0, await h.Db.Context.CustomerLocationLinks.CountAsync());
        Assert.Equal(0, await h.Db.Context.TransportOrders.CountAsync());
    }

    [Fact]
    public async Task FailedAddressBookSave_IsAFieldErrorOnThatStopsCheckbox_AndNothingIsSaved()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // The SECOND stop of the request (zero-based index 1) cannot enter the address book: no street.
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf Peeters", "Kaai 12", "9000", "Gent", saveToAddressBook: true),
            FreeStop(StopType.Unloading, "Werf", null, null, "Brugge", saveToAddressBook: true)), CancellationToken.None));

        Assert.Equal(["stops[1].saveToAddressBook"], exception.FieldErrors!.Keys);
        // No partial success: not even the first (valid) stop's address was created.
        Assert.Equal(0, await h.Db.Context.TransportOrders.CountAsync());
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync());
        Assert.Equal(0, await h.Db.Context.CustomerLocationLinks.CountAsync());
    }

    [Fact]
    public async Task UnchangedLegacyPostalCode_MayStayOnTheStop_ButCannotEnterTheAddressBook()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf", "Kaai 12", "3080", "Tervuren"),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);
        var row = await h.Db.Context.TransportOrderStops.SingleAsync(s => s.Id == created.Order!.Stops[0].Id);
        row.PostalCode = "30800"; // legacy value from before the check
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();
        var current = (await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None))!;

        var stops = current.Stops.Select(Echo).ToList();
        stops[0] = stops[0] with { SaveToAddressBook = true };
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpdateAsync(current.Id, UpdateFrom(current, stops), CancellationToken.None));

        Assert.Equal("Een Belgische postcode heeft 4 cijfers.", exception.FieldErrors!["stops[0].saveToAddressBook"][0]);
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync());
    }

    [Fact]
    public async Task AddressBookLocation_NeverLandsInAnotherTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Another tenant already owns the very same front door.
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        var foreignLocationId = Guid.NewGuid();
        h.Db.Context.Locations.Add(new Location
        {
            Id = foreignLocationId, TenantId = otherTenantId, Code = "LOC-X", Name = "Vreemd", Street = "Kaai", HouseNumber = "12",
            PostalCode = "9000", City = "Gent", CountryCode = "BE", IsActive = true, Type = LocationType.Warehouse,
            AddressExactKey = AddressNormalizer.ExactKey("BE", "9000", "Gent", "Kaai", "12"),
            AddressStreetKey = AddressNormalizer.StreetKey("BE", "9000", "Gent", "Kaai"),
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, "Werf", "Kaai 12", "9000", "Gent", saveToAddressBook: true),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);

        var stop = created.Order!.Stops[0];
        Assert.Equal(AddressBookOutcome.Created, stop.AddressBookOutcome); // the foreign duplicate is invisible
        Assert.NotEqual(foreignLocationId, stop.LocationId);
        var location = await h.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == stop.LocationId);
        Assert.Equal(h.TenantId, location.TenantId);
    }

    // ------------------------------------------------------ postal code / country

    [Fact]
    public async Task NewStop_WithAnImpossibleBelgianPostalCode_IsRefusedAsAFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, null, "Kaai 12", "30800", "Boortmeerbeek"),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None));

        Assert.Equal("Een Belgische postcode heeft 4 cijfers.", exception.FieldErrors!["stops[0].postalCode"][0]);
    }

    [Fact]
    public async Task UnchangedLegacyInvalidPostalCode_NeverBlocksASave_ButChangingItIsChecked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, null, "Kaai 12", "3080", "Tervuren"),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);
        // Legacy data: the stored value predates the check.
        var row = await h.Db.Context.TransportOrderStops.SingleAsync(s => s.Id == created.Order!.Stops[0].Id);
        row.PostalCode = "30800";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var current = (await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None))!;
        var echoed = current.Stops.Select(Echo).ToList();
        echoed[0] = echoed[0] with { Reference = "REF-1" }; // an unrelated edit
        var unrelated = await h.Sut.UpdateAsync(current.Id, UpdateFrom(current, echoed), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, unrelated.Outcome);
        Assert.Equal("30800", unrelated.Order!.Stops[0].PostalCode);

        var changed = unrelated.Order.Stops.Select(Echo).ToList();
        changed[0] = changed[0] with { PostalCode = "30801" };
        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpdateAsync(current.Id, UpdateFrom(unrelated.Order, changed), CancellationToken.None));
        Assert.True(exception.FieldErrors!.ContainsKey("stops[0].postalCode"));
    }

    [Fact]
    public async Task UnknownCountry_SkipsTheFormatCheck_ButAnUnknownCountryCodeIsRefused()
    {
        var h = await SeedAsync(seedCountries: true);
        using var _ = h.Db;

        var british = await h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, null, "Downing Street 10", "SW1A 2AA", "London", "GB"),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, british.Outcome);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(Request(h.CustomerId,
            FreeStop(StopType.Loading, null, "Kaai 12", "9000", "Gent", "ZZ"),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None));
        Assert.True(exception.FieldErrors!.ContainsKey("stops[0].countryCode"));
    }

    [Fact]
    public async Task MasterLocationStop_WithALegacyInvalidPostalCodeOnTheLocation_IsNotBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var location = await h.Db.Context.Locations.SingleAsync(l => l.Id == h.LocationId);
        location.PostalCode = "20300"; // legacy master data
        await h.Db.Context.SaveChangesAsync();

        // The client echoes the location's own (invalid) value; the address is copied from the location anyway.
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, h.LocationId, null, "Noorderlaan 10", "20300", "Antwerpen", "BE",
                null, null, null, null),
            FreeStop(StopType.Unloading, null, null, null, "Brugge")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
    }
}
