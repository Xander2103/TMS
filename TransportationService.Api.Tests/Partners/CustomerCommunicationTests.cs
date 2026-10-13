using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Partners;

public class CustomerCommunicationTests
{
    private sealed record Harness(SqliteTestDbContext Db, CustomerCommunicationService Sut, Guid TenantId, Guid CustomerId,
        Guid ContactWithEmail, Guid ContactWithoutEmail, Guid InactiveContact);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });

        var withEmail = new CustomerContact { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, FirstName = "An", LastName = "Peeters", Email = "an@haven.be", IsActive = true };
        var withoutEmail = new CustomerContact { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, FirstName = "Bert", LastName = "Claes", IsActive = true };
        var inactive = new CustomerContact { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, FirstName = "Cor", LastName = "Weg", Email = "cor@haven.be", IsActive = false };
        db.Context.CustomerContacts.AddRange(withEmail, withoutEmail, inactive);
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new CustomerCommunicationService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, sut, tenantId, customerId, withEmail.Id, withoutEmail.Id, inactive.Id);
    }

    private static SaveCustomerCommunicationRuleRequest Rule(
        CustomerCommunicationType type, IReadOnlyList<Guid> contactIds, Guid? fallback = null, bool isActive = true) =>
        new(type, null, null, null, fallback, isActive, contactIds);

    [Fact]
    public async Task Create_RequiresRealContacts_OfTheSameCustomer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(h.CustomerId, Rule(CustomerCommunicationType.Invoice, []), CancellationToken.None));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(h.CustomerId, Rule(CustomerCommunicationType.Invoice, [Guid.NewGuid()]), CancellationToken.None));
    }

    [Fact]
    public async Task Create_OtherType_RequiresLabel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Sut.CreateAsync(h.CustomerId, Rule(CustomerCommunicationType.Other, [h.ContactWithEmail]), CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_ReturnsActiveLinkedContactsWithEmail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(h.CustomerId,
            Rule(CustomerCommunicationType.PlanningAlert, [h.ContactWithEmail, h.ContactWithoutEmail, h.InactiveContact]),
            CancellationToken.None);

        var recipients = await h.Sut.ResolveRecipientsAsync(h.CustomerId, CustomerCommunicationType.PlanningAlert, CancellationToken.None);

        var recipient = Assert.Single(recipients);
        Assert.Equal(h.ContactWithEmail, recipient.ContactId);
        Assert.Equal("an@haven.be", recipient.Email);
        Assert.False(recipient.IsFallback);
    }

    [Fact]
    public async Task Resolve_UsesFallback_WhenNoLinkedContactHasEmail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(h.CustomerId,
            Rule(CustomerCommunicationType.InvoiceReminder, [h.ContactWithoutEmail], fallback: h.ContactWithEmail),
            CancellationToken.None);

        var recipients = await h.Sut.ResolveRecipientsAsync(h.CustomerId, CustomerCommunicationType.InvoiceReminder, CancellationToken.None);

        var recipient = Assert.Single(recipients);
        Assert.Equal(h.ContactWithEmail, recipient.ContactId);
        Assert.True(recipient.IsFallback);
    }

    [Fact]
    public async Task Resolve_IgnoresInactiveRules_AndOtherTypes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(h.CustomerId,
            Rule(CustomerCommunicationType.Invoice, [h.ContactWithEmail], isActive: false), CancellationToken.None);
        await h.Sut.CreateAsync(h.CustomerId,
            Rule(CustomerCommunicationType.Claims, [h.ContactWithEmail]), CancellationToken.None);

        Assert.Empty(await h.Sut.ResolveRecipientsAsync(h.CustomerId, CustomerCommunicationType.Invoice, CancellationToken.None));
    }

    [Fact]
    public async Task Update_ReplacesContactLinks_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Sut.CreateAsync(h.CustomerId,
            Rule(CustomerCommunicationType.DelayNotification, [h.ContactWithEmail]), CancellationToken.None);

        var updated = await h.Sut.UpdateAsync(h.CustomerId, rule!.Id,
            Rule(CustomerCommunicationType.DelayNotification, [h.ContactWithoutEmail]), CancellationToken.None);

        Assert.Equal([h.ContactWithoutEmail], updated!.ContactIds);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.Action == "CommunicationRuleChanged"));
    }

    [Fact]
    public async Task Delete_RemovesRule()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var rule = await h.Sut.CreateAsync(h.CustomerId,
            Rule(CustomerCommunicationType.GeneralReminder, [h.ContactWithEmail]), CancellationToken.None);

        Assert.True(await h.Sut.DeleteAsync(h.CustomerId, rule!.Id, CancellationToken.None));
        Assert.Empty((await h.Sut.ListAsync(h.CustomerId, CancellationToken.None))!);
    }
}
