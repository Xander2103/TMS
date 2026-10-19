using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Peppol.Controllers;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Peppol.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Peppol;

/// <summary>
/// Production hardening: B4 (Live fails closed while only the sandbox exists — refused in
/// settings, queue, dispatcher, polling and webhook; never "Delivered"), H3 (atomic Submitting
/// claim with an idempotency key, crash-after-send reconciled by lookup, never a blind resend),
/// EXT-03 (least-recently-polled first) and EXT-04 (webhook storage failure → 503).
/// </summary>
public class PeppolProductionHardeningTests
{
    /// <summary>Counts every provider call; can run a hook INSIDE SendDocument (racing instance)
    /// or throw AFTER recording the send (crash/timeout after the document went out).</summary>
    private sealed class RecordingProvider(string key = SandboxPeppolProvider.ProviderKey, bool live = false) : IPeppolProvider
    {
        private readonly SandboxPeppolProvider _inner = new();

        public int Sends { get; private set; }
        public int Polls { get; private set; }
        public List<string> Keys { get; } = [];
        public Func<Task>? DuringSend { get; set; }
        public bool ThrowAfterSend { get; set; }
        public bool ThrowOnLookup { get; set; }

        public string Key => key;
        public string DisplayName => "Recording";
        public bool SupportsRegistration => false;
        public bool SupportsLiveNetwork => live;

        public Task<PeppolParticipantResult> ValidateParticipantAsync(string scheme, string id, CancellationToken cancellationToken)
            => _inner.ValidateParticipantAsync(scheme, id, cancellationToken);

        public async Task<PeppolSubmissionResult> SendDocumentAsync(PeppolOutboundRequest request, CancellationToken cancellationToken)
        {
            Sends++;
            Keys.Add(request.IdempotencyKey);
            if (DuringSend is { } hook)
            {
                DuringSend = null;
                await hook();
            }

            var result = await _inner.SendDocumentAsync(request, cancellationToken);
            if (ThrowAfterSend)
            {
                ThrowAfterSend = false;
                throw new TimeoutException("connection reset after the request was sent");
            }

            return result;
        }

        public Task<PeppolTransmissionStatusResult> GetTransmissionStatusAsync(string providerMessageId, CancellationToken cancellationToken)
        {
            Polls++;
            return _inner.GetTransmissionStatusAsync(providerMessageId, cancellationToken);
        }

        public Task<PeppolSubmissionLookupResult> FindSubmissionAsync(string idempotencyKey, CancellationToken cancellationToken)
            => ThrowOnLookup
                ? throw new HttpRequestException("lookup unavailable")
                : _inner.FindSubmissionAsync(idempotencyKey, cancellationToken);

