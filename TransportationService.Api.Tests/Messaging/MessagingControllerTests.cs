using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Controllers;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// Phase 6 (corrections wave 4): MessageTemplate save-time placeholder validation, BodyHtml
/// sanitization, per-customer override precedence and audit trail.
/// </summary>
public class MessagingControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 29, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, MessagingController Sut, Guid TenantId, Guid CustomerId);

    /// <summary>Unwraps an ActionResult&lt;T&gt; whether the action returned T directly (sets
    /// .Value) or via Ok(...)/an ObjectResult (sets .Result instead).</summary>
    private static T UnwrapValue<T>(ActionResult<T> result) =>
        result.Value ?? (T)((ObjectResult)result.Result!).Value!;

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var sut = new MessagingController(db.Context, tenant, audit);
        return new Harness(db, sut, tenantId, customerId);
    }

    [Fact]
    public async Task UpsertTemplate_UnknownPlaceholder_ThrowsDomainValidationException()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = new MessagingController.UpsertTemplateRequest(
            MessageKinds.OrderAccepted, MessageChannel.Email, "nl", "Onderwerp",
            "Beste {{customerName}}, {{onbekendeVariabele}}", null, true);

        var exception = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpsertTemplate(request, CancellationToken.None));
        Assert.Contains("onbekendeVariabele", exception.Message);
    }

    [Fact]
    public async Task UpsertTemplate_KnownPlaceholders_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = new MessagingController.UpsertTemplateRequest(
            MessageKinds.OrderAccepted, MessageChannel.Email, "nl", "Bevestiging {{orderNumber}}",
            "Beste {{customerName}}, uw opdracht {{orderNumber}} ({{goodsDescription}}) is geaccepteerd.", null, true);

        var result = await h.Sut.UpsertTemplate(request, CancellationToken.None);

        Assert.NotNull(UnwrapValue(result));
    }

    [Fact]
    public async Task UpsertTemplate_SanitizesBodyHtml_StripsScript()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = new MessagingController.UpsertTemplateRequest(
            MessageKinds.PodAvailable, MessageChannel.Email, "nl", "Onderwerp", "Body",
            "<p>Hallo</p><script>alert(1)</script>", true);

        var result = await h.Sut.UpsertTemplate(request, CancellationToken.None);

        Assert.Equal("<p>Hallo</p>", UnwrapValue(result).BodyHtml);
    }

    [Fact]
    public async Task UpsertTemplate_RecordsAudit_WithOldAndNew()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var first = new MessagingController.UpsertTemplateRequest(
            MessageKinds.PodAvailable, MessageChannel.Email, "nl", "Eerste onderwerp", "Eerste tekst", null, true);
        await h.Sut.UpsertTemplate(first, CancellationToken.None);

        var second = new MessagingController.UpsertTemplateRequest(
            MessageKinds.PodAvailable, MessageChannel.Email, "nl", "Tweede onderwerp", "Tweede tekst", null, true);
        await h.Sut.UpsertTemplate(second, CancellationToken.None);

        var logs = h.Db.Context.AuditLogs.Where(a => a.EntityType == "MessageTemplate").ToList();
        Assert.Equal(2, logs.Count);
        Assert.Contains(logs, l => l.Action == "Created");
        Assert.Contains(logs, l => l.Action == "Updated");
    }

    [Fact]
    public async Task CustomerTemplates_PrecedenceChain_MarksOverriddenVsInherited()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Tenant default.
        await h.Sut.UpsertTemplate(new MessagingController.UpsertTemplateRequest(
            MessageKinds.PodAvailable, MessageChannel.Email, "nl", "Tenant onderwerp", "Tenant tekst", null, true),
            CancellationToken.None);

        var beforeOverride = await h.Sut.CustomerTemplates(h.CustomerId, CancellationToken.None);
        var inherited = Assert.Single(UnwrapValue(beforeOverride), t => t.Kind == MessageKinds.PodAvailable);
        Assert.False(inherited.IsOverridden);
        Assert.Equal("Tenant onderwerp", inherited.Subject);

        // Customer-specific override.
        await h.Sut.UpsertTemplate(new MessagingController.UpsertTemplateRequest(
            MessageKinds.PodAvailable, MessageChannel.Email, "nl", "Klant onderwerp", "Klant tekst", null, true, h.CustomerId),
            CancellationToken.None);

        var afterOverride = await h.Sut.CustomerTemplates(h.CustomerId, CancellationToken.None);
        var overridden = Assert.Single(UnwrapValue(afterOverride), t => t.Kind == MessageKinds.PodAvailable);
        Assert.True(overridden.IsOverridden);
        Assert.Equal("Klant onderwerp", overridden.Subject);
    }

    // --- Phase 7: GET api/message-templates/placeholders ---

    [Fact]
    public void Placeholders_KnownEventKey_ReturnsEventTokensPlusGlobalTokens()
    {
        var sut = new MessagingController(null!, null!, null!);

        var result = sut.Placeholders(MessageKinds.OrderAccepted);

        var tokens = UnwrapValue(result);
        Assert.Contains("orderNumber", tokens);
        Assert.Contains("customerName", tokens);
        Assert.Contains("companyName", tokens); // global token
    }

    [Fact]
    public void Placeholders_UnknownOrMissingEventKey_ReturnsOnlyGlobalTokens()
    {
        var sut = new MessagingController(null!, null!, null!);

        Assert.Equal(new[] { "companyName" }, UnwrapValue(sut.Placeholders("does_not_exist")));
        Assert.Equal(new[] { "companyName" }, UnwrapValue(sut.Placeholders(null)));
    }

    // --- Phase 7: outbox filters (channel, recipient search) + related-entity passthrough ---

    private static async Task<OutboxMessage> SeedOutboxRowAsync(
        Harness h, MessageChannel channel, OutboxStatus status, string recipientAddress, string? recipientName = null,
        string? relatedEntityType = null, string? relatedEntityId = null)
    {
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = h.TenantId,
            Channel = channel,
            Kind = MessageKinds.OrderAccepted,
            OwnerType = MessageOwnerType.Customer,
            OwnerId = h.CustomerId,
            RecipientAddress = recipientAddress,
            RecipientName = recipientName,
            Language = "nl",
            Body = "Body",
            Status = status,
            IdempotencyKey = $"order_accepted:{Guid.NewGuid()}",
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        h.Db.Context.OutboxMessages.Add(message);
        await h.Db.Context.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task Outbox_FiltersByChannelAndRecipientSearch()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedOutboxRowAsync(h, MessageChannel.Email, OutboxStatus.Sent, "haven@klant.test", "Haven BV");
        await SeedOutboxRowAsync(h, MessageChannel.Sms, OutboxStatus.Sent, "+32470000000");
        await SeedOutboxRowAsync(h, MessageChannel.Email, OutboxStatus.Sent, "andere@klant.test", "Andere Klant");

        var byChannel = UnwrapValue(await h.Sut.Outbox(null, null, MessageChannel.Sms, null, null, null, CancellationToken.None));
        Assert.Single(byChannel.Items);
        Assert.Equal(MessageChannel.Sms, byChannel.Items[0].Channel);

        var bySearch = UnwrapValue(await h.Sut.Outbox(null, null, null, "haven", null, null, CancellationToken.None));
        Assert.Single(bySearch.Items);
        Assert.Equal("haven@klant.test", bySearch.Items[0].RecipientAddress);
    }

    [Fact]
    public async Task Outbox_IncludesRelatedEntityFields()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedOutboxRowAsync(h, MessageChannel.Email, OutboxStatus.Failed, "haven@klant.test",
            relatedEntityType: "TransportOrder", relatedEntityId: "ORD-42");

        var page = UnwrapValue(await h.Sut.Outbox(OutboxStatus.Failed, null, null, null, null, null, CancellationToken.None));

        var row = Assert.Single(page.Items);
        Assert.Equal("TransportOrder", row.RelatedEntityType);
        Assert.Equal("ORD-42", row.RelatedEntityId);
    }

    [Fact]
    public async Task Outbox_TenantIsolation_OtherTenantRowsNeverReturned()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(), TenantId = otherTenantId, Channel = MessageChannel.Email, Kind = MessageKinds.OrderAccepted,
            OwnerType = MessageOwnerType.Customer, OwnerId = Guid.NewGuid(), RecipientAddress = "other@tenant.test",
            Language = "nl", Body = "Body", Status = OutboxStatus.Sent, IdempotencyKey = $"order_accepted:{Guid.NewGuid()}",
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var page = UnwrapValue(await h.Sut.Outbox(null, null, null, null, null, null, CancellationToken.None));

        Assert.Empty(page.Items);
    }
}
