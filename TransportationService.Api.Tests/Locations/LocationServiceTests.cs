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
        Street: "Havenlaan", HouseNumber: "1", PostalCode: "2000", City: "Antwerpen", CountryCode: "be",
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
    public async Task Options_CustomerScope_IncludesOwnAndCompanyLocations_NeverOtherCustomers()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customerId = await SeedCustomerAsync(h);
        var otherCustomerId = await SeedCustomerAsync(h, "KL-0002", "Andere Klant BV");
        await h.Sut.CreateAsync(CreateRequest("DEP-001", "Eigen depot"), CancellationToken.None);
        var site = await h.Sut.CreateAsync(
            CreateRequest("LOC-A", "Klantsite") with
            {
                CustomerId = customerId,
                Type = LocationType.CustomerLocation,
                IsDefaultLoadingLocation = true,
            }, CancellationToken.None);
        await h.Sut.CreateAsync(
            CreateRequest("LOC-B", "Site van andere klant") with { CustomerId = otherCustomerId, Type = LocationType.CustomerLocation },
            CancellationToken.None);

        var options = await h.Sut.GetOptionsAsync(null, customerId, CancellationToken.None);

        Assert.Equal(2, options.Count);
        Assert.DoesNotContain(options, o => o.Name == "Site van andere klant");
        // Defaults sort first and carry their flags + city for display.
        Assert.Equal(site.Location!.Id, options[0].Id);
        Assert.True(options[0].IsDefaultLoadingLocation);
        Assert.False(options[0].IsDefaultUnloadingLocation);
        Assert.Equal("Antwerpen", options[0].City);
    }
}
