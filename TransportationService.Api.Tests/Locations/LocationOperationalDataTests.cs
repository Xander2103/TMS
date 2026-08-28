using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
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
/// Master-data wave 2026-08-05: operational location fields, structured opening hours,
/// sensitive access-code gating, delete protection and duplication.
/// </summary>
public class LocationOperationalDataTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 05, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, LocationService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var sut = new LocationService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new CountryCodeValidator(db.Context));
        return new Harness(db, sut, tenantId);
    }

    private static async Task<Guid> SeedCustomerAsync(Harness h, string number = "KL-0001", string name = "Klant BV")
    {
        var customerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = customerId, TenantId = h.TenantId, CustomerNumber = number, Name = name, IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        return customerId;
    }

    private static async Task<Guid> SeedContactAsync(Harness h, Guid customerId, Guid? tenantId = null)
    {
        var contactId = Guid.NewGuid();
        h.Db.Context.Set<CustomerContact>().Add(new CustomerContact
        {
            Id = contactId, TenantId = tenantId ?? h.TenantId, CustomerId = customerId,
            FirstName = "An", LastName = "Peeters", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();
        return contactId;
    }

    /// <summary>Every operational field filled, two Monday intervals, Saturday deliberately closed.</summary>
    private static CreateLocationRequest FullRequest(string? code = "OPS-001", Guid? customerId = null, Guid? contactId = null) => new(
        code, "Site Antwerpen", LocationType.CustomerLocation,
        Street: "Havenlaan", HouseNumber: "12", PostalCode: "2030", City: "Antwerpen", CountryCode: "be",
        Latitude: 51.22m, Longitude: 4.40m,
        ContactName: "Jan", ContactPhone: "+32 3 111 11 11", ContactEmail: "jan@klant.be",
        OpeningHours: "vrije tekst blijft fallback", LoadingInstructions: "Laden achteraan.",
        UnloadingInstructions: "Lossen vooraan.", AccessInstructions: "Aanmelden aan de balie.",
        AccessRestrictions: null, VehicleRestrictions: null, TrailerRestrictions: null,
        AlfapassRequired: false, AppointmentRequired: true, CustomerId: customerId, Notes: "nota",
        ExternalReference: "EXT-42", ContactMobile: "+32 470 00 00 01", CustomerContactId: contactId,
        Gate: "Poort 4", AccessCode: "1234#A", ReceptionPoint: "Balie B", Dock: "Kade 12",
        RouteDescription: "Via de Noorderlaan, tweede afslag rechts.",
        DeliveryByAppointmentOnly: true,
        HeightRestrictionMeters: 4.20m, WeightRestrictionTons: 44.50m,
        AdrAllowed: true, CraneRequired: true, ForkliftAvailable: true,
        DriverInstructions: "Meld je aan bij de weegbrug.", InternalMemo: "Alleen intern zichtbaar.",
        DefaultLoadingMinutes: 45, DefaultUnloadingMinutes: 30,
        PreferredArrivalFrom: "08:00", PreferredArrivalTo: "11:00",
        EarliestArrival: "06:30", LatestArrival: "16:30",
        OpeningIntervals:
        [
            new LocationOpeningIntervalDto(1, "07:00", "12:00"),
            new LocationOpeningIntervalDto(1, "13:00", "17:00", "namiddagblok"),
        ]);

    private static UpdateLocationRequest ToUpdate(LocationDetailDto d) => new(
        d.Code, d.Name, d.Type, d.Street, d.HouseNumber, d.PostalCode, d.City, d.CountryCode,
        d.Latitude, d.Longitude, d.ContactName, d.ContactPhone, d.ContactEmail,
        d.OpeningHours, d.LoadingInstructions, d.UnloadingInstructions, d.AccessInstructions,
        d.AccessRestrictions, d.VehicleRestrictions, d.TrailerRestrictions,
        d.AlfapassRequired, d.AppointmentRequired, d.IsActive, d.CustomerId, d.Notes,
        d.IsDefaultLoadingLocation, d.IsDefaultUnloadingLocation, d.IsDefaultBillingLocation,
        d.ExternalReference, d.ContactMobile, d.CustomerContactId, d.Gate, d.AccessCode,
        d.ReceptionPoint, d.Dock, d.RouteDescription, d.DeliveryByAppointmentOnly,
        d.HeightRestrictionMeters, d.WeightRestrictionTons, d.AdrAllowed, d.CraneRequired,
        d.ForkliftAvailable, d.DriverInstructions, d.InternalMemo,
        d.DefaultLoadingMinutes, d.DefaultUnloadingMinutes,
        d.PreferredArrivalFrom, d.PreferredArrivalTo, d.EarliestArrival, d.LatestArrival,
        d.OpeningIntervals);

    [Fact]
    public async Task Create_FullOperationalFields_RoundTripToDetail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var contactId = await SeedContactAsync(h, customerId);

        var result = await h.Sut.CreateAsync(FullRequest(customerId: customerId, contactId: contactId),
            CancellationToken.None, canViewSensitive: true);

        Assert.Equal(LocationOperationOutcome.Success, result.Outcome);
        var d = result.Location!;
        Assert.Equal("EXT-42", d.ExternalReference);
        Assert.Equal("+32 470 00 00 01", d.ContactMobile);
        Assert.Equal(contactId, d.CustomerContactId);
        Assert.Equal("Poort 4", d.Gate);
        Assert.Equal("1234#A", d.AccessCode);
        Assert.Equal("Balie B", d.ReceptionPoint);
        Assert.Equal("Kade 12", d.Dock);
        Assert.Equal("Via de Noorderlaan, tweede afslag rechts.", d.RouteDescription);
        Assert.True(d.DeliveryByAppointmentOnly);
        Assert.Equal(4.20m, d.HeightRestrictionMeters);
        Assert.Equal(44.50m, d.WeightRestrictionTons);
        Assert.True(d.AdrAllowed);
        Assert.True(d.CraneRequired);
        Assert.True(d.ForkliftAvailable);
        Assert.Equal("Meld je aan bij de weegbrug.", d.DriverInstructions);
        Assert.Equal("Alleen intern zichtbaar.", d.InternalMemo);
        Assert.Equal(45, d.DefaultLoadingMinutes);
        Assert.Equal(30, d.DefaultUnloadingMinutes);
        Assert.Equal("08:00", d.PreferredArrivalFrom);
        Assert.Equal("11:00", d.PreferredArrivalTo);
        Assert.Equal("06:30", d.EarliestArrival);
        Assert.Equal("16:30", d.LatestArrival);
        Assert.Equal("vrije tekst blijft fallback", d.OpeningHours);

        // Two Monday intervals, Saturday closed (= simply absent).
        Assert.Equal(2, d.OpeningIntervals!.Count);
        Assert.All(d.OpeningIntervals, i => Assert.Equal(1, i.DayOfWeek));
        Assert.Equal(("07:00", "12:00"), (d.OpeningIntervals[0].FromTime, d.OpeningIntervals[0].ToTime));
        Assert.Equal(("13:00", "17:00"), (d.OpeningIntervals[1].FromTime, d.OpeningIntervals[1].ToTime));
        Assert.Equal("namiddagblok", d.OpeningIntervals[1].Note);
        Assert.DoesNotContain(d.OpeningIntervals, i => i.DayOfWeek == 6);

        // GET returns the same picture (interval list compared element-wise: the record's own
        // equality would compare the freshly materialised lists by reference).
        var fetched = await h.Sut.GetByIdAsync(d.Id, CancellationToken.None, canViewSensitive: true);
        Assert.Equal(d with { OpeningIntervals = null, LinkedCustomerNames = null }, fetched! with { OpeningIntervals = null, LinkedCustomerNames = null });
        Assert.Equal(d.OpeningIntervals, fetched.OpeningIntervals!);
    }

    [Fact]
    public async Task Create_BlankCode_GeneratesLocPrefixedCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(FullRequest(code: null), CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.Success, result.Outcome);
        Assert.StartsWith("LOC-", result.Location!.Code);
        Assert.Equal("LOC-".Length + 8, result.Location.Code.Length);
    }

    [Fact]
    public async Task Update_RoundTrip_PreservesFields_AndReplacesIntervalsWholesale()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var created = (await h.Sut.CreateAsync(FullRequest(customerId: customerId), CancellationToken.None, canViewSensitive: true)).Location!;

        var update = ToUpdate(created) with
        {
            Gate = "Poort 9",
            OpeningIntervals = [new LocationOpeningIntervalDto(2, "06:00", "18:00")],
        };
        var updated = (await h.Sut.UpdateAsync(created.Id, update, CancellationToken.None, canViewSensitive: true)).Location!;

        Assert.Equal("Poort 9", updated.Gate);
        var interval = Assert.Single(updated.OpeningIntervals!);
        Assert.Equal((2, "06:00", "18:00"), (interval.DayOfWeek, interval.FromTime, interval.ToTime));

        // Everything not touched by the update stays exactly as created.
        Assert.Equal(created with { Gate = "Poort 9", OpeningIntervals = updated.OpeningIntervals, LinkedCustomerNames = updated.LinkedCustomerNames }, updated);

        // The old interval rows are really gone, not orphaned.
        Assert.Single(h.Db.Context.Set<LocationOpeningInterval>().Where(i => i.LocationId == created.Id).ToList());
    }

    [Fact]
    public async Task OverlappingIntervalsOnTheSameDay_AreRejected_WithFieldPath()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = FullRequest() with
        {
            OpeningIntervals =
            [
                new LocationOpeningIntervalDto(1, "07:00", "12:00"),
                new LocationOpeningIntervalDto(1, "11:30", "17:00"),
            ],
        };
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(request, CancellationToken.None));

        Assert.Equal("De tijdvakken van eenzelfde dag mogen niet overlappen.", ex.Message);
        Assert.Contains("openingIntervals[1].fromTime", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task IntervalEndBeforeStart_IsRejected_WithFieldPath()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = FullRequest() with
        {
            OpeningIntervals = [new LocationOpeningIntervalDto(1, "12:00", "07:00")],
        };
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(request, CancellationToken.None));

        Assert.Equal("De eindtijd moet na de starttijd liggen.", ex.Message);
        Assert.Contains("openingIntervals[0].toTime", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task InvalidDayOfWeek_AndInvalidTime_AreRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var badDay = FullRequest() with { OpeningIntervals = [new LocationOpeningIntervalDto(0, "07:00", "12:00")] };
        var dayEx = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(badDay, CancellationToken.None));
        Assert.Contains("openingIntervals[0].dayOfWeek", dayEx.FieldErrors!.Keys);

        var badTime = FullRequest() with { OpeningIntervals = [new LocationOpeningIntervalDto(1, "7 uur", "12:00")] };
        var timeEx = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(badTime, CancellationToken.None));
        Assert.Contains("openingIntervals[0].fromTime", timeEx.FieldErrors!.Keys);
    }

    [Fact]
    public async Task HandlingMinutesOutsideRange_AreRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = FullRequest() with { DefaultLoadingMinutes = 1441 };
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(request, CancellationToken.None));

        Assert.Contains("defaultLoadingMinutes", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task AccessCode_HiddenWithoutPermission_VisibleWithPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Sut.CreateAsync(FullRequest(), CancellationToken.None, canViewSensitive: true)).Location!;

        var withoutPermission = await h.Sut.GetByIdAsync(created.Id, CancellationToken.None);
        var withPermission = await h.Sut.GetByIdAsync(created.Id, CancellationToken.None, canViewSensitive: true);

        Assert.Null(withoutPermission!.AccessCode);
        Assert.Equal("1234#A", withPermission!.AccessCode);
    }

    [Fact]
    public async Task AccessCode_IsPreserved_WhenUpdatingWithoutPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = (await h.Sut.CreateAsync(FullRequest(), CancellationToken.None, canViewSensitive: true)).Location!;

        // A caller without the permission never sees the code, so their PUT sends null —
        // the stored code must survive untouched (never cleared, never overwritten).
        var update = ToUpdate(created) with { AccessCode = null, Gate = "Poort 5" };
        var result = await h.Sut.UpdateAsync(created.Id, update, CancellationToken.None, canViewSensitive: false);

        Assert.Null(result.Location!.AccessCode);
        var sensitive = await h.Sut.GetByIdAsync(created.Id, CancellationToken.None, canViewSensitive: true);
        Assert.Equal("1234#A", sensitive!.AccessCode);
        Assert.Equal("Poort 5", sensitive.Gate);
    }

    [Fact]
    public async Task AccessCode_IgnoredOnCreate_WithoutPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = (await h.Sut.CreateAsync(FullRequest(), CancellationToken.None, canViewSensitive: false)).Location!;

        var sensitive = await h.Sut.GetByIdAsync(created.Id, CancellationToken.None, canViewSensitive: true);
        Assert.Null(sensitive!.AccessCode);
    }

    [Fact]
    public async Task Delete_IsBlocked_WhenATransportOrderStopReferencesTheLocation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var created = (await h.Sut.CreateAsync(FullRequest(customerId: customerId), CancellationToken.None)).Location!;

        var order = new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, OrderNumber = "ORD-0001",
            CustomerId = customerId, OrderDate = new DateOnly(2026, 8, 5),
        };
        order.Stops.Add(new TransportOrderStop
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = order.Id,
            Sequence = 1, StopType = StopType.Loading, LocationId = created.Id,
        });
        h.Db.Context.Add(order);
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.DeleteAsync(created.Id, CancellationToken.None));

        Assert.Equal("Deze locatie is al gebruikt en kan niet worden verwijderd. Je kunt de locatie wel deactiveren.", ex.Message);
        // Deactivating stays possible.
        Assert.True(await h.Sut.SetActiveAsync(created.Id, new SetLocationActiveRequest(false), CancellationToken.None));
    }

    [Fact]
    public async Task Duplicate_CopiesFieldsAndIntervals_WithNewCodeAndClearedDefaults()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var created = (await h.Sut.CreateAsync(
            FullRequest(customerId: customerId) with { IsDefaultLoadingLocation = true, IsDefaultBillingLocation = true },
            CancellationToken.None, canViewSensitive: true)).Location!;

        var copy = (await h.Sut.DuplicateAsync(created.Id, CancellationToken.None, canViewSensitive: true)).Location!;

        Assert.NotEqual(created.Id, copy.Id);
        Assert.NotEqual(created.Code, copy.Code);
        Assert.StartsWith("LOC-", copy.Code);
        Assert.Equal("Site Antwerpen (kopie)", copy.Name);
        Assert.True(copy.IsActive);
        Assert.False(copy.IsDefaultLoadingLocation);
        Assert.False(copy.IsDefaultUnloadingLocation);
        Assert.False(copy.IsDefaultBillingLocation);

        // Master data and operational fields ride along, including the sensitive code and hours.
        Assert.Equal(created.AccessCode, copy.AccessCode);
        Assert.Equal(created.Gate, copy.Gate);
        Assert.Equal(created.RouteDescription, copy.RouteDescription);
        Assert.Equal(created.DefaultLoadingMinutes, copy.DefaultLoadingMinutes);
        Assert.Equal(created.CustomerId, copy.CustomerId);
        Assert.Equal(created.OpeningIntervals, copy.OpeningIntervals);

        // The original keeps its defaults.
        var original = await h.Sut.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.True(original!.IsDefaultLoadingLocation);
    }

    [Fact]
    public async Task CustomerContact_OfAnotherCustomer_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var otherCustomerId = await SeedCustomerAsync(h, "KL-0002", "Andere Klant BV");
        var foreignContactId = await SeedContactAsync(h, otherCustomerId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(FullRequest(customerId: customerId, contactId: foreignContactId), CancellationToken.None));

        Assert.Contains("customerContactId", ex.FieldErrors!.Keys);
        Assert.Equal("De gekozen contactpersoon hoort niet bij de gekoppelde klant.", ex.Message);
    }

    [Fact]
    public async Task CustomerContact_OfAnotherTenant_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        var foreignTenantContactId = await SeedContactAsync(h, customerId, tenantId: otherTenantId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(FullRequest(customerId: customerId, contactId: foreignTenantContactId), CancellationToken.None));

        Assert.Contains("customerContactId", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task CustomerContact_WithoutCustomerLink_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var contactId = await SeedContactAsync(h, customerId);

        // The location itself is not linked to any customer → the contact link is meaningless.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(FullRequest(customerId: null, contactId: contactId), CancellationToken.None));

        Assert.Contains("customerContactId", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Search_MatchesPostalCode_AndFiltersByCountryAndType()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(FullRequest("OPS-001"), CancellationToken.None);
        await h.Sut.CreateAsync(FullRequest("OPS-002") with
        {
            Name = "Bouwwerf Gent", Type = LocationType.ConstructionSite, PostalCode = "9000", CountryCode = "nl",
        }, CancellationToken.None);

        var byPostal = await h.Sut.SearchAsync("2030", null, null, null, null, null, null, null,
            TransportationService.Api.Common.Models.PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(1, byPostal.TotalCount);
        Assert.Equal("Site Antwerpen", byPostal.Items[0].Name);

        var byCountry = await h.Sut.SearchAsync(null, null, null, null, "be", null, null, null,
            TransportationService.Api.Common.Models.PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(1, byCountry.TotalCount);

        var byType = await h.Sut.SearchAsync(null, LocationType.ConstructionSite, null, null, null, null, null, null,
            TransportationService.Api.Common.Models.PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(1, byType.TotalCount);
        Assert.Equal("Bouwwerf Gent", byType.Items[0].Name);

        var byPostalFilter = await h.Sut.SearchAsync(null, null, null, null, null, "90", null, null,
            TransportationService.Api.Common.Models.PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(1, byPostalFilter.TotalCount);
        Assert.Equal("Bouwwerf Gent", byPostalFilter.Items[0].Name);
    }
}
