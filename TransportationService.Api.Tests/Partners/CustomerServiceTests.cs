using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
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
        return new CustomerService(db.Context, tenantContext, audit, new CountryCodeValidator(db.Context));
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
    public async Task CreateAsync_WithUnknownCountryCode_ThrowsDomainValidation()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        await CountrySeeder.SyncAsync(db.Context);
        var sut = CreateSut(db, tenantId);

        var request = NewCustomer("Acme") with { CountryCode = "XX" };

        await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.CreateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_NormalizesCountryCodeToUppercase()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        await CountrySeeder.SyncAsync(db.Context);
        var sut = CreateSut(db, tenantId);

        var created = await sut.CreateAsync(NewCustomer("Acme") with { CountryCode = "be" }, CancellationToken.None);

        Assert.Equal("BE", created.CountryCode);
    }

    [Fact]
    public async Task CreateAsync_WithVatAndPeppolProfile_PersistsAndValidates()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        await CountrySeeder.SyncAsync(db.Context);
        var sut = CreateSut(db, tenantId);

        var created = await sut.CreateAsync(NewCustomer("Acme") with
        {
            VatNumber = "BE 0123.456.749",
            VatTreatment = Modules.Partners.Entities.VatTreatment.IntraCommunitySupply,
            DefaultVatRatePercent = 0m,
            VatCountryCode = "nl",
            PeppolId = "0123456749",
            PeppolScheme = "0208",
            PurchaseOrderRequired = true,
            CustomerReferenceRequired = true,
        }, CancellationToken.None);

        Assert.Equal("BE0123456749", created.VatNumber);
        Assert.Equal(Modules.Partners.Entities.VatTreatment.IntraCommunitySupply, created.VatTreatment);
        Assert.Equal(0m, created.DefaultVatRatePercent);
        Assert.Equal("NL", created.VatCountryCode);
        Assert.Equal("0123456749", created.PeppolId);
        Assert.Equal("0208", created.PeppolScheme);
        Assert.True(created.PurchaseOrderRequired);
        Assert.True(created.CustomerReferenceRequired);
    }

    [Fact]
    public async Task CreateAsync_InvalidBelgianVat_Throws()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(NewCustomer("Acme") with { VatNumber = "BE0123456750" }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_PeppolSchemeWithoutId_Throws()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(NewCustomer("Acme") with { PeppolScheme = "0208" }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_PeppolIdWithEmbeddedScheme_Throws()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateAsync(NewCustomer("Acme") with { PeppolId = "0208:0123456749" }, CancellationToken.None));
    }

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

    [Fact]
    public async Task SetActiveAsync_TogglesLifecycle_AndAudits()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("Acme"), CancellationToken.None);

        var ok = await sut.SetActiveAsync(customer.Id, new SetCustomerActiveRequest(false), CancellationToken.None);

        Assert.True(ok);
        Assert.False((await sut.GetByIdAsync(customer.Id, CancellationToken.None))!.IsActive);
        Assert.Single(db.Context.AuditLogs, l => l.EntityType == "Customer" && l.Action == "Deactivated");

        await sut.SetActiveAsync(customer.Id, new SetCustomerActiveRequest(true), CancellationToken.None);
        Assert.True((await sut.GetByIdAsync(customer.Id, CancellationToken.None))!.IsActive);
        Assert.Single(db.Context.AuditLogs, l => l.EntityType == "Customer" && l.Action == "Activated");
    }

    [Fact]
    public async Task CreateAsync_WithInitialContact_PersistsContactInSameTransaction()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);

        var created = await sut.CreateAsync(NewCustomer("Acme") with
        {
            InitialContact = new CreateCustomerContactRequest("Ann", "Peeters", "Aankoop", "ann@acme.be", null, true, null),
        }, CancellationToken.None);

        var contact = Assert.Single(created.Contacts);
        Assert.Equal("Ann", contact.FirstName);
        Assert.True(contact.IsPrimary);
        Assert.Single(db.Context.AuditLogs, l => l.Action == "ContactAdded");
    }

    [Fact]
    public async Task CreateAsync_WithIncompleteInitialContact_FailsWithFieldError_AndCreatesNothing()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db, "t", "KL-");
        var sut = CreateSut(db, tenantId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(NewCustomer("Acme") with
        {
            InitialContact = new CreateCustomerContactRequest("Ann", " ", null, null, null, false, null),
        }, CancellationToken.None));

        Assert.Contains("initialContact.lastName", ex.FieldErrors!.Keys);
        Assert.Empty(await db.Context.Customers.ToListAsync());
        Assert.Empty(await db.Context.CustomerContacts.ToListAsync());
    }
}
