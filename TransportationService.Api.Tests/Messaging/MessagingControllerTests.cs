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
}