        public Task<PeppolDocumentValidationResult> ValidateDocumentAsync(string xml, CancellationToken cancellationToken)
            => _inner.ValidateDocumentAsync(xml, cancellationToken);
    }

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, TestClock Clock, LocalFileStorageService Storage,
        RecordingProvider Provider, PeppolProviderFactory Factory)
    {
        public DevTenantContext Tenant => new(TenantId);

        public PeppolTransmissionService Transmissions() => new(
            Db.Context, Tenant, new AuditService(Db.Context, Tenant, new DevCurrentUserContext(null)),
            new PeppolInvoiceService(Db.Context, Tenant, Factory), Storage, Clock, providerFactory: Factory);

        public PeppolDispatcher Dispatcher(TransportationDbContext? context = null) =>
            new(context ?? Db.Context, Factory, Storage, Clock);

        public PeppolSettingsService Settings() => new(
            Db.Context, Tenant, new AuditService(Db.Context, Tenant, new DevCurrentUserContext(null)), Factory);
    }

    private static async Task<(Harness H, Invoice Invoice, LegalEntity Entity)> SeedAsync(params IPeppolProvider[] extraProviders)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = "t", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var provider = new RecordingProvider();
        var factory = new PeppolProviderFactory([provider, .. extraProviders]);
        var h = new Harness(db, tenantId, new TestClock(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero)),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-peppol-hardening", Guid.NewGuid().ToString("N"))),
            provider, factory);

        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Trans BV",
            VatNumber = "BE0123456749", Iban = "BE68539007547034", CountryCode = "BE",
            PeppolId = "0123456749", PeppolScheme = "0208", InvoicePrefix = "F",
            IsActive = true, IsDefault = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumber = "KL-1", Name = "Acme NV",
            VatNumber = "BE0417497106", CountryCode = "BE", IsActive = true,
            PeppolId = "0417497106", PeppolScheme = "0208", PeppolEnabled = true,
            DefaultLegalEntityId = entity.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Context.LegalEntities.Add(entity);
        db.Context.Customers.Add(customer);
        db.Context.PeppolSettings.Add(new PeppolSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalEntityId = entity.Id,
            Enabled = true, Environment = PeppolEnvironment.Sandbox,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        var invoice = NewInvoice(tenantId, customer, entity, "F2026100001");
        db.Context.Invoices.Add(invoice);
        await db.Context.SaveChangesAsync();
        return (h, invoice, entity);
    }

    private static Invoice NewInvoice(Guid tenantId, Customer customer, LegalEntity entity, string number) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, InvoiceNumber = number,
        CustomerId = customer.Id, LegalEntityId = entity.Id,
        InvoicePeriodYear = 2026, InvoicePeriodMonth = 10,
        InvoiceDate = new DateOnly(2026, 10, 19), DueDate = new DateOnly(2026, 11, 18),
        Status = InvoiceStatus.Sent, Currency = "EUR",
        SellerName = entity.LegalName, SellerVatNumber = entity.VatNumber, SellerIban = entity.Iban,
        CustomerVatTreatment = VatTreatment.DomesticVat.ToString(), CustomerVatNumberSnapshot = customer.VatNumber,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        Lines =
        [
            new InvoiceLine
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Sequence = 1, Description = "Transport",
                Quantity = 1m, UnitPrice = 500m, VatRatePercent = 21m, UnitCode = "C62",
                VatCategoryCode = "S", LedgerAccountNumberSnapshot = "700000",
            },
        ],
    };

    private static async Task<PeppolTransmission> QueueAsync(Harness h, Invoice invoice)
    {
        var dto = await h.Transmissions().QueueAsync(invoice.Id, CancellationToken.None);
        return await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.Id == dto!.Id);
    }

    private static async Task ForceLiveAsync(Harness h, PeppolTransmission transmission)
    {
        // A legacy row from before the fail-closed policy (Live was configurable then).
        transmission.Environment = PeppolEnvironment.Live;
        await h.Db.Context.SaveChangesAsync();
    }

    // --- B4: Live fails closed ------------------------------------------------------------

    [Fact]
    public async Task Settings_Live_WithOnlyTheSandbox_IsRefused_SandboxStaysUsable()
    {
        var (h, _, entity) = await SeedAsync();
        using var _ = h.Db;

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => h.Settings().SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Live", "sandbox", null, null), CancellationToken.None));
        Assert.Contains("Peppol Live-provider niet geconfigureerd", ex.Message);
        // Refused even while disabled: a later "enable" may never go Live through the sandbox.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Settings().SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(false, "Live", "sandbox", null, null), CancellationToken.None));
        Assert.Equal(PeppolEnvironment.Sandbox, (await h.Db.Context.PeppolSettings.AsNoTracking().SingleAsync()).Environment);

        var sandbox = await h.Settings().SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Sandbox", "sandbox", null, null), CancellationToken.None);
        Assert.Equal("Sandbox", sandbox.Environment);
        Assert.False(sandbox.LiveProviderAvailable);
        Assert.False(sandbox.LiveBlocked);
        Assert.False((await h.Settings().GetOverviewAsync(CancellationToken.None)).LiveProviderAvailable);
    }

    [Fact]
    public async Task Settings_Live_IsAccepted_OnlyForALiveCapableProvider()
    {
        var live = new RecordingProvider("acme-ap", live: true);
        var (h, _, entity) = await SeedAsync(live);
        using var _ = h.Db;

        var dto = await h.Settings().SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Live", "acme-ap", null, null), CancellationToken.None);

        Assert.Equal("Live", dto.Environment);
        Assert.True(dto.LiveProviderAvailable);
        Assert.False(dto.LiveBlocked);
        // The sandbox key stays refused for Live even when a live provider exists.
        await Assert.ThrowsAsync<DomainValidationException>(() => h.Settings().SaveSettingsAsync(entity.Id,
            new SavePeppolSettingsRequest(true, "Live", "sandbox", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task ExistingLiveSettings_BlockQueueing_ValidationExplains_NothingStored()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var settings = await h.Db.Context.PeppolSettings.SingleAsync();
        settings.Environment = PeppolEnvironment.Live; // saved before the policy existed
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions().QueueAsync(invoice.Id, CancellationToken.None));
        Assert.Contains("Peppol Live-provider niet geconfigureerd", ex.Message);
        Assert.Equal(0, await h.Db.Context.PeppolTransmissions.CountAsync());

        var validation = await new PeppolInvoiceService(h.Db.Context, h.Tenant, h.Factory)
            .ValidateAsync(invoice.Id, CancellationToken.None);
        Assert.Contains(validation!.Issues, i => i.Code == PeppolLivePolicy.IssueCode);
        var settingsDto = await h.Settings().GetSettingsAsync(settings.LegalEntityId, CancellationToken.None);
        Assert.True(settingsDto.LiveBlocked);
    }

    [Fact]
    public async Task Dispatcher_NeverSubmitsALiveTransmission_ThroughTheSandbox()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        await ForceLiveAsync(h, transmission);

        for (var run = 0; run < 4; run++)
        {
            await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);
            h.Clock.Advance(TimeSpan.FromMinutes(5));
        }

        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Failed, transmission.Status);
        Assert.Contains("Live-provider niet geconfigureerd", transmission.ErrorDetail);
        Assert.Null(transmission.ProviderMessageId);
        Assert.Equal(0, h.Provider.Sends);
        Assert.Equal(0, h.Provider.Polls);
        Assert.False(await h.Db.Context.PeppolTransmissionEvents.AnyAsync(e => e.Status == PeppolTransmissionStatus.Delivered));
        // And a Live retry stays refused.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions().RetryAsync(transmission.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_NeverMarksALiveTransmission_Delivered_ViaSandboxPolling()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None); // Sandbox → SubmittedToProvider
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, transmission.Status);
        var pollsBefore = h.Provider.Polls;
        await ForceLiveAsync(h, transmission); // legacy in-flight "Live via sandbox" row

        for (var run = 0; run < 5; run++)
        {
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);
        }

        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.NotEqual(PeppolTransmissionStatus.Delivered, transmission.Status);
        Assert.Equal(PeppolTransmissionStatus.Failed, transmission.Status);
        Assert.Equal(pollsBefore, h.Provider.Polls); // never polled once Live
        Assert.False(await h.Db.Context.PeppolTransmissionEvents.AnyAsync(e => e.Status == PeppolTransmissionStatus.Delivered));
    }

    [Fact]
    public async Task Webhook_DeliveredForALiveTransmissionViaSandbox_IsIgnored()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        await ForceLiveAsync(h, transmission);

        var outcome = await new PeppolWebhookService(h.Db.Context, h.Clock, h.Storage, providerFactory: h.Factory)
            .ProcessAsync("sandbox", new PeppolWebhookRequest(transmission.ProviderMessageId!, "status", "Delivered"), CancellationToken.None);

        Assert.False(outcome.Accepted);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, transmission.Status);
    }

    [Fact]
    public async Task HealthCheck_IsUnhealthy_ForLiveWithoutProvider_AndHealthyForSandboxOnly()
    {
        var (h, _, _) = await SeedAsync();
        using var _ = h.Db;
        var check = new PeppolHealthCheck(h.Db.Context, h.Factory, h.Clock);

        var healthy = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Equal(false, healthy.Data["liveProviderConfigured"]);
        Assert.Contains("alleen Sandbox", healthy.Description);

        var settings = await h.Db.Context.PeppolSettings.SingleAsync();
        settings.Environment = PeppolEnvironment.Live;
        await h.Db.Context.SaveChangesAsync();

        var unhealthy = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, unhealthy.Status);
        Assert.Contains("Peppol Live-provider niet geconfigureerd", unhealthy.Description);
        Assert.Equal(1, unhealthy.Data["liveSettingsWithoutProvider"]);
    }

    // --- H3: claim, idempotency key, crash recovery -----------------------------------------

    [Fact]
    public async Task ConcurrentDispatchers_SubmitTheTransmissionExactlyOnce()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        using var secondInstance = h.Db.CreateSystemContext();

        // While instance A is inside the provider call, instance B runs a full dispatch pass.
        h.Provider.DuringSend = () => h.Dispatcher(secondInstance).ProcessPendingAsync(10, CancellationToken.None);
        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, h.Provider.Sends);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, transmission.Status);
        Assert.Equal(PeppolDispatcher.BuildIdempotencyKey(transmission), Assert.Single(h.Provider.Keys));
        Assert.Equal(transmission.IdempotencyKey, h.Provider.Keys[0]);
        Assert.NotNull(transmission.SubmissionClaimedAt);
    }

    [Fact]
    public async Task TheClaim_IsAtomic_OnlyOneOfTwoInstancesWins()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        using var other = h.Db.CreateSystemContext();

        var first = await h.Dispatcher().TryClaimAsync(transmission.Id, "k", CancellationToken.None);
        var second = await h.Dispatcher(other).TryClaimAsync(transmission.Id, "k", CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        var row = await other.PeppolTransmissions.AsNoTracking().SingleAsync(t => t.Id == transmission.Id);
        Assert.Equal(PeppolTransmissionStatus.Submitting, row.Status);
        // A claimed (possibly sent) transmission can no longer be cancelled.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions().CancelAsync(transmission.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CrashAfterSend_IsReconciledByLookup_NeverResubmitted()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        h.Provider.ThrowAfterSend = true; // the document reached the provider, the answer was lost

        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Submitting, transmission.Status);
        Assert.Null(transmission.ProviderMessageId);

        // Within the reconcile window nothing happens; it is never picked up as Queued.
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Submitting, transmission.Status);

        h.Clock.Advance(PeppolDispatcher.SubmittingReconcileAfter);
        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();

        Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, transmission.Status);
        Assert.Equal($"sbx-{transmission.CorrelationId:N}", transmission.ProviderMessageId);
        Assert.Equal(1, h.Provider.Sends);
        Assert.True(await h.Db.Context.PeppolTransmissionEvents.AnyAsync(
            e => e.TransmissionId == transmission.Id && e.Detail!.Contains("Hersteld na onderbreking")));
    }

    [Fact]
    public async Task InterruptedClaim_ThatNeverReachedTheProvider_IsRequeued_WithTheSameKey()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        // Crash between the claim and the network call: Submitting, nothing at the provider.
        Assert.True(await h.Dispatcher().TryClaimAsync(transmission.Id, PeppolDispatcher.BuildIdempotencyKey(transmission), CancellationToken.None));
        h.Clock.Advance(PeppolDispatcher.SubmittingReconcileAfter + TimeSpan.FromMinutes(1));

        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);

        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, transmission.Status); // requeued, then sent in the same run
        Assert.Equal(1, transmission.RetryCount);
        Assert.Equal(1, h.Provider.Sends);
        Assert.Equal(PeppolDispatcher.BuildIdempotencyKey(transmission), h.Provider.Keys.Single());
    }

    [Fact]
    public async Task InterruptedClaim_WithAnUnknownLookupAnswer_StaysSubmitting()
    {
        var (h, invoice, _) = await SeedAsync();
        using var _ = h.Db;
        var transmission = await QueueAsync(h, invoice);
        await h.Dispatcher().TryClaimAsync(transmission.Id, "k-unknown", CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(1));
        h.Provider.ThrowOnLookup = true;

        await h.Dispatcher().ProcessPendingAsync(10, CancellationToken.None);

        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Submitting, transmission.Status);
        Assert.Equal(0, h.Provider.Sends);
        var health = await new PeppolHealthCheck(h.Db.Context, h.Factory, h.Clock).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Equal(1, health.Data["interruptedSubmissions"]);
    }

    // --- EXT-03: polling never starves ------------------------------------------------------

    [Fact]
    public async Task Polling_TakesTheLeastRecentlyPolledFirst()
    {
        var (h, invoice, entity) = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.SingleAsync();
        var invoices = new List<Invoice> { invoice };
        for (var i = 2; i <= 3; i++)
        {
            var extra = NewInvoice(h.TenantId, customer, entity, $"F202610000{i}");
            h.Db.Context.Invoices.Add(extra);
            invoices.Add(extra);
        }

        await h.Db.Context.SaveChangesAsync();
        foreach (var each in invoices)
        {
            await QueueAsync(h, each);
        }

        // Batch of 2: the first run submits two and polls those two.
        await h.Dispatcher().ProcessPendingAsync(2, CancellationToken.None);
        // Second run (same minute): submits the third; its poll is due, the other two are not.
        await h.Dispatcher().ProcessPendingAsync(2, CancellationToken.None);

        var rows = await h.Db.Context.PeppolTransmissions.AsNoTracking().ToListAsync();
        Assert.All(rows, row => Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, row.Status));
        Assert.All(rows, row => Assert.True(row.NextPollAt > h.Clock.GetUtcNow().UtcDateTime));
        Assert.Equal(3, h.Provider.Polls); // each polled exactly once — none starved, none twice
    }

    // --- EXT-04: webhook storage failure is not acknowledged ------------------------------------

    private sealed class FailingStorage : IFileStorageService
    {
        public Task<string> SaveAsync(Guid tenantId, string category, string fileName, Stream content, CancellationToken cancellationToken)
            => throw new IOException("disk full");

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
            => throw new IOException("disk full");

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task Webhook_StorageFailure_Answers503_SoTheProviderRedelivers()
    {
        var (h, _, _) = await SeedAsync();
        using var _ = h.Db;
        var service = new PeppolWebhookService(h.Db.Context, h.Clock, new FailingStorage(), providerFactory: h.Factory);
        var controller = new PeppolWebhookController(service,
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Peppol:Webhook:Secret"] = "s3cret" }).Build(), h.Clock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.HttpContext.Request.Headers[PeppolWebhookController.SecretHeaderName] = "s3cret";
        const string body = """
            {"providerMessageId":"in-1","kind":"incoming","document":{"receiverParticipant":"0208:0123456749",
             "supplierParticipant":"0208:1","documentNumber":"LF-1","payloadXml":"<Invoice/>"}}
            """;
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        controller.HttpContext.Request.Body = new MemoryStream(bytes);
        controller.HttpContext.Request.ContentLength = bytes.Length;

        var result = await controller.Receive("sandbox", CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
        Assert.Equal("60", controller.HttpContext.Response.Headers.RetryAfter.ToString());
        Assert.True(Assert.IsType<PeppolWebhookOutcomeDto>(status.Value).RetryLater);
        Assert.Equal(0, await h.Db.Context.PeppolIncomingDocuments.CountAsync());
    }
}
