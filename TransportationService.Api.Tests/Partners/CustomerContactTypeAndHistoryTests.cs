using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Dtos;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Partners;

/// <summary>
/// Master-data wave: multiple contacts on create, contact types with primary-per-type,
/// used-contact delete protection, PO-policy sync, widened audit, and the history projection.
/// </summary>
public class CustomerContactTypeAndHistoryTests
{
    private static CustomerService CreateSut(SqliteTestDbContext db, Guid tenantId)
    {
        var tenantContext = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenantContext, new DevCurrentUserContext(null));
        return new CustomerService(db.Context, tenantContext, audit, new CountryCodeValidator(db.Context));
    }

    private static async Task<Guid> SeedTenantAsync(SqliteTestDbContext db, string slug = "t", string prefix = "KL-")
    {
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = slug, Slug = slug, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumberPrefix = prefix, CustomerNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        return tenantId;
    }

    private static CreateCustomerRequest NewCustomer(string name) =>
        new(name, null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null);

    private static CreateCustomerContactRequest Contact(
        string first, string last, bool isPrimary = false, string? type = null) =>
        new(first, last, null, null, null, isPrimary, null, ContactType: type);

    [Fact]
    public async Task CreateAsync_WithMultipleContacts_PersistsAllWithTypes()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);

        var created = await sut.CreateAsync(NewCustomer("Testklant Transport NV") with
        {
            Contacts =
            [
                Contact("Jan", "Peeters", isPrimary: true, type: "Planning"),
                Contact("Sofie", "Janssens", isPrimary: true, type: "Facturatie"),
                Contact("Marc", "De Smet", type: "Magazijn"),
            ],
        }, CancellationToken.None);

        var reloaded = await sut.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(3, reloaded!.Contacts.Count);
        Assert.Contains(reloaded.Contacts, c => c is { FirstName: "Jan", ContactType: "Planning", IsPrimary: true });
        Assert.Contains(reloaded.Contacts, c => c is { FirstName: "Sofie", ContactType: "Facturatie", IsPrimary: true });
        Assert.Contains(reloaded.Contacts, c => c is { FirstName: "Marc", ContactType: "Magazijn", IsPrimary: false });
    }

    [Fact]
    public async Task CreateAsync_TwoPrimariesOfSameType_ThrowsWithIndexedFieldPath()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(
            NewCustomer("Dubbel") with
            {
                Contacts =
                [
                    Contact("A", "Eén", isPrimary: true, type: "Planning"),
                    Contact("B", "Twee", isPrimary: true, type: "Planning"),
                ],
            }, CancellationToken.None));

        Assert.Contains("contacts[1].isPrimary", ex.FieldErrors!.Keys);
        Assert.Empty(await db.Context.Customers.Where(c => c.TenantId == tenantId).ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_UnknownContactType_Throws()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(
            NewCustomer("X") with { Contacts = [Contact("A", "B", type: "Onzin")] }, CancellationToken.None));

        Assert.Contains("contacts[0].contactType", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task CreateAsync_LegacyInitialContact_DefaultsToAlgemeen()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);

        var created = await sut.CreateAsync(NewCustomer("Legacy") with
        {
            InitialContact = Contact("Els", "Vermeulen", isPrimary: true),
        }, CancellationToken.None);

        var contact = Assert.Single(created.Contacts);
        Assert.Equal("Algemeen", contact.ContactType);
        Assert.True(contact.IsPrimary);
    }

    [Fact]
    public async Task AddContact_PrimaryDemotesOnlySameType()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("Demote") with
        {
            Contacts =
            [
                Contact("Jan", "Planning", isPrimary: true, type: "Planning"),
                Contact("Sofie", "Facturatie", isPrimary: true, type: "Facturatie"),
            ],
        }, CancellationToken.None);

        await sut.AddContactAsync(customer.Id,
            Contact("Piet", "NieuwePlanning", isPrimary: true, type: "Planning"), CancellationToken.None);

        var reloaded = await sut.GetByIdAsync(customer.Id, CancellationToken.None);
        var planningPrimary = Assert.Single(reloaded!.Contacts, c => c.ContactType == "Planning" && c.IsPrimary);
        Assert.Equal("Piet", planningPrimary.FirstName);
        // The invoicing primary is untouched by a planning promotion.
        Assert.Single(reloaded.Contacts, c => c.ContactType == "Facturatie" && c.IsPrimary && c.FirstName == "Sofie");
    }

    [Fact]
    public async Task UpdateContact_RecordsOldAndNewValuesInAudit()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("AuditKlant") with
        {
            Contacts = [Contact("Jan", "Peeters", type: "Planning")],
        }, CancellationToken.None);
        var contact = customer.Contacts.Single();

        await sut.UpdateContactAsync(customer.Id, contact.Id,
            new UpdateCustomerContactRequest("Jan", "Peeters", null, null, "+32 475 11 22 33", false, null,
                ContactType: "Planning"), CancellationToken.None);

        var log = await db.Context.AuditLogs
            .SingleAsync(a => a.TenantId == tenantId && a.Action == "ContactUpdated");
        Assert.NotNull(log.OldValuesJson);
        Assert.Contains("Jan", log.OldValuesJson);
        // '+' is escaped as + by System.Text.Json — assert on the digits.
        Assert.Contains("475 11 22 33", log.NewValuesJson);
    }

    [Fact]
    public async Task RemoveContact_ReferencedByCommunicationRule_IsBlockedWithDutchMessage()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("InGebruik") with
        {
            Contacts = [Contact("Vera", "Vast")],
        }, CancellationToken.None);
        var contact = customer.Contacts.Single();

        var rule = new CustomerCommunicationRule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            Type = CustomerCommunicationType.Invoice,
        };
        db.Context.CustomerCommunicationRules.Add(rule);
        db.Context.CustomerCommunicationRuleContacts.Add(new CustomerCommunicationRuleContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RuleId = rule.Id,
            ContactId = contact.Id,
        });
        await db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => sut.RemoveContactAsync(customer.Id, contact.Id, CancellationToken.None));

        Assert.Contains("deactiveren", ex.Message);
        Assert.NotNull(await db.Context.CustomerContacts
            .FirstOrDefaultAsync(c => c.Id == contact.Id));
    }

    [Fact]
    public async Task Update_PurchaseOrderRequired_SyncsAuthoritativePolicy()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("PoSync"), CancellationToken.None);

        UpdateCustomerRequest Update(bool poRequired) =>
            new("PoSync", null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null,
                IsActive: true, PurchaseOrderRequired: poRequired);

        // bool true → Required.
        await sut.UpdateAsync(customer.Id, Update(true), CancellationToken.None);
        var entity = await db.Context.Customers.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(PurchaseOrderPolicy.Required, entity.PurchaseOrderPolicy);
        Assert.True(entity.PurchaseOrderRequired);

        // bool false clears Required → None.
        await sut.UpdateAsync(customer.Id, Update(false), CancellationToken.None);
        await db.Context.Entry(entity).ReloadAsync();
        Assert.Equal(PurchaseOrderPolicy.None, entity.PurchaseOrderPolicy);

        // "Optional" set by the billing panel survives a form save with bool=false.
        entity.PurchaseOrderPolicy = PurchaseOrderPolicy.Optional;
        await db.Context.SaveChangesAsync();
        await sut.UpdateAsync(customer.Id, Update(false), CancellationToken.None);
        await db.Context.Entry(entity).ReloadAsync();
        Assert.Equal(PurchaseOrderPolicy.Optional, entity.PurchaseOrderPolicy);
    }

    [Fact]
    public async Task Update_AuditCapturesAddressAndContactFieldChanges()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        await CountrySeeder.SyncAsync(db.Context);
        var sut = CreateSut(db, tenantId);
        var customer = await sut.CreateAsync(NewCustomer("Adres BV"), CancellationToken.None);

        await sut.UpdateAsync(customer.Id,
            new UpdateCustomerRequest("Adres BV", null, null, null, "info@adres.be", "+32 3 111 22 33", null,
                "Noorderlaan", "10", "2030", "Antwerpen", "BE", null, 30, null, null, IsActive: true),
            CancellationToken.None);

        var log = await db.Context.AuditLogs
            .SingleAsync(a => a.TenantId == tenantId && a.Action == "Updated");
        Assert.Contains("Noorderlaan", log.NewValuesJson);
        Assert.Contains("info@adres.be", log.NewValuesJson);
        Assert.Contains("Antwerpen", log.NewValuesJson);
    }

    [Fact]
    public async Task History_ReturnsReadableEntriesWithCategoriesAndDiffs()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);
        var history = new CustomerHistoryService(db.Context, new DevTenantContext(tenantId));

        var customer = await sut.CreateAsync(NewCustomer("Historiek NV") with
        {
            Contacts = [Contact("Jan", "Peeters", type: "Planning")],
        }, CancellationToken.None);
        await sut.UpdateAsync(customer.Id,
            new UpdateCustomerRequest("Historiek NV", null, null, null, "nieuw@historiek.be", null, null,
                null, null, null, null, null, null, 30, null, null, IsActive: true),
            CancellationToken.None);

        var page = await history.GetHistoryAsync(customer.Id, 1, 25, null, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Contains(page!.Items, e => e.Category == "Contactpersonen" && e.ActionLabel == "Contactpersoon toegevoegd");
        var update = Assert.Single(page.Items, e => e.Action == "Updated");
        var emailChange = Assert.Single(update.Changes, c => c.Field == "E-mailadres");
        Assert.Null(emailChange.Before);
        Assert.Equal("nieuw@historiek.be", emailChange.After);

        // Category filter narrows the list.
        var contactsOnly = await history.GetHistoryAsync(customer.Id, 1, 25, "Contactpersonen", CancellationToken.None);
        Assert.All(contactsOnly!.Items, e => Assert.Equal("Contactpersonen", e.Category));
    }

    [Fact]
    public async Task History_OtherTenantsCustomer_ReturnsNull()
    {
        using var db = new SqliteTestDbContext();
        var tenantA = await SeedTenantAsync(db, "a", "A-");
        var tenantB = await SeedTenantAsync(db, "b", "B-");
        var sut = CreateSut(db, tenantA);
        var customer = await sut.CreateAsync(NewCustomer("Van A"), CancellationToken.None);

        var historyB = new CustomerHistoryService(db.Context, new DevTenantContext(tenantB));
        Assert.Null(await historyB.GetHistoryAsync(customer.Id, 1, 25, null, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_DuplicateExplicitNumber_LeavesNoPartialContacts()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = await SeedTenantAsync(db);
        var sut = CreateSut(db, tenantId);
        await sut.CreateAsync(NewCustomer("Eerste") with { CustomerNumber = "KL-0099" }, CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => sut.CreateAsync(
            NewCustomer("Tweede") with
            {
                CustomerNumber = "KL-0099",
                Contacts = [Contact("Nooit", "Bewaard")],
            }, CancellationToken.None));

        Assert.Empty(await db.Context.CustomerContacts
            .Where(c => c.TenantId == tenantId && c.FirstName == "Nooit").ToListAsync());
        Assert.Single(await db.Context.Customers.Where(c => c.TenantId == tenantId).ToListAsync());
    }
}
