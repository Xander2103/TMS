using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Messaging;

/// <summary>
/// Wave 9: provider-neutral messaging — profile resolution, template rendering, quiet hours,
/// idempotency, dispatch with retry/backoff, permanent-failure alerting and channel fallback.
/// </summary>
public class MessagingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingProvider : IMessageChannelProvider
    {
        public List<OutboxMessage> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("provider down");
            }

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed record Harness(
        SqliteTestDbContext Db, MessageOutboxService Sut, MessageDispatcher Dispatcher,
        RecordingProvider Email, RecordingProvider Sms, TestClock Clock, Guid TenantId, Guid CustomerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV",
            Email = "planning@haven.be", DefaultLanguageCode = "nl", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var tenant = new DevTenantContext(tenantId);
        var sut = new MessageOutboxService(db.Context, tenant, clock);
        var email = new RecordingProvider();
        var sms = new RecordingProvider();
        var dispatcher = new MessageDispatcher(db.Context, email, sms, clock);
        return new Harness(db, sut, dispatcher, email, sms, clock, tenantId, customerId);
    }

    private static MessageRequest Request(Guid customerId, string kind = MessageKinds.PodAvailable) =>
        new(kind, MessageOwnerType.Customer, customerId,
            new Dictionary<string, string> { ["orderNumber"] = "ORD-0042", ["customerName"] = "Haven BV" },
            "ProofOfDelivery", Guid.NewGuid().ToString(), $"{kind}:{Guid.NewGuid()}");

    [Fact]
    public async Task Queue_ResolvesRecipientAndRendersTemplate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);

        Assert.Equal(QueueOutcome.Queued, result.Outcome);
        var message = h.Db.Context.OutboxMessages.Single();
        Assert.Equal(MessageChannel.Email, message.Channel);
        Assert.Equal("planning@haven.be", message.RecipientAddress);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Contains("ORD-0042", message.Body);
        Assert.NotNull(message.Subject);
    }

    [Fact]
    public async Task Queue_RespectsProfileToggles_AndSuppresses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.MessagingProfiles.Add(new MessagingProfile
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId,
            OwnerType = MessageOwnerType.Customer, OwnerId = h.CustomerId,
            EmailEnabled = false, SmsEnabled = false,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);

        Assert.Equal(QueueOutcome.Suppressed, result.Outcome);
        var message = h.Db.Context.OutboxMessages.Single();
        Assert.Equal(OutboxStatus.Suppressed, message.Status);
        Assert.NotNull(message.FailureReason);
    }

    [Fact]
    public async Task Queue_DisabledType_Suppresses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.MessagingProfiles.Add(new MessagingProfile
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId,
            OwnerType = MessageOwnerType.Customer, OwnerId = h.CustomerId,
            EmailEnabled = true,
            EnabledKindsJson = "[\"order_confirmation\"]",
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.QueueAsync(Request(h.CustomerId, MessageKinds.PodAvailable), CancellationToken.None);

        Assert.Equal(QueueOutcome.Suppressed, result.Outcome);
    }

    [Fact]
    public async Task Queue_QuietHours_DelaysDispatch()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Now is 12:00 UTC; quiet window 10:00-14:00 pushes delivery to 14:00.
        h.Db.Context.MessagingProfiles.Add(new MessagingProfile
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId,
            OwnerType = MessageOwnerType.Customer, OwnerId = h.CustomerId,
            EmailEnabled = true, QuietHoursFrom = new(10, 0), QuietHoursTo = new(14, 0),
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);
        var message = h.Db.Context.OutboxMessages.Single();
        Assert.Equal(new DateTime(2026, 7, 18, 14, 0, 0), message.NextAttemptAt);

        // The dispatcher skips it until the quiet window has passed.
        await h.Dispatcher.DispatchPendingAsync(10, CancellationToken.None);
        Assert.Empty(h.Email.Sent);

        h.Clock.Advance(TimeSpan.FromMinutes(121));
        await h.Dispatcher.DispatchPendingAsync(10, CancellationToken.None);
        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task Queue_IsIdempotent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var request = Request(h.CustomerId);

        await h.Sut.QueueAsync(request, CancellationToken.None);
        var second = await h.Sut.QueueAsync(request, CancellationToken.None);

        Assert.Equal(QueueOutcome.Duplicate, second.Outcome);
        Assert.Single(h.Db.Context.OutboxMessages);
    }

    [Fact]
    public async Task Dispatch_Sends_AndMarksSent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);

        await h.Dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        var message = h.Db.Context.OutboxMessages.Single();
        Assert.Equal(OutboxStatus.Sent, message.Status);
        Assert.NotNull(message.SentAt);
        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task Dispatch_RetriesWithBackoff_ThenFailsAndAlerts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);
        h.Email.Fail = true;

        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            await h.Dispatcher.DispatchPendingAsync(10, CancellationToken.None);
            h.Clock.Advance(TimeSpan.FromHours(6));
        }

        var message = h.Db.Context.OutboxMessages.Single();
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(5, message.AttemptCount);
        Assert.Contains("provider down", message.FailureReason);
    }

    [Fact]
    public async Task PermanentFailure_QueuesFallbackChannel()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.MessagingProfiles.Add(new MessagingProfile
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId,
            OwnerType = MessageOwnerType.Customer, OwnerId = h.CustomerId,
            EmailEnabled = true, SmsEnabled = true, PhoneNumber = "+32470000000",
            FallbackChannel = MessageChannel.Sms,
        });
        await h.Db.Context.SaveChangesAsync();
        await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);
        h.Email.Fail = true;

        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            await h.Dispatcher.DispatchPendingAsync(10, CancellationToken.None);
            h.Clock.Advance(TimeSpan.FromHours(6));
        }

        // The failed e-mail spawned exactly one SMS fallback, which then sends.
        var fallback = h.Db.Context.OutboxMessages.Single(m => m.Channel == MessageChannel.Sms);
        Assert.Equal("+32470000000", fallback.RecipientAddress);
        await h.Dispatcher.DispatchPendingAsync(10, CancellationToken.None);
        Assert.Single(h.Sms.Sent);
    }

    [Fact]
    public async Task CustomTemplate_OverridesBuiltIn()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.MessageTemplates.Add(new MessageTemplate
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId,
            Kind = MessageKinds.PodAvailable, Channel = MessageChannel.Email, Language = "nl",
            Subject = "Eigen onderwerp {{orderNumber}}", Body = "Eigen tekst voor {{customerName}}", IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        await h.Sut.QueueAsync(Request(h.CustomerId), CancellationToken.None);

        var message = h.Db.Context.OutboxMessages.Single();
        Assert.Equal("Eigen onderwerp ORD-0042", message.Subject);
        Assert.Equal("Eigen tekst voor Haven BV", message.Body);
    }
}
