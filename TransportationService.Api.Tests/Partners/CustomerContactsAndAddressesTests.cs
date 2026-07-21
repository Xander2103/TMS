using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Dtos;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Locations.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

public class CustomerContactsAndAddressesTests
{
    private sealed record Harness(SqliteTestDbContext Db, CustomerService Customers, LocationService Locations, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var validator = new CountryCodeValidator(db.Context);
        return new Harness(db,
            new CustomerService(db.Context, tenant, audit, validator),
            new LocationService(db.Context, tenant, audit, validator),
            tenantId, customerId);
    }

    private static async Task<Guid> AddDepartmentAsync(Harness h, string code = "PLAN", string name = "Planning")
    {
        var department = new ContactDepartment { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = code, Name = name, IsActive = true };
        h.Db.Context.ContactDepartments.Add(department);
        await h.Db.Context.SaveChangesAsync();
        return department.Id;
    }

    [Fact]
    public async Task AddContact_WithExpandedFields_RoundTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var departmentId = await AddDepartmentAsync(h);

        var contact = await h.Customers.AddContactAsync(h.CustomerId, new CreateCustomerContactRequest(
            "An", "Peeters", "Planner", "an@haven.be", "+3231112233", IsPrimary: true, Notes: null,
            DisplayName: "An P.", Nickname: "Anneke", MobilePhone: "+32470112233",
            DepartmentId: departmentId, PreferredLanguageCode: "NL", IsActive: true), CancellationToken.None);

        Assert.NotNull(contact);
        Assert.Equal("An P.", contact!.DisplayName);
        Assert.Equal("Anneke", contact.Nickname);
        Assert.Equal("+32470112233", contact.MobilePhone);
        Assert.Equal(departmentId, contact.DepartmentId);
        Assert.Equal("nl", contact.PreferredLanguageCode);
        Assert.True(contact.IsActive);
    }

    [Fact]
    public async Task AddContact_WithUnknownDepartment_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Customers.AddContactAsync(h.CustomerId, new CreateCustomerContactRequest(
                "An", "Peeters", null, null, null, false, null, DepartmentId: Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateContact_CanDeactivate_AndClearDepartment()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var departmentId = await AddDepartmentAsync(h);
        var contact = await h.Customers.AddContactAsync(h.CustomerId, new CreateCustomerContactRequest(
            "An", "Peeters", null, null, null, false, null, DepartmentId: departmentId), CancellationToken.None);

        var updated = await h.Customers.UpdateContactAsync(h.CustomerId, contact!.Id, new UpdateCustomerContactRequest(
            "An", "Peeters", null, null, null, false, null, DepartmentId: null, IsActive: false), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Null(updated!.DepartmentId);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task DefaultBillingLocation_IsUniquePerCustomer_AndMovesOnPromotion()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var first = await h.Locations.CreateAsync(LocationRequest("FACT-1", "Facturatieadres 1", h.CustomerId, isBilling: true), CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, first.Outcome);
        Assert.True(first.Location!.IsDefaultBillingLocation);

        var second = await h.Locations.CreateAsync(LocationRequest("FACT-2", "Facturatieadres 2", h.CustomerId, isBilling: true), CancellationToken.None);
        Assert.Equal(LocationOperationOutcome.Success, second.Outcome);

        var firstReloaded = await h.Locations.GetByIdAsync(first.Location.Id, CancellationToken.None);
        Assert.False(firstReloaded!.IsDefaultBillingLocation);
        Assert.True(second.Location!.IsDefaultBillingLocation);
    }

    [Fact]
    public async Task DefaultBilling_WithoutCustomer_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Locations.CreateAsync(LocationRequest("FACT-3", "Zonder klant", customerId: null, isBilling: true), CancellationToken.None));
    }

    [Fact]
    public async Task NewAddressRoleTypes_AreAccepted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Locations.CreateAsync(LocationRequest("ZETEL-1", "Maatschappelijke zetel", h.CustomerId) with
        {
            Type = LocationType.RegisteredOffice,
        }, CancellationToken.None);

        Assert.Equal(LocationOperationOutcome.Success, created.Outcome);
        Assert.Equal(LocationType.RegisteredOffice, created.Location!.Type);
    }

    private static CreateLocationRequest LocationRequest(string code, string name, Guid? customerId, bool isBilling = false) => new(
        code, name, LocationType.BillingAddress,
        "Straat", "1", "2000", "Antwerpen", "BE", null, null,
        null, null, null, null, null, null, null, null, null, null,
        AlfapassRequired: false, AppointmentRequired: false, CustomerId: customerId, Notes: null,
        IsDefaultBillingLocation: isBilling);
}
