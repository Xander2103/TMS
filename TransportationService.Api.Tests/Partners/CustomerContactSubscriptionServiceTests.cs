using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

/// <summary>
/// Sprint 3 — "who receives what?" configured on the contact, stored as the communication
/// rules the engine already understands.
/// </summary>
public class CustomerContactSubscriptionServiceTests
{
    private static readonly DateTime Now = new(2026, 08, 28, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(
        SqliteTestDbContext Db,
        CustomerContactSubscriptionService Sut,
        CustomerCommunicationService Communication,
        Guid TenantId,
        Guid CustomerId);

    private static async Task<Harness> SeedAsync(string? customerLanguage = null, string tenantLanguage = "nl")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DefaultLanguage = tenantLanguage,
            DateFormat = "dd/MM/yyyy", DecimalSeparator = ",", Timezone = "Europe/Brussels",
        });

        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Klant A", CustomerNumber = "KL-1",
            DefaultLanguageCode = customerLanguage,
        };
        db.Context.Customers.Add(customer);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        return new Harness(
            db,
            new CustomerContactSubscriptionService(db.Context, tenant, audit),
            new CustomerCommunicationService(db.Context, tenant, audit),
            tenantId,
            customer.Id);
    }

    private static async Task<Guid> AddContactAsync(
        Harness h, string first, string last, string? email = "x@example.com",
        string? language = null, bool isActive = true,
        CustomerContactType type = CustomerContactType.Algemeen)
    {
        var contact = new CustomerContact
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            FirstName = first, LastName = last, Email = email,
            PreferredLanguageCode = language, IsActive = isActive, ContactType = type,
        };
        h.Db.Context.CustomerContacts.Add(contact);
        await h.Db.Context.SaveChangesAsync();
        return contact.Id;
    }

    // ---------------------------------------------------------- scenario A

    [Fact]
    public async Task Contact_ReceivesPlanningEtaAndPod_WhenTheBoxesAreTicked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters");

        var saved = await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning", "eta", "delivery-pod"], CancellationToken.None);

        Assert.Equal(["delivery-pod", "eta", "planning"], saved!.OptionKeys.Order().ToArray());
        // Stored as the engine's own rules, so delivery resolution works unchanged.
        foreach (var type in new[]
        {
            CustomerCommunicationType.PlanningAlert, CustomerCommunicationType.EtaUpdate,
            CustomerCommunicationType.ProofOfDelivery,
        })
        {
            var recipients = await h.Communication.ResolveRecipientsAsync(h.CustomerId, type, CancellationToken.None);
            Assert.Single(recipients, r => r.ContactId == jan);
        }
    }

    // ---------------------------------------------------------- scenario B

    [Fact]
    public async Task AccountingContact_ReceivesOnlyTheInvoiceOptions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var marie = await AddContactAsync(h, "Marie", "Claes", type: CustomerContactType.Facturatie);

        await h.Sut.SetForContactAsync(h.CustomerId, marie, ["invoice", "credit-note", "invoice-reminder"], CancellationToken.None);

        foreach (var type in new[]
        {
            CustomerCommunicationType.Invoice, CustomerCommunicationType.CreditNote,
            CustomerCommunicationType.InvoiceReminder,
        })
        {
            Assert.Single(await h.Communication.ResolveRecipientsAsync(h.CustomerId, type, CancellationToken.None));
        }

        // …and nothing from the transport side.
        Assert.Empty(await h.Communication.ResolveRecipientsAsync(h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None));
        Assert.Empty(await h.Communication.ResolveRecipientsAsync(h.CustomerId, CustomerCommunicationType.ProofOfDelivery, CancellationToken.None));
    }

    // ---------------------------------------------------------- scenario C

    [Fact]
    public async Task TwoContacts_BothReceiveTheSameNotification()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var sofie = await AddContactAsync(h, "Sofie", "Janssens", "sofie@example.com");

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);
        await h.Sut.SetForContactAsync(h.CustomerId, sofie, ["planning"], CancellationToken.None);

        var recipients = await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None);

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, r => r.ContactId == jan);
        Assert.Contains(recipients, r => r.ContactId == sofie);
    }

    // ---------------------------------------------------------- scenario D

    [Fact]
    public async Task FrenchContact_ResolvesToFrench_ThroughTheLanguageChain()
    {
        var h = await SeedAsync(customerLanguage: "nl");
        using var _ = h.Db;
        var pierre = await AddContactAsync(h, "Pierre", "Dupont", language: "fr");
        await h.Sut.SetForContactAsync(h.CustomerId, pierre, ["planning"], CancellationToken.None);

        var recipient = Assert.Single(await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None));

        Assert.Equal("fr", recipient.LanguageCode);
    }

    [Fact]
    public async Task ContactWithoutALanguage_FallsBackToTheCustomerThenTheTenant()
    {
        var h = await SeedAsync(customerLanguage: "fr", tenantLanguage: "nl");
        using var _ = h.Db;
        var contact = await AddContactAsync(h, "Sans", "Langue", language: null);
        await h.Sut.SetForContactAsync(h.CustomerId, contact, ["planning"], CancellationToken.None);

        var recipient = Assert.Single(await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None));
        Assert.Equal("fr", recipient.LanguageCode);

        // Without a customer language the tenant default is the last resort.
        var h2 = await SeedAsync(customerLanguage: null, tenantLanguage: "de");
        using var __ = h2.Db;
        var contact2 = await AddContactAsync(h2, "Ohne", "Sprache", language: null);
        await h2.Sut.SetForContactAsync(h2.CustomerId, contact2, ["planning"], CancellationToken.None);

        var recipient2 = Assert.Single(await h2.Communication.ResolveRecipientsAsync(
            h2.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None));
        Assert.Equal("de", recipient2.LanguageCode);
    }

    // ---------------------------------------------------------- scenario E

    [Fact]
    public async Task InactiveContact_ReceivesNothing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var contact = await AddContactAsync(h, "Oud", "Contact", isActive: false);
        await h.Sut.SetForContactAsync(h.CustomerId, contact, ["planning"], CancellationToken.None);

        Assert.Empty(await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None));
    }

    // ---------------------------------------------------------- scenario F

    [Fact]
    public async Task ExistingFallbackKeepsWorking_AndSurvivesUnsubscribingTheLastContact()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var backup = await AddContactAsync(h, "Backup", "Balie", "balie@example.com");

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);

        // An administrator configures a fallback on the advanced screen.
        var rule = await h.Db.Context.CustomerCommunicationRules
            .FirstAsync(r => r.Type == CustomerCommunicationType.PlanningAlert);
        rule.FallbackContactId = backup;
        await h.Db.Context.SaveChangesAsync();

        // Jan is unticked on his contact card; the advanced rule must NOT be thrown away.
        await h.Sut.SetForContactAsync(h.CustomerId, jan, [], CancellationToken.None);

        var recipients = await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None);
        var fallback = Assert.Single(recipients);
        Assert.Equal(backup, fallback.ContactId);
        Assert.True(fallback.IsFallback);
    }

    [Fact]
    public async Task UnsubscribingTheLastContact_RemovesARuleThatHasNothingLeft()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters");

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);
        await h.Sut.SetForContactAsync(h.CustomerId, jan, [], CancellationToken.None);

        Assert.False(await h.Db.Context.CustomerCommunicationRules
            .AnyAsync(r => r.Type == CustomerCommunicationType.PlanningAlert));
    }

    // ----------------------------------------------- advanced rules preserved

    [Fact]
    public async Task AdvancedRuleSettings_AreNeverRewrittenByTheSimpleLayer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters");
        var sofie = await AddContactAsync(h, "Sofie", "Janssens", "sofie@example.com");
        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["invoice"], CancellationToken.None);

        var rule = await h.Db.Context.CustomerCommunicationRules.FirstAsync(r => r.Type == CustomerCommunicationType.Invoice);
        rule.CcEmail = "boekhouding@klant.be";
        rule.LanguageCode = "fr";
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.SetForContactAsync(h.CustomerId, sofie, ["invoice"], CancellationToken.None);

        var reloaded = await h.Db.Context.CustomerCommunicationRules.AsNoTracking()
            .FirstAsync(r => r.Type == CustomerCommunicationType.Invoice);
        Assert.Equal("boekhouding@klant.be", reloaded.CcEmail);
        Assert.Equal("fr", reloaded.LanguageCode);
    }

    [Fact]
    public async Task LegacyTypesOutsideTheSimpleList_StayUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters");

        // A rule of a type the contact form does not offer (advanced/legacy).
        h.Db.Context.CustomerCommunicationRules.Add(new CustomerCommunicationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            Type = CustomerCommunicationType.Other, CustomTypeLabel = "Douane", Channel = "Email", IsActive = true,
            Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = jan }],
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);

        var legacy = await h.Db.Context.CustomerCommunicationRules.Include(r => r.Contacts).AsNoTracking()
            .FirstAsync(r => r.Type == CustomerCommunicationType.Other);
        Assert.Equal("Douane", legacy.CustomTypeLabel);
        Assert.Single(legacy.Contacts);
        Assert.Contains(CustomerCommunicationType.Other, CustomerNotificationCatalog.AdvancedOnlyTypes);
    }

    [Fact]
    public async Task SavingTheContactWithoutTouchingTheBoxes_DoesNotWidenAnAdvancedRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters");

        // An administrator limited Jan to PlanningAlert ONLY on the advanced screen; the simple
        // "planning" option also covers DeliveryChange.
        h.Db.Context.CustomerCommunicationRules.Add(new CustomerCommunicationRule
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = h.CustomerId,
            Type = CustomerCommunicationType.PlanningAlert, Channel = "Email", IsActive = true,
            Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = jan }],
        });
        await h.Db.Context.SaveChangesAsync();

        var shown = await h.Sut.GetForContactAsync(h.CustomerId, jan, CancellationToken.None);
        Assert.Equal(["planning"], shown!.OptionKeys);

        // The contact card is saved with exactly what it showed.
        await h.Sut.SetForContactAsync(h.CustomerId, jan, shown.OptionKeys, CancellationToken.None);

        Assert.False(await h.Db.Context.CustomerCommunicationRules
            .AnyAsync(r => r.Type == CustomerCommunicationType.DeliveryChange));
    }

    // ------------------------------------------------ untick / retick, rule hygiene

    [Fact]
    public async Task RetickingAfterUnticking_ResurrectsTheSoftDeletedLink_WithoutADuplicate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var sofie = await AddContactAsync(h, "Sofie", "Janssens", "sofie@example.com");
        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);
        await h.Sut.SetForContactAsync(h.CustomerId, sofie, ["planning"], CancellationToken.None);

        // Jan is unticked (the rule survives because Sofie is still on it) and ticked again.
        await h.Sut.SetForContactAsync(h.CustomerId, jan, [], CancellationToken.None);
        var saved = await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);

        Assert.Equal(["planning"], saved!.OptionKeys);
        var recipients = await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None);
        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, r => r.ContactId == jan);
        Assert.Contains(recipients, r => r.ContactId == sofie);

        // One physical row per (rule, contact): the soft-deleted link was reused, not duplicated.
        var rule = await h.Db.Context.CustomerCommunicationRules.AsNoTracking()
            .FirstAsync(r => r.Type == CustomerCommunicationType.PlanningAlert);
        var janLinks = await h.Db.Context.CustomerCommunicationRuleContacts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.RuleId == rule.Id && c.ContactId == jan)
            .ToListAsync();
        var link = Assert.Single(janLinks);
        Assert.False(link.IsDeleted);
        Assert.Null(link.DeletedAt);
    }

    [Fact]
    public async Task Subscribing_NeverReactivatesARuleAnAdministratorSwitchedOff()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var sofie = await AddContactAsync(h, "Sofie", "Janssens", "sofie@example.com");

        var inactiveId = Guid.NewGuid();
        h.Db.Context.CustomerCommunicationRules.Add(new CustomerCommunicationRule
        {
            Id = inactiveId, TenantId = h.TenantId, CustomerId = h.CustomerId,
            Type = CustomerCommunicationType.ProofOfDelivery, Channel = "Email", IsActive = false, CcEmail = "archief@klant.be",
            Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = sofie }],
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["delivery-pod"], CancellationToken.None);

        var rules = await h.Db.Context.CustomerCommunicationRules.Include(r => r.Contacts).AsNoTracking()
            .Where(r => r.Type == CustomerCommunicationType.ProofOfDelivery)
            .ToListAsync();
        var inactive = Assert.Single(rules, r => r.Id == inactiveId);
        Assert.False(inactive.IsActive);
        Assert.Equal("archief@klant.be", inactive.CcEmail);
        Assert.Single(inactive.Contacts, c => c.ContactId == sofie);

        var fresh = Assert.Single(rules, r => r.Id != inactiveId);
        Assert.True(fresh.IsActive);
        Assert.Single(fresh.Contacts, c => c.ContactId == jan);

        // Jan receives; Sofie (only on the switched-off rule) still does not.
        var recipients = await h.Communication.ResolveRecipientsAsync(
            h.CustomerId, CustomerCommunicationType.ProofOfDelivery, CancellationToken.None);
        Assert.Single(recipients, r => r.ContactId == jan);
    }

    [Fact]
    public async Task Unticking_RemovesTheContactFromEveryRuleOfThatType_AndKeepsTheAdvancedRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var backup = await AddContactAsync(h, "Backup", "Balie", "balie@example.com");

        var simpleId = Guid.NewGuid();
        var advancedId = Guid.NewGuid();
        h.Db.Context.CustomerCommunicationRules.AddRange(
            new CustomerCommunicationRule
            {
                Id = simpleId, TenantId = h.TenantId, CustomerId = h.CustomerId,
                Type = CustomerCommunicationType.Invoice, Channel = "Email", IsActive = true,
                Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = jan }],
            },
            new CustomerCommunicationRule
            {
                Id = advancedId, TenantId = h.TenantId, CustomerId = h.CustomerId,
                Type = CustomerCommunicationType.Invoice, Channel = "Email", IsActive = true,
                CcEmail = "boekhouding@klant.be", LanguageCode = "fr", FallbackContactId = backup,
                Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = jan }],
            });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.SetForContactAsync(h.CustomerId, jan, [], CancellationToken.None);

        Assert.Empty((await h.Sut.GetForContactAsync(h.CustomerId, jan, CancellationToken.None))!.OptionKeys);
        Assert.False(await h.Db.Context.CustomerCommunicationRuleContacts.AnyAsync(c => c.ContactId == jan));

        // The empty simple rule is gone; the advanced one is kept with all its routing intact.
        Assert.False(await h.Db.Context.CustomerCommunicationRules.AnyAsync(r => r.Id == simpleId));
        var advanced = await h.Db.Context.CustomerCommunicationRules.Include(r => r.Contacts).AsNoTracking()
            .FirstAsync(r => r.Id == advancedId);
        Assert.Equal("boekhouding@klant.be", advanced.CcEmail);
        Assert.Equal("fr", advanced.LanguageCode);
        Assert.Equal(backup, advanced.FallbackContactId);
        Assert.Empty(advanced.Contacts);
    }

    [Fact]
    public async Task Subscribing_PrefersTheActiveRuleWithoutAdvancedSettings()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var sofie = await AddContactAsync(h, "Sofie", "Janssens", "sofie@example.com");

        var advancedId = Guid.NewGuid();
        var simpleId = Guid.NewGuid();
        h.Db.Context.CustomerCommunicationRules.AddRange(
            new CustomerCommunicationRule
            {
                Id = advancedId, TenantId = h.TenantId, CustomerId = h.CustomerId,
                Type = CustomerCommunicationType.Invoice, Channel = "Email", IsActive = true, CcEmail = "boekhouding@klant.be",
                Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = sofie }],
            },
            new CustomerCommunicationRule
            {
                Id = simpleId, TenantId = h.TenantId, CustomerId = h.CustomerId,
                Type = CustomerCommunicationType.Invoice, Channel = "Email", IsActive = true,
                Contacts = [new CustomerCommunicationRuleContact { Id = Guid.NewGuid(), TenantId = h.TenantId, ContactId = sofie }],
            });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["invoice"], CancellationToken.None);

        var simple = await h.Db.Context.CustomerCommunicationRules.Include(r => r.Contacts).AsNoTracking().FirstAsync(r => r.Id == simpleId);
        var advanced = await h.Db.Context.CustomerCommunicationRules.Include(r => r.Contacts).AsNoTracking().FirstAsync(r => r.Id == advancedId);
        Assert.Contains(simple.Contacts, c => c.ContactId == jan);
        Assert.DoesNotContain(advanced.Contacts, c => c.ContactId == jan);
    }

    [Fact]
    public async Task SavingUnchangedOptions_WritesNoAuditRecord()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters");

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);
        Assert.Equal(1, await h.Db.Context.AuditLogs.CountAsync(a => a.Action == "ContactNotificationsChanged"));

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);
        Assert.Equal(1, await h.Db.Context.AuditLogs.CountAsync(a => a.Action == "ContactNotificationsChanged"));

        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning", "eta"], CancellationToken.None);
        Assert.Equal(2, await h.Db.Context.AuditLogs.CountAsync(a => a.Action == "ContactNotificationsChanged"));
    }

    // ------------------------------------------------------------- overview

    [Fact]
    public async Task Overview_ListsRecipientsPerOption_AndMarksAdvancedRouting()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var jan = await AddContactAsync(h, "Jan", "Peeters", "jan@example.com");
        var sofie = await AddContactAsync(h, "Sofie", "Janssens", "sofie@example.com");
        await h.Sut.SetForContactAsync(h.CustomerId, jan, ["planning"], CancellationToken.None);
        await h.Sut.SetForContactAsync(h.CustomerId, sofie, ["planning"], CancellationToken.None);

        var rule = await h.Db.Context.CustomerCommunicationRules.FirstAsync(r => r.Type == CustomerCommunicationType.PlanningAlert);
        rule.CcEmail = "cc@klant.be";
        await h.Db.Context.SaveChangesAsync();

        var overview = await h.Sut.GetOverviewAsync(h.CustomerId, CancellationToken.None);

        var planning = Assert.Single(overview!, l => l.OptionKey == "planning");
        Assert.Equal(["Jan Peeters", "Sofie Janssens"], planning.Recipients.Where(r => !r.IsAdvanced).Select(r => r.Name).Order().ToArray());
        // The CC mailbox is routing detail, not a person: flagged so the UI can hide it.
        Assert.Single(planning.Recipients, r => r.IsAdvanced && r.Email == "cc@klant.be");
    }

    [Fact]
    public async Task Subscriptions_AreScopedToTheCustomersOwnContacts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var other = new Customer { Id = Guid.NewGuid(), TenantId = h.TenantId, Name = "Andere", CustomerNumber = "KL-2" };
        h.Db.Context.Customers.Add(other);
        var foreignContact = new CustomerContact
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerId = other.Id,
            FirstName = "Vreemde", LastName = "Persoon", Email = "v@example.com", IsActive = true,
        };
        h.Db.Context.CustomerContacts.Add(foreignContact);
        await h.Db.Context.SaveChangesAsync();

        Assert.Null(await h.Sut.SetForContactAsync(h.CustomerId, foreignContact.Id, ["planning"], CancellationToken.None));
    }
}
