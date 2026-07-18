using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Partners;

public class CustomerServiceTests
{
    private static CustomerService CreateSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenantContext = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(null));
        return new CustomerService(db.Context, tenantContext, audit);
    }

    private static async Task<Guid> SeedTenantAsync(SqliteTestDbContext db, string slug, string prefix)
    {
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = slug, Slug = slug, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumberPrefix = prefix, CustomerNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        return tenantId;
    }

    private static CreateCustomerRequest NewCustomer(string name) =>
        new(name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null);

    [Fact]
    public async Task CreateAsync_GeneratesSequentialCustomerNumbers()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);

        var first = await sut.CreateAsync(NewCustomer("Acme"), CancellationToken.None);
        var second = await sut.CreateAsync(NewCustomer("Globex"), CancellationToken.None);

        Assert.Equal("KL-0001", first.CustomerNumber);
        Assert.Equal("KL-0002", second.CustomerNumber);
    }

    [Fact]
    public async Task AddContact_MarkedPrimary_DemotesExistingPrimary()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("Acme"), CancellationToken.None);

        // Each service call models a separate HTTP request (fresh scoped DbContext); clearing
        // the change tracker between calls keeps the shared in-memory context faithful to that.
        await sut.AddContactAsync(customer.Id, new CreateCustomerContactRequest("Ann", "One", null, null, null, true, null), CancellationToken.None);
        db.Context.ChangeTracker.Clear();
        await sut.AddContactAsync(customer.Id, new CreateCustomerContactRequest("Bob", "Two", null, null, null, true, null), CancellationToken.None);
        db.Context.ChangeTracker.Clear();

        var reloaded = await sut.GetByIdAsync(customer.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Contacts, contact => contact.IsPrimary);
        Assert.Equal("Bob", reloaded.Contacts.First(c => c.IsPrimary).FirstName);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_AndReleasesCustomerNumberFilter()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("Acme"), CancellationToken.None);

        var deleted = await sut.DeleteAsync(customer.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await sut.GetByIdAsync(customer.Id, CancellationToken.None));
        var raw = await db.Context.Customers.IgnoreQueryFilters().SingleAsync();
        Assert.True(raw.IsDeleted);
    }

    [Fact]
    public async Task SearchAsync_IsTenantScoped()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = await SeedTenantAsync(db, "a", "A-");
        var tenantB = await SeedTenantAsync(db, "b", "B-");
        await CreateSut(db, tenantA).CreateAsync(NewCustomer("Acme A"), CancellationToken.None);

        var resultForB = await CreateSut(db, tenantB).SearchAsync(null, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Empty(resultForB.Items);
    }
}
