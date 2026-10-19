using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Controllers;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Identity;

/// <summary>
/// H9: activation and password-reset links leave through the outbox in EVERY environment
/// (Production included), bypass recipient opt-outs, keep the anonymous answer uniform and keep
/// the token hygiene (one-way idempotency key, body scrubbed after delivery, hidden from the
/// outbox detail API).
/// </summary>
public class AccountMailDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private const string BaseUrl = "https://tms.acme.example";

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId, Guid EmployeeId)
    {
        public UserAccountFlowService Anonymous() => new(
            Db.Context,
            new RequestTenantAccessor(new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            new PasswordHasher(),
            new AuditService(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(null)),
            new TestClock(Now), new HostingEnvironment { EnvironmentName = "Production" },
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Frontend:BaseUrl"] = BaseUrl }).Build());

        public UserAccountFlowService Admin()
        {
            var tenant = new DevTenantContext(TenantId);
            return new UserAccountFlowService(
                Db.Context, tenant, new PasswordHasher(),
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)),
                new TestClock(Now), new HostingEnvironment { EnvironmentName = "Production" },
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Frontend:BaseUrl"] = BaseUrl }).Build());
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme Transport", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "jan@acme.example", FirstName = "Jan", LastName = "Janssens",
            EmployeeId = employeeId, PreferredLanguageCode = "fr",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        // The employee switched e-mail off entirely: account-security mail must still go out.
        db.Context.MessagingProfiles.Add(new MessagingProfile
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OwnerType = MessageOwnerType.Employee, OwnerId = employeeId,
            EmailEnabled = false, SmsEnabled = false, EnabledKindsJson = "[]",
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId, employeeId);
    }

    [Fact]
    public async Task PasswordReset_InProduction_IsQueuedThroughTheOutbox()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Anonymous().RequestPasswordResetAsync("  JAN@acme.example ", CancellationToken.None);

        var mail = await h.Db.Context.OutboxMessages.SingleAsync();
        Assert.Equal(MessageKinds.PasswordReset, mail.Kind);
        Assert.Equal(h.TenantId, mail.TenantId);
        Assert.Equal(OutboxStatus.Pending, mail.Status); // not suppressed by the profile's opt-out
        Assert.Equal(MessageChannel.Email, mail.Channel);
        Assert.Equal("jan@acme.example", mail.RecipientAddress);
        Assert.Equal("fr", mail.Language);
        Assert.Contains($"{BaseUrl}/reset-password?token=", mail.Body);
        Assert.Contains("2 heures", mail.Body);
        Assert.Contains("Acme Transport", mail.Subject);
        Assert.Empty(MessageTemplateRenderer.Unresolved(mail.Subject, mail.Body));

        // The link's token is the live reset credential, the idempotency key only a one-way reference.
        var token = mail.Body[(mail.Body.IndexOf("token=", StringComparison.Ordinal) + 6)..].Split('\n')[0].Trim();
        Assert.DoesNotContain(token, mail.IdempotencyKey);
        Assert.Null(await h.Admin().CompleteWithTokenAsync(token, "NieuwWachtwoord1!", CancellationToken.None));
    }

    [Fact]
    public async Task PasswordReset_ForAnUnknownAddress_QueuesNothing_AndDoesNotThrow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Anonymous().RequestPasswordResetAsync("niemand@acme.example", CancellationToken.None);

        Assert.Equal(0, await h.Db.Context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task PasswordReset_RateLimit_AlsoLimitsTheMails()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var flow = h.Anonymous();

        for (var i = 0; i < 5; i++)
        {
            await flow.RequestPasswordResetAsync("jan@acme.example", CancellationToken.None);
        }

        Assert.Equal(3, await h.Db.Context.OutboxMessages.CountAsync(m => m.Kind == MessageKinds.PasswordReset));
    }

    [Fact]
    public async Task Activation_IsQueuedAsMail_AndStillReturnedOnceToTheAdministrator()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var started = await h.Admin().StartActivationAsync(h.UserId, CancellationToken.None);

        Assert.NotNull(started);
        Assert.True(started!.EmailQueued);
        var mail = await h.Db.Context.OutboxMessages.SingleAsync();
        Assert.Equal(MessageKinds.UserActivation, mail.Kind);
        Assert.Equal(OutboxStatus.Pending, mail.Status);
        Assert.Contains($"{BaseUrl}/activeren?token={started.Token}&email=jan%40acme.example", mail.Body);
    }

    [Fact]
    public async Task AccountMails_AreScrubbedAfterDelivery()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Anonymous().RequestPasswordResetAsync("jan@acme.example", CancellationToken.None);
        await h.Admin().StartActivationAsync(h.UserId, CancellationToken.None);

        var email = new RecordingProvider();
        var dispatcher = new MessageDispatcher(h.Db.Context, email, new RecordingProvider(), new TestClock(Now));
        Assert.Equal(2, await dispatcher.DispatchPendingAsync(10, CancellationToken.None));

        Assert.All(email.SentBodies, body => Assert.Contains("token=", body)); // SMTP got the link
        var rows = await h.Db.Context.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal(OutboxStatus.Sent, row.Status);
            Assert.Equal(MessageDispatcher.ScrubbedBodyMarker, row.Body);
        });
    }

    [Fact]
    public async Task OutboxDetail_HidesAPendingOneTimeLink_FromMessagingAdmins()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Anonymous().RequestPasswordResetAsync("jan@acme.example", CancellationToken.None);
        var id = (await h.Db.Context.OutboxMessages.SingleAsync()).Id;
        var tenant = new DevTenantContext(h.TenantId);

        var controller = new MessagingController(h.Db.Context, tenant,
            new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));
        var result = Assert.IsType<OkObjectResult>(await controller.OutboxDetail(id, CancellationToken.None));

        var row = Assert.IsType<OutboxMessage>(result.Value);
        Assert.Equal(MessagingController.CredentialBodyHidden, row.Body);
        Assert.DoesNotContain("token=", row.Body);
        // The durable row still carries the link for the dispatcher.
        Assert.Contains("token=", (await h.Db.Context.OutboxMessages.AsNoTracking().SingleAsync()).Body);
    }

    [Fact]
    public void AccountMailKinds_CarryAOneTimeCredential_AndAreNotTenantOverridable()
    {
        Assert.True(MessageKinds.CarriesOneTimeCredential(MessageKinds.UserActivation));
        Assert.True(MessageKinds.CarriesOneTimeCredential(MessageKinds.PasswordReset));
        Assert.DoesNotContain(MessageKinds.UserActivation, MessageKinds.All);
        Assert.DoesNotContain(MessageKinds.PasswordReset, MessageKinds.All);
    }

    private sealed class RecordingProvider : IMessageChannelProvider
    {
        public List<string> SentBodies { get; } = [];

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            SentBodies.Add(message.Body);
            return Task.CompletedTask;
        }
    }
}
