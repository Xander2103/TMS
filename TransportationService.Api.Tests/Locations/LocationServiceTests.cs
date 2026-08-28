using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
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

public class LocationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

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

    private static CreateLocationRequest CreateRequest(string code = "DEP-001", string name = "Hoofddepot") => new(
        code, name, LocationType.Depot,
        Street: "Havenlaan", HouseNumber: code, PostalCode: "2000", City: "Antwerpen", CountryCode: "be",
        Latitude: 51.22m, Longitude: 4.40m,
        ContactName: "Jan", ContactPhone: null, ContactEmail: null,
        OpeningHours: "08:00-18:00", LoadingInstructions: null, UnloadingInstructions: null, AccessInstructions: null,
        AccessRestrictions: null, VehicleRestrictions: null, TrailerRestrictions: null,
        AlfapassRequired: true, AppointmentRequired: false, CustomerId: null, Notes: null);

    [Fact]
    public async Task Create_PersistsLocation_WithUppercasedCountryCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.Success, result.Outcome);
        Assert.Equal("BE", result.Location!.CountryCode);
        Assert.True(result.Location.AlfapassRequired);
    }

    [Fact]
    public async Task Create_DuplicateCodeInSameTenant_ReturnsConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var second = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.DuplicateCode, second.Outcome);
    }

    [Fact]
    public async Task Create_InvalidLatitude_ReturnsInvalidCoordinates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = CreateRequest() with { Latitude = 200m };
        var result = await h.Sut.CreateAsync(request, CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.InvalidCoordinates, result.Outcome);
    }

    [Fact]
    public async Task Search_FiltersByTypeAndDoesNotLeakOtherTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(CreateRequest("DEP-001", "Depot A"), CancellationToken.None);
        await h.Sut.CreateAsync(CreateRequest("TERM-001", "Terminal A") with { Type = LocationType.Terminal }, CancellationToken.None);

        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.Set<Location>().Add(new Location { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "X", Name = "Other depot", Type = LocationType.Depot, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var depots = await h.Sut.SearchAsync(null, LocationType.Depot, null, null, null, null, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, depots.TotalCount);
        Assert.Equal("Depot A", depots.Items[0].Name);
    }

    [Fact]
    public async Task GetOptions_OnlyReturnsActiveLocations()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);
        await h.Sut.UpdateAsync(created.Location!.Id, new UpdateLocationRequest(
            created.Location.Code, created.Location.Name, created.Location.Type,
            null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, false, false,
            IsActive: false, CustomerId: null, Notes: null), CancellationToken.None);

        var options = await h.Sut.GetOptionsAsync(null, null, CancellationToken.None);

        Assert.Empty(options);
    }

    [Fact]
    public async Task Delete_SoftDeletes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var deleted = await h.Sut.DeleteAsync(created.Location!.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await h.Sut.GetByIdAsync(created.Location.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Create_LinkedToCustomer_ReturnsCustomerName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = customerId, TenantId = h.TenantId, CustomerNumber = "KL-0001", Name = "Klant BV", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CreateAsync(CreateRequest() with { CustomerId = customerId, Type = LocationType.CustomerLocation }, CancellationToken.None);

        Assert.Equal("Klant BV", result.Location!.CustomerName);
    }

    private static async Task<Guid> SeedCustomerAsync(Harness h, string number = "KL-0001", string name = "Klant BV")
    {
        var customerId = Guid.NewGuid();
        h.Db.Context.Customers.Add(new Customer { Id = customerId, TenantId = h.TenantId, CustomerNumber = number, Name = name, IsActive = true });
        await h.Db.Context.SaveChangesAsync();
        return customerId;
    }

    [Fact]
    public async Task SetDefaults_WithoutLinkedCustomer_IsRejectedWithFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(
            () => h.Sut.SetDefaultsAsync(created.Location!.Id, new SetLocationDefaultsRequest(true, false), CancellationToken.None));

        Assert.NotNull(ex.FieldErrors);
        Assert.Contains("isDefaultLoadingLocation", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task SetDefaults_DemotesThePreviousDefaultOfTheSameCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var first = await h.Sut.CreateAsync(
            CreateRequest("LOC-A", "Site A") with { CustomerId = customerId, Type = LocationType.CustomerLocation }, CancellationToken.None);
        var second = await h.Sut.CreateAsync(
            CreateRequest("LOC-B", "Site B") with { CustomerId = customerId, Type = LocationType.CustomerLocation }, CancellationToken.None);

        await h.Sut.SetDefaultsAsync(first.Location!.Id, new SetLocationDefaultsRequest(true, true), CancellationToken.None);
        var promoted = await h.Sut.SetDefaultsAsync(second.Location!.Id, new SetLocationDefaultsRequest(true, false), CancellationToken.None);

        Assert.True(promoted.Location!.IsDefaultLoadingLocation);
        var demoted = await h.Sut.GetByIdAsync(first.Location.Id, CancellationToken.None);
        Assert.False(demoted!.IsDefaultLoadingLocation);
        // The unloading default was not contested and stays with the first location.
        Assert.True(demoted.IsDefaultUnloadingLocation);
    }

    [Fact]
    public async Task SetActive_Deactivates_AndOptionsExcludeInactive()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(CreateRequest(), CancellationToken.None);

        var ok = await h.Sut.SetActiveAsync(created.Location!.Id, new SetLocationActiveRequest(false), CancellationToken.None);

        Assert.True(ok);
        Assert.False((await h.Sut.GetByIdAsync(created.Location.Id, CancellationToken.None))!.IsActive);
        Assert.Empty(await h.Sut.GetOptionsAsync(null, null, CancellationToken.None));

        await h.Sut.SetActiveAsync(created.Location.Id, new SetLocationActiveRequest(true), CancellationToken.None);
        Assert.Single(await h.Sut.GetOptionsAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Options_CustomerScope_CustomerAddressesFirst_ThenCompany_ThenOtherCustomers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var otherCustomerId = await SeedCustomerAsync(h, "KL-0002", "Andere Klant BV");
        await h.Sut.CreateAsync(CreateRequest("DEP-001", "Eigen depot"), CancellationToken.None);
        // Name sorts BEFORE the default site, so the default-first rule within the group is proven too.
        var plain = await h.Sut.CreateAsync(
            CreateRequest("LOC-C", "Aankomsthal") with { CustomerId = customerId, Type = LocationType.CustomerLocation },
            CancellationToken.None);
        var site = await h.Sut.CreateAsync(
            CreateRequest("LOC-A", "Klantsite") with
            {
                CustomerId = customerId,
                Type = LocationType.CustomerLocation,
                IsDefaultLoadingLocation = true,
            }, CancellationToken.None);
        // Name sorts first alphabetically — must still land LAST because it belongs to another customer.
        var foreign = await h.Sut.CreateAsync(
            CreateRequest("LOC-B", "AAA Site van andere klant") with { CustomerId = otherCustomerId, Type = LocationType.CustomerLocation },
            CancellationToken.None);

        var options = await h.Sut.GetOptionsAsync(null, customerId, CancellationToken.None);

        Assert.Equal(
            new[] { site.Location!.Id, plain.Location!.Id },
            options.Take(2).Select(o => o.Id));
        Assert.Equal("Eigen depot", options[2].Name);
        Assert.Equal(foreign.Location!.Id, options[3].Id);
        // Defaults sort first and carry their flags + city for display.
        Assert.True(options[0].IsDefaultLoadingLocation);
        Assert.False(options[0].IsDefaultUnloadingLocation);
        Assert.Equal("Antwerpen", options[0].City);
        // Provenance flags per group.
        Assert.True(options[0].IsLinkedToCustomer);
        Assert.Null(options[0].LinkedCustomerNames);
        Assert.False(options[2].IsLinkedToCustomer);
        Assert.Equal(0, options[2].LinkedCustomerCount);
        Assert.False(options[3].IsLinkedToCustomer);
    }

    [Fact]
    public async Task Options_AddressOfAnotherCustomer_IsOffered_WithProvenance()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var otherA = await SeedCustomerAsync(h, "KL-0002", "Euro Retail Group");
        var otherB = await SeedCustomerAsync(h, "KL-0003", "Distri-Frais SPRL");
        var shared = await h.Sut.CreateAsync(
            CreateRequest("LOC-S", "Gedeeld magazijn") with { CustomerId = otherA, Type = LocationType.CustomerLocation },
            CancellationToken.None);
        await LinkAsync(h, otherB, shared.Location!.Id);

        var options = await h.Sut.GetOptionsAsync(null, customerId, CancellationToken.None);

        var option = Assert.Single(options, o => o.Id == shared.Location.Id);
        Assert.False(option.IsLinkedToCustomer);
        Assert.Equal(2, option.LinkedCustomerCount);
        Assert.Equal("Distri-Frais SPRL, Euro Retail Group", option.LinkedCustomerNames);
        // No relationship is created merely by offering the address.
        Assert.False(await h.Db.Context.CustomerLocationLinks.AnyAsync(l => l.LocationId == shared.Location.Id && l.CustomerId == customerId));
    }

    [Fact]
    public async Task Options_SharedAddress_ListsOnlyTheOtherCustomers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var other = await SeedCustomerAsync(h, "KL-0002", "Euro Retail Group");
        var shared = await h.Sut.CreateAsync(
            CreateRequest("LOC-S", "Gedeeld magazijn") with { CustomerId = customerId, Type = LocationType.CustomerLocation },
            CancellationToken.None);
        await LinkAsync(h, other, shared.Location!.Id);

        var option = Assert.Single(await h.Sut.GetOptionsAsync(null, customerId, CancellationToken.None));

        Assert.True(option.IsLinkedToCustomer);
        Assert.Equal(2, option.LinkedCustomerCount);
        Assert.Equal("Euro Retail Group", option.LinkedCustomerNames);
    }

    [Fact]
    public async Task Options_NeverOffersAddressesOfAnotherTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        await h.Sut.CreateAsync(CreateRequest("DEP-001", "Eigen depot"), CancellationToken.None);

        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Set<Location>().Add(new Location { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "X", Name = "Depot elders", Type = LocationType.Depot, IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var withCustomer = await h.Sut.GetOptionsAsync(null, customerId, CancellationToken.None);
        var withoutCustomer = await h.Sut.GetOptionsAsync(null, null, CancellationToken.None);

        Assert.Equal("Eigen depot", Assert.Single(withCustomer).Name);
        Assert.Equal("Eigen depot", Assert.Single(withoutCustomer).Name);
    }

    // --- Locations-UX wave: customer sort + per-customer grouping ---------------------------

    private async Task<(Harness H, Guid CustomerAId, Guid CustomerBId)> SeedGroupedAsync()
    {
        var h = await SeedAsync();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        h.Db.Context.Customers.AddRange(
            new TransportationService.Api.Modules.Partners.Entities.Customer
            {
                Id = customerA, TenantId = h.TenantId, CustomerNumber = "KL-1", Name = "Alfa BV", IsActive = true,
            },
            new TransportationService.Api.Modules.Partners.Entities.Customer
            {
                Id = customerB, TenantId = h.TenantId, CustomerNumber = "KL-2", Name = "Beta BV", IsActive = true,
            });
        await h.Db.Context.SaveChangesAsync();
        await h.Sut.CreateAsync(CreateRequest("A-2", "Magazijn Leuven") with { CustomerId = customerA }, CancellationToken.None);
        await h.Sut.CreateAsync(CreateRequest("A-1", "Depot Brussel") with { CustomerId = customerA }, CancellationToken.None);
        await h.Sut.CreateAsync(CreateRequest("B-1", "Leverpunt Gent") with { CustomerId = customerB }, CancellationToken.None);
        await h.Sut.CreateAsync(CreateRequest("X-1", "Vrij terrein"), CancellationToken.None);
        return (h, customerA, customerB);
    }

    [Fact]
    public async Task Search_SortsByCustomerAndStatus_ServerSide()
    {
        var (h, _, _) = await SeedGroupedAsync();
        using var _d = h.Db;

        var byCustomer = await h.Sut.SearchAsync(null, null, null, null, null, null,
            "customer", "asc", PageRequest.Of(1, 20), CancellationToken.None);
        // Null customer sorts first ascending; then Alfa, then Beta.
        Assert.Equal(["Vrij terrein", "Depot Brussel", "Magazijn Leuven", "Leverpunt Gent"],
            byCustomer.Items.Select(i => i.Name).ToArray());

        await h.Sut.SetActiveAsync(byCustomer.Items[1].Id,
            new SetLocationActiveRequest(false), CancellationToken.None);
        var byStatus = await h.Sut.SearchAsync(null, null, null, null, null, null,
            "status", "asc", PageRequest.Of(1, 20), CancellationToken.None);
        Assert.False(byStatus.Items[0].IsActive); // inactive first ascending
    }

    [Fact]
    public async Task Grouped_PagesOverGroups_UnlinkedBucketLast_InnerSortApplied()
    {
        var (h, customerA, _) = await SeedGroupedAsync();
        using var _d = h.Db;

        var all = await h.Sut.SearchGroupedAsync(null, null, null, null, null, null,
            innerSort: "name", PageRequest.Of(1, 10), CancellationToken.None);

        // Three groups: Alfa, Beta, then the unlinked bucket LAST.
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Alfa BV", "Beta BV", null], all.Items.Select(g => g.CustomerName).ToArray());
        Assert.Equal(["Depot Brussel", "Magazijn Leuven"],
            all.Items[0].Locations.Select(l => l.Name).ToArray());

        // Inner sort by code flips Alfa's order (A-1 Depot, A-2 Magazijn stays but proves key).
        var byCode = await h.Sut.SearchGroupedAsync(null, null, null, null, null, null,
            innerSort: "code", PageRequest.Of(1, 10), CancellationToken.None);
        Assert.Equal(["A-1", "A-2"], byCode.Items[0].Locations.Select(l => l.Code).ToArray());

        // Honest paging: page size 1 returns ONE whole group and the true group total.
        var pageOne = await h.Sut.SearchGroupedAsync(null, null, null, null, null, null,
            "name", PageRequest.Of(1, 1), CancellationToken.None);
        Assert.Equal(3, pageOne.TotalCount);
        var alfa = Assert.Single(pageOne.Items);
        Assert.Equal("Alfa BV", alfa.CustomerName);
        Assert.Equal(2, alfa.Locations.Count);

        // Customer filter narrows to that customer's single group.
        var filtered = await h.Sut.SearchGroupedAsync(null, null, null, customerA, null, null,
            "name", PageRequest.Of(1, 10), CancellationToken.None);
        Assert.Single(filtered.Items);
        Assert.Equal("Alfa BV", filtered.Items[0].CustomerName);
    }

    [Fact]
    public async Task Grouped_IsTenantIsolated()
    {
        var (h, _, _) = await SeedGroupedAsync();
        using var _d = h.Db;
        var foreignTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        var foreignSut = new LocationService(h.Db.Context, new DevTenantContext(foreignTenant),
            new AuditService(h.Db.Context, new DevTenantContext(foreignTenant), new DevCurrentUserContext(null)),
            new CountryCodeValidator(h.Db.Context));

        var foreign = await foreignSut.SearchGroupedAsync(null, null, null, null, null, null,
            "name", PageRequest.Of(1, 10), CancellationToken.None);

        Assert.Equal(0, foreign.TotalCount);
        Assert.Empty(foreign.Items);
    }

    // ------------------------------------------------------------ audit fixes

    private static UpdateLocationRequest UpdateFrom(LocationDetailDto d, Guid? customerId) => new(
        d.Code, d.Name, d.Type, d.Street, d.HouseNumber, d.PostalCode, d.City, d.CountryCode,
        d.Latitude, d.Longitude, d.ContactName, d.ContactPhone, d.ContactEmail,
        d.OpeningHours, d.LoadingInstructions, d.UnloadingInstructions, d.AccessInstructions,
        d.AccessRestrictions, d.VehicleRestrictions, d.TrailerRestrictions,
        d.AlfapassRequired, d.AppointmentRequired, IsActive: true, CustomerId: customerId, Notes: d.Notes,
        d.IsDefaultLoadingLocation, d.IsDefaultUnloadingLocation, d.IsDefaultBillingLocation);

    private static async Task<CustomerLocationLink> LinkAsync(Harness h, Guid customerId, Guid locationId)
    {
        var link = new CustomerLocationLink
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = customerId, LocationId = locationId,
            Role = CustomerLocationRole.Both, IsActive = true, Alias = "Eigen naam", IsDefaultLoading = true,
        };
        h.Db.Context.CustomerLocationLinks.Add(link);
        await h.Db.Context.SaveChangesAsync();
        return link;
    }

    [Fact]
    public async Task Update_ChangingTheLegacyOwnerOfASharedAddress_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await SeedCustomerAsync(h, "KL-1", "Alfa BV");
        var customerB = await SeedCustomerAsync(h, "KL-2", "Beta BV");
        var customerC = await SeedCustomerAsync(h, "KL-3", "Gamma BV");
        var created = await h.Sut.CreateAsync(CreateRequest("S-1", "Gedeeld") with { CustomerId = customerA }, CancellationToken.None);
        await LinkAsync(h, customerB, created.Location!.Id);

        var ex = await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(
            () => h.Sut.UpdateAsync(created.Location.Id, UpdateFrom(created.Location, customerC), CancellationToken.None));

        Assert.Contains("Klant › Adressen", ex.Message);
        // Nothing was touched: both relationships survive, no soft delete.
        var links = await h.Db.Context.CustomerLocationLinks.IgnoreQueryFilters()
            .Where(l => l.LocationId == created.Location.Id).ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, l => Assert.False(l.IsDeleted));
    }

    [Fact]
    public async Task Update_ChangingTheLegacyOwnerOfASingleLinkAddress_SoftDeletesTheOldLink_WithAudit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await SeedCustomerAsync(h, "KL-1", "Alfa BV");
        var customerB = await SeedCustomerAsync(h, "KL-2", "Beta BV");
        var created = await h.Sut.CreateAsync(CreateRequest("S-1", "Verhuisd") with { CustomerId = customerA }, CancellationToken.None);
        var oldLinkId = (await h.Db.Context.CustomerLocationLinks.SingleAsync(l => l.LocationId == created.Location!.Id)).Id;

        var updated = await h.Sut.UpdateAsync(created.Location!.Id, UpdateFrom(created.Location, customerB), CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.Success, updated.Outcome);
        var all = await h.Db.Context.CustomerLocationLinks.IgnoreQueryFilters()
            .Where(l => l.LocationId == created.Location.Id).ToListAsync();
        var old = Assert.Single(all, l => l.Id == oldLinkId);
        Assert.True(old.IsDeleted); // soft, never ExecuteDelete
        Assert.Single(all, l => l.CustomerId == customerB && !l.IsDeleted);
        Assert.Contains(await h.Db.Context.AuditLogs.ToListAsync(),
            a => a.EntityType == "CustomerLocationLink" && a.EntityId == oldLinkId.ToString() && a.Action == "Unlinked");
    }

    [Fact]
    public async Task Update_NeverOverwritesAnExistingLinksDefaultsFromTheAddressForm()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await SeedCustomerAsync(h, "KL-1", "Alfa BV");
        var created = await h.Sut.CreateAsync(CreateRequest("S-1", "Site") with { CustomerId = customerA }, CancellationToken.None);
        var link = await h.Db.Context.CustomerLocationLinks.SingleAsync(l => l.LocationId == created.Location!.Id);
        link.IsDefaultUnloading = true;
        link.Alias = "Magazijn Noord";
        await h.Db.Context.SaveChangesAsync();

        // The address form re-saves with its own (false) default flags.
        await h.Sut.UpdateAsync(created.Location!.Id,
            UpdateFrom(created.Location, customerA) with { IsDefaultUnloadingLocation = false }, CancellationToken.None);

        var after = await h.Db.Context.CustomerLocationLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        Assert.True(after.IsDefaultUnloading);
        Assert.Equal("Magazijn Noord", after.Alias);
    }

    [Fact]
    public async Task Duplicate_DerivesAddressKeys_AndCreatesTheCustomerLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await SeedCustomerAsync(h, "KL-1", "Alfa BV");
        var source = await h.Sut.CreateAsync(CreateRequest("S-1", "Origineel") with { CustomerId = customerA }, CancellationToken.None);

        var copy = await h.Sut.DuplicateAsync(source.Location!.Id, CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.Success, copy.Outcome);
        var stored = await h.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == copy.Location!.Id);
        var original = await h.Db.Context.Locations.AsNoTracking().SingleAsync(l => l.Id == source.Location.Id);
        Assert.Equal(original.AddressExactKey, stored.AddressExactKey);
        Assert.Equal(original.AddressStreetKey, stored.AddressStreetKey);
        Assert.False(string.IsNullOrEmpty(stored.AddressExactKey));
        Assert.Single(await h.Db.Context.CustomerLocationLinks
            .Where(l => l.LocationId == stored.Id && l.CustomerId == customerA).ToListAsync());
        Assert.Equal(1, copy.Location!.LinkedCustomerCount);
    }

    [Fact]
    public async Task Create_SameFrontDoorAsAnActiveAddress_ReturnsPossibleDuplicate_UnlessOverridden()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerA = await SeedCustomerAsync(h, "KL-1", "Alfa BV");
        var existing = await h.Sut.CreateAsync(CreateRequest("S-1", "Bestaand") with { CustomerId = customerA }, CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, existing.Outcome);

        // Different casing/spacing, same door: refused with the candidate list.
        var again = CreateRequest("S-2", "Nogmaals") with { Street = " havenlaan ", HouseNumber = "s-1", City = "ANTWERPEN" };
        var refused = await h.Sut.CreateAsync(again, CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.PossibleDuplicate, refused.Outcome);
        Assert.Null(refused.Location);
        Assert.True(refused.Duplicates!.HasExactMatch);
        var candidate = Assert.Single(refused.Duplicates.Candidates);
        Assert.Equal(existing.Location!.Id, candidate.LocationId);
        Assert.Equal(["Alfa BV"], candidate.LinkedCustomers);
        Assert.Equal(1, await h.Db.Context.Locations.CountAsync());

        var overridden = await h.Sut.CreateAsync(again with { OverrideDuplicate = true }, CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, overridden.Outcome);
        Assert.Equal(2, await h.Db.Context.Locations.CountAsync());
    }

    [Fact]
    public async Task Create_SameFrontDoorAsAnInactiveAddress_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var existing = await h.Sut.CreateAsync(CreateRequest("S-1", "Oud"), CancellationToken.None);
        await h.Sut.SetActiveAsync(existing.Location!.Id, new SetLocationActiveRequest(false), CancellationToken.None);

        var result = await h.Sut.CreateAsync(CreateRequest("S-2", "Nieuw") with { HouseNumber = "S-1" }, CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task Grouped_ListsASharedAddressUnderEachOfItsCustomers_ViaTheLinks()
    {
        var (h, customerA, customerB) = await SeedGroupedAsync();
        using var _d = h.Db;
        // "Magazijn Leuven" (legacy owner Alfa) is now also used by Beta — via a link only.
        var shared = await h.Db.Context.Locations.SingleAsync(l => l.Name == "Magazijn Leuven");
        await LinkAsync(h, customerB, shared.Id);

        var all = await h.Sut.SearchGroupedAsync(null, null, null, null, null, null,
            "name", PageRequest.Of(1, 10), CancellationToken.None);

        var alfa = all.Items.Single(g => g.CustomerId == customerA);
        var beta = all.Items.Single(g => g.CustomerId == customerB);
        Assert.Contains(alfa.Locations, l => l.Id == shared.Id);
        var betaRow = Assert.Single(beta.Locations, l => l.Id == shared.Id);
        // Per-customer defaults come from Beta's own link, not from the legacy owner's flags.
        Assert.True(betaRow.IsDefaultLoadingLocation);
        Assert.Equal(["Leverpunt Gent", "Magazijn Leuven"], beta.Locations.Select(l => l.Name).ToArray());

        // Filtering on Beta only shows Beta's relationships (Alfa's group is absent).
        var onlyBeta = await h.Sut.SearchGroupedAsync(null, null, null, customerB, null, null,
            "name", PageRequest.Of(1, 10), CancellationToken.None);
        Assert.Equal(1, onlyBeta.TotalCount);
        Assert.Equal(2, Assert.Single(onlyBeta.Items).Locations.Count);

        // The flat list aggregates every user of the address.
        var flat = await h.Sut.SearchAsync("Leuven", null, null, null, null, null, null, null, PageRequest.Of(1, 10), CancellationToken.None);
        Assert.Equal("Alfa BV, Beta BV", Assert.Single(flat.Items).CustomerName);

        var detail = await h.Sut.GetByIdAsync(shared.Id, CancellationToken.None);
        Assert.Equal(2, detail!.LinkedCustomerCount);
        Assert.Equal(["Alfa BV", "Beta BV"], detail.LinkedCustomerNames);
    }
}
