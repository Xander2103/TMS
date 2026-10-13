using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Controllers;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// Phase 7 (corrections wave 4, admin UI): the rules list must join the static catalog with
/// per-tenant overrides (defaults for rows with none), and the per-customer override endpoint
/// must reject events the tenant hasn't opened up for override — the gap this phase closes on
/// top of the Phase 6 controller.
/// </summary>
public class NotificationRulesControllerTests
{
    private static readonly DateTime Now = new(2026, 07, 30, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Harness(SqliteTestDbContext Db, NotificationRulesController Sut, Guid TenantId, Guid CustomerId);

    private static T UnwrapValue<T>(ActionResult<T> result) =>
        result.Value ?? (T)((ObjectResult)result.Result!).Value!;

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new NotificationRulesController(db.Context, tenant, audit);
        return new Harness(db, sut, tenantId, customerId);
    }

    [Fact]
    public async Task List_EventWithoutRule_ReturnsCatalogDefaults()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.List(CancellationToken.None);

        var catalogEntry = NotificationEventCatalog.Resolve(MessageKinds.OrderCreated)!;
        var row = Assert.Single(UnwrapValue(result), r => r.EventKey == MessageKinds.OrderCreated);
        Assert.False(row.IsCustomized);
        Assert.True(row.Enabled);
        Assert.Equal(catalogEntry.DefaultInApp, row.InAppEnabled);
        Assert.Equal(catalogEntry.DefaultEmail, row.EmailEnabled);
        Assert.Equal(catalogEntry.DefaultRecipients.Count, row.Recipients.Count);
        Assert.Equal(catalogEntry.Label, row.Label);
        Assert.Equal(catalogEntry.Group, row.Group);
    }

    [Fact]
    public async Task List_ContainsAllThirtyCatalogEntries_GroupedAndOrdered()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = UnwrapValue(await h.Sut.List(CancellationToken.None));

        Assert.Equal(NotificationEventCatalog.All.Count, result.Count);
        var orderedByCaller = result.OrderBy(r => r.Group).ThenBy(r => r.Label).ToList();
        Assert.Equal(orderedByCaller.Select(r => r.EventKey), result.Select(r => r.EventKey));
    }

    [Fact]
    public async Task Upsert_UnknownEventKey_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = new NotificationRulesController.UpsertNotificationRuleRequest(
            true, true, false, false, []);

        var result = await h.Sut.Upsert("does_not_exist", request, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Upsert_CreatesThenUpdates_PersistsAndAuditsBoth()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var first = new NotificationRulesController.UpsertNotificationRuleRequest(
            true, true, false, true, [new RecipientSpec(NotificationRecipientType.ExplicitEmail, "ops@acme.test")]);
        await h.Sut.Upsert(MessageKinds.OrderAccepted, first, CancellationToken.None);

        var second = new NotificationRulesController.UpsertNotificationRuleRequest(
            false, false, true, true, []);
        await h.Sut.Upsert(MessageKinds.OrderAccepted, second, CancellationToken.None);

        var persisted = Assert.Single(h.Db.Context.NotificationRules,
            r => r.TenantId == h.TenantId && r.EventKey == MessageKinds.OrderAccepted);
        Assert.False(persisted.Enabled);
        Assert.False(persisted.InAppEnabled);
        Assert.True(persisted.EmailEnabled);
        Assert.Null(persisted.RecipientsJson);

        var logs = h.Db.Context.AuditLogs.Where(a => a.EntityType == "NotificationRule").ToList();
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.Action == "Created");
        Assert.Contains(logs, l => l.Action == "Updated");
    }

    [Fact]
    public async Task List_TenantIsolation_OnlyOwnTenantRuleOverridesApply()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var otherAudit = new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null));
        var otherSut = new NotificationRulesController(h.Db.Context, otherTenant, otherAudit);

        await otherSut.Upsert(MessageKinds.OrderAccepted,
            new NotificationRulesController.UpsertNotificationRuleRequest(false, false, false, false, []),
            CancellationToken.None);

        var ownRow = Assert.Single(UnwrapValue(await h.Sut.List(CancellationToken.None)),
            r => r.EventKey == MessageKinds.OrderAccepted);
        Assert.True(ownRow.Enabled); // untouched by the other tenant's disabled rule
        Assert.False(ownRow.IsCustomized);
    }

    [Fact]
    public async Task SetForCustomer_UnknownEventKey_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.SetForCustomer(
            h.CustomerId, "does_not_exist", new NotificationRulesController.SetCustomerNotificationOverrideRequest(false),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SetForCustomer_EventNotOverridable_ReturnsBadRequest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // No NotificationRule row at all -> AllowCustomerOverride defaults to false.

        var result = await h.Sut.SetForCustomer(
            h.CustomerId, MessageKinds.OrderAccepted, new NotificationRulesController.SetCustomerNotificationOverrideRequest(false),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(h.Db.Context.CustomerNotificationOverrides);
    }

    [Fact]
    public async Task SetForCustomer_RuleExistsButOverrideNotAllowed_ReturnsBadRequest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.Upsert(MessageKinds.OrderAccepted,
            new NotificationRulesController.UpsertNotificationRuleRequest(true, true, true, false, []),
            CancellationToken.None);

        var result = await h.Sut.SetForCustomer(
            h.CustomerId, MessageKinds.OrderAccepted, new NotificationRulesController.SetCustomerNotificationOverrideRequest(false),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SetForCustomer_Overridable_PersistsAndAudits_AndListForCustomerReflectsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.Upsert(MessageKinds.OrderAccepted,
            new NotificationRulesController.UpsertNotificationRuleRequest(true, true, true, true, []),
            CancellationToken.None);

        var before = Assert.Single(UnwrapValue(await h.Sut.ListForCustomer(h.CustomerId, CancellationToken.None)));
        Assert.Null(before.Enabled); // inherits

        await h.Sut.SetForCustomer(
            h.CustomerId, MessageKinds.OrderAccepted, new NotificationRulesController.SetCustomerNotificationOverrideRequest(false),
            CancellationToken.None);

        var after = Assert.Single(UnwrapValue(await h.Sut.ListForCustomer(h.CustomerId, CancellationToken.None)));
        Assert.Equal(false, after.Enabled);

        var logs = h.Db.Context.AuditLogs.Where(a => a.EntityType == "CustomerNotificationOverride").ToList();
        Assert.Single(logs);
    }

    [Fact]
    public async Task ListForCustomer_UnknownCustomer_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.ListForCustomer(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
