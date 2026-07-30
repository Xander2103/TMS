using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Partners.Services;
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

/// <summary>Phase 13: transmission lifecycle, dispatcher, webhook idempotency, incoming queue.</summary>
public class PeppolTransmissionTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, TestClock Clock, LocalFileStorageService Storage,
        PeppolTransmissionService Transmissions, PeppolDispatcher Dispatcher,
        PeppolWebhookService Webhook, PeppolIncomingService Incoming);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "t", Slug = "t", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var clock = new TestClock(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var storage = new LocalFileStorageService(
            Path.Combine(Path.GetTempPath(), "ts-peppol-tests", Guid.NewGuid().ToString("N")));
        var factory = new PeppolProviderFactory([new SandboxPeppolProvider()]);
        var invoiceDocuments = new PeppolInvoiceService(db.Context, tenant);
        var transmissions = new PeppolTransmissionService(
            db.Context, tenant, audit, invoiceDocuments, storage, clock);
        var dispatcher = new PeppolDispatcher(db.Context, factory, storage, clock);
        var webhook = new PeppolWebhookService(db.Context, clock, storage);
        var incoming = new PeppolIncomingService(db.Context, tenant, audit);
        return new Harness(db, tenantId, clock, storage, transmissions, dispatcher, webhook, incoming);
    }

    private static async Task<(Customer Customer, LegalEntity Entity)> SeedPartiesAsync(Harness h)
    {
        var entity = new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, LegalName = "Trans BV",
            VatNumber = "BE0123456749", Iban = "BE68539007547034", CountryCode = "BE",
            PeppolId = "0123456749", PeppolScheme = "0208", InvoicePrefix = "F",
            IsActive = true, IsDefault = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, CustomerNumber = "KL-1", Name = "Acme NV",
            VatNumber = "BE0417497106", CountryCode = "BE", IsActive = true,
            PeppolId = "0417497106", PeppolScheme = "0208", PeppolEnabled = true,
            DefaultLegalEntityId = entity.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        h.Db.Context.LegalEntities.Add(entity);
        h.Db.Context.Customers.Add(customer);
        h.Db.Context.PeppolSettings.Add(new PeppolSettings
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, LegalEntityId = entity.Id,
            Enabled = true, Environment = PeppolEnvironment.Sandbox,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await h.Db.Context.SaveChangesAsync();
        return (customer, entity);
    }

    private static async Task<Invoice> SeedSentInvoiceAsync(
        Harness h, Customer customer, LegalEntity entity, string number = "F2026070001")
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, InvoiceNumber = number,
            CustomerId = customer.Id, LegalEntityId = entity.Id,
            InvoicePeriodYear = 2026, InvoicePeriodMonth = 7,
            InvoiceDate = new DateOnly(2026, 7, 30), DueDate = new DateOnly(2026, 8, 29),
            Status = InvoiceStatus.Sent, Currency = "EUR",
            SellerName = entity.LegalName, SellerVatNumber = entity.VatNumber, SellerIban = entity.Iban,
            CustomerVatTreatment = VatTreatment.DomesticVat.ToString(), CustomerVatNumberSnapshot = customer.VatNumber,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Lines =
            [
                new InvoiceLine
                {
                    Id = Guid.NewGuid(), TenantId = h.TenantId, Sequence = 1, Description = "Transport",
                    Quantity = 1m, UnitPrice = 500m, VatRatePercent = 21m, UnitCode = "C62",
                    VatCategoryCode = "S", LedgerAccountNumberSnapshot = "700000",
                },
            ],
        };
        h.Db.Context.Invoices.Add(invoice);
        await h.Db.Context.SaveChangesAsync();
        return invoice;
    }

    // --- Queue ---

    [Fact]
    public async Task Queue_CreatesQueuedTransmission_WithStoredPayloadHashAndEvent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);

        var dto = await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);

        Assert.NotNull(dto);
        Assert.Equal("Queued", dto.Status);
        Assert.Equal(1, dto.PayloadVersion);
        Assert.NotNull(dto.PayloadHash);
        var stored = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoice.Id);
        Assert.Equal("0208:0123456749", stored.SellerParticipant);
        Assert.Equal("0208:0417497106", stored.BuyerParticipant);
        Assert.Equal(PeppolEnvironment.Sandbox, stored.Environment);
        Assert.NotNull(stored.PayloadStorageKey);
        await using var payload = await h.Storage.OpenReadAsync(stored.PayloadStorageKey!, CancellationToken.None);
        using var reader = new StreamReader(payload);
        Assert.Contains(invoice.InvoiceNumber, await reader.ReadToEndAsync());
        Assert.Single(dto.Events, e => e.Status == "Queued");
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a =>
            a.EntityType == "PeppolTransmission" && a.Action == "Queued"));
    }

    [Fact]
    public async Task Queue_DuplicateActiveTransmission_IsRefusedInDutch()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None));
        Assert.Contains("actieve Peppol-verzending", ex.Message);
    }

    [Fact]
    public async Task Queue_InvalidInvoice_ListsValidationIssues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        customer.PeppolId = null;
        customer.PeppolScheme = null;
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None));
        Assert.Contains("Peppol-ID", ex.Message);
    }

    [Fact]
    public async Task Queue_ForeignTenantInvoice_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);

        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreign = new PeppolTransmissionService(
            h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)),
            new PeppolInvoiceService(h.Db.Context, foreignTenant), h.Storage, h.Clock);
        Assert.Null(await foreign.QueueAsync(invoice.Id, CancellationToken.None));
    }

    // --- Dispatcher lifecycle ---

    [Fact]
    public async Task Dispatcher_SubmitsThenPolls_ToDelivered_NeverDeliveredOnSubmit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);

        // Run 1: submit → SubmittedToProvider (poll in the same run repeats the same status).
        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);
        var transmission = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoice.Id);
        Assert.Equal(PeppolTransmissionStatus.SubmittedToProvider, transmission.Status);
        Assert.NotNull(transmission.ProviderMessageId);

        // Run 2: poll → AcceptedByProvider. Run 3: poll → Delivered. Only provider polls deliver.
        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.AcceptedByProvider, transmission.Status);

        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Delivered, transmission.Status);

        var events = await h.Db.Context.PeppolTransmissionEvents
            .Where(e => e.TransmissionId == transmission.Id)
            .OrderBy(e => e.CreatedAt).Select(e => e.Status).ToListAsync();
        Assert.Equal(
            [PeppolTransmissionStatus.Queued, PeppolTransmissionStatus.SubmittedToProvider,
             PeppolTransmissionStatus.AcceptedByProvider, PeppolTransmissionStatus.Delivered],
            events);
    }

    [Fact]
    public async Task Dispatcher_RefusedSubmission_BacksOffThenFailsAfterMaxAttempts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        var dto = await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);
        // Poison the stored payload with the sandbox failure marker.
        var transmission = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.Id == dto!.Id);
        await h.Storage.DeleteAsync(transmission.PayloadStorageKey!, CancellationToken.None);
        using (var poisoned = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<xml>SANDBOX-FAIL</xml>")))
        {
            transmission.PayloadStorageKey = await h.Storage.SaveAsync(
                h.TenantId, "peppol", "poisoned.xml", poisoned, CancellationToken.None);
        }

        await h.Db.Context.SaveChangesAsync();

        // Attempt 1: refused → still Queued with a future NextAttemptAt (backoff).
        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Queued, transmission.Status);
        Assert.Equal(1, transmission.RetryCount);
        Assert.True(transmission.NextAttemptAt > h.Clock.GetUtcNow().UtcDateTime);

        // Not picked up again before the backoff moment.
        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(1, transmission.RetryCount);

        // Fast-forward to the final attempt: MaxAttempts reached → Failed.
        transmission.RetryCount = PeppolDispatcher.MaxAttempts - 1;
        transmission.NextAttemptAt = h.Clock.GetUtcNow().UtcDateTime;
        await h.Db.Context.SaveChangesAsync();
        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Failed, transmission.Status);
        Assert.NotNull(transmission.ErrorDetail);
    }

    [Fact]
    public async Task Retry_CreatesNextVersion_ReusingImmutablePayload()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        var original = await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);
        var stored = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.Id == original!.Id);
        stored.Status = PeppolTransmissionStatus.Failed;
        await h.Db.Context.SaveChangesAsync();

        var retry = await h.Transmissions.RetryAsync(stored.Id, CancellationToken.None);

        Assert.NotNull(retry);
        Assert.Equal(2, retry.PayloadVersion);
        Assert.Equal(original!.PayloadHash, retry.PayloadHash);
        var retryRow = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.Id == retry.Id);
        Assert.Equal(stored.PayloadStorageKey, retryRow.PayloadStorageKey); // same immutable payload
        await h.Db.Context.Entry(stored).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Failed, stored.Status); // original untouched

        // Retry on a non-terminal transmission is refused.
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions.RetryAsync(retry.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_QueuedOnly()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        var dto = await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);

        var cancelled = await h.Transmissions.CancelAsync(dto!.Id, CancellationToken.None);
        Assert.Equal("Cancelled", cancelled!.Status);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Transmissions.CancelAsync(dto.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnvironmentIsStampedPerTransmission_SettingsChangeNeverRewritesHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);

        var settings = await h.Db.Context.PeppolSettings.SingleAsync(s => s.LegalEntityId == entity.Id);
        settings.Environment = PeppolEnvironment.Live;
        await h.Db.Context.SaveChangesAsync();

        var transmission = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoice.Id);
        Assert.Equal(PeppolEnvironment.Sandbox, transmission.Environment);
    }

    [Fact]
    public async Task Dispatcher_CancelledInvoice_WithdrawsQueuedTransmission_BeforeSubmitting()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);
        // Invoice gets cancelled while the transmission waits in the queue.
        var tracked = await h.Db.Context.Invoices.SingleAsync(i => i.Id == invoice.Id);
        tracked.Status = InvoiceStatus.Cancelled;
        await h.Db.Context.SaveChangesAsync();

        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None);

        var transmission = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoice.Id);
        Assert.Equal(PeppolTransmissionStatus.Cancelled, transmission.Status);
        Assert.Null(transmission.ProviderMessageId); // the provider was never contacted
    }

    [Fact]
    public async Task InvoiceCancellation_WithdrawsQueuedTransmissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);

        var tenant = new DevTenantContext(h.TenantId);
        var audit = new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null));
        var invoices = new Modules.Invoicing.Services.InvoiceService(h.Db.Context, tenant, audit, h.Clock,
            new Modules.Invoicing.Services.InvoiceNumberService(h.Db.Context, tenant),
            new CustomerBillingConfigService(h.Db.Context, tenant, audit, h.Clock),
            new Modules.Accounting.Services.AccountingService(h.Db.Context, tenant, audit));

        var result = await invoices.ChangeStatusAsync(invoice.Id, InvoiceStatus.Cancelled, CancellationToken.None);

        Assert.Equal(Modules.Invoicing.Dtos.InvoiceOperationOutcome.Success, result.Outcome);
        var transmission = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoice.Id);
        Assert.Equal(PeppolTransmissionStatus.Cancelled, transmission.Status);
    }

    [Fact]
    public async Task Retry_UsesMaxVersionPlusOne_AcrossAllPriorTransmissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        var v1 = await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);
        var v1Row = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.Id == v1!.Id);
        v1Row.Status = PeppolTransmissionStatus.Failed;
        await h.Db.Context.SaveChangesAsync();
        var v2 = await h.Transmissions.RetryAsync(v1Row.Id, CancellationToken.None);
        var v2Row = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.Id == v2!.Id);
        v2Row.Status = PeppolTransmissionStatus.Failed;
        await h.Db.Context.SaveChangesAsync();

        // Retrying the OLD v1 row after v2 also failed must not reuse version 2.
        var v3 = await h.Transmissions.RetryAsync(v1Row.Id, CancellationToken.None);

        Assert.Equal(3, v3!.PayloadVersion);
    }

    // --- Webhook ---

    [Fact]
    public async Task Webhook_StatusUpdate_IsIdempotent_AndNeverRegresses()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (customer, entity) = await SeedPartiesAsync(h);
        var invoice = await SeedSentInvoiceAsync(h, customer, entity);
        await h.Transmissions.QueueAsync(invoice.Id, CancellationToken.None);
        await h.Dispatcher.ProcessPendingAsync(10, CancellationToken.None); // → SubmittedToProvider
        var transmission = await h.Db.Context.PeppolTransmissions.SingleAsync(t => t.InvoiceId == invoice.Id);
        var messageId = transmission.ProviderMessageId!;

        // Double post of the same delivered status → single transition + single event.
        var first = await h.Webhook.ProcessAsync("sandbox",
            new PeppolWebhookRequest(messageId, "status", "Delivered"), CancellationToken.None);
        var replay = await h.Webhook.ProcessAsync("sandbox",
            new PeppolWebhookRequest(messageId, "status", "Delivered"), CancellationToken.None);
        Assert.True(first.Accepted);
        Assert.True(replay.Accepted);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Delivered, transmission.Status);
        Assert.Equal(1, await h.Db.Context.PeppolTransmissionEvents.CountAsync(
            e => e.TransmissionId == transmission.Id && e.Status == PeppolTransmissionStatus.Delivered));

        // A late "Rejected" after Delivered never regresses the terminal-ish state.
        await h.Webhook.ProcessAsync("sandbox",
            new PeppolWebhookRequest(messageId, "status", "Rejected"), CancellationToken.None);
        await h.Db.Context.Entry(transmission).ReloadAsync();
        Assert.Equal(PeppolTransmissionStatus.Delivered, transmission.Status);

        // Unknown provider message ids are acknowledged-but-ignored.
        var unknown = await h.Webhook.ProcessAsync("sandbox",
            new PeppolWebhookRequest("sbx-unknown", "status", "Delivered"), CancellationToken.None);
        Assert.False(unknown.Accepted);
    }

    [Fact]
    public async Task Webhook_IncomingDocument_ResolvesTenant_AndDeduplicates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await SeedPartiesAsync(h); // entity 0208:0123456749 = receiver

        PeppolWebhookRequest Request(string messageId, string payload = "<Invoice>1</Invoice>") => new(
            messageId, "incoming", Document: new PeppolWebhookIncomingDocument(
                "0208:0123456749", "0208:9999999999", "Leverancier BV", "SupplierInvoice",
                "LF-1", new DateOnly(2026, 7, 20), 250m, "EUR", payload));

        var first = await h.Webhook.ProcessAsync("sandbox", Request("in-1"), CancellationToken.None);
        Assert.True(first.Accepted);
        var document = await h.Db.Context.PeppolIncomingDocuments.SingleAsync();
        Assert.Equal(h.TenantId, document.TenantId);
        Assert.Equal(PeppolIncomingDocumentStatus.Received, document.Status);
        Assert.NotNull(document.PayloadStorageKey);

        // Same message id → deduped; same content under a NEW id → deduped too.
        await h.Webhook.ProcessAsync("sandbox", Request("in-1"), CancellationToken.None);
        await h.Webhook.ProcessAsync("sandbox", Request("in-2"), CancellationToken.None);
        Assert.Equal(1, await h.Db.Context.PeppolIncomingDocuments.CountAsync());

        // Unknown receiver participant → ignored, nothing stored.
        var unknown = await h.Webhook.ProcessAsync("sandbox", new PeppolWebhookRequest(
            "in-3", "incoming", Document: new PeppolWebhookIncomingDocument(
                "0208:0000000000", DocumentNumber: "LF-2", PayloadXml: "<x/>")), CancellationToken.None);
        Assert.False(unknown.Accepted);
        Assert.Equal(1, await h.Db.Context.PeppolIncomingDocuments.CountAsync());
    }

    [Fact]
    public async Task WebhookController_EnforcesSharedSecret()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Peppol:Webhook:Secret"] = "s3cret" })
            .Build();
        var controller = new PeppolWebhookController(h.Webhook, configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var request = new PeppolWebhookRequest("sbx-1", "status", "Delivered");

        // Missing and wrong secrets → 401.
        Assert.IsType<UnauthorizedResult>(await controller.Receive("sandbox", request, CancellationToken.None));
        controller.HttpContext.Request.Headers[PeppolWebhookController.SecretHeaderName] = "wrong";
        Assert.IsType<UnauthorizedResult>(await controller.Receive("sandbox", request, CancellationToken.None));

        // Correct secret → 200 with an outcome body (unknown message: acknowledged, not accepted).
        controller.HttpContext.Request.Headers[PeppolWebhookController.SecretHeaderName] = "s3cret";
        var ok = Assert.IsType<OkObjectResult>(await controller.Receive("sandbox", request, CancellationToken.None));
        Assert.IsType<PeppolWebhookOutcomeDto>(ok.Value);

        // Without ANY configured secret the endpoint refuses everything (secure default).
        var unconfigured = new PeppolWebhookController(h.Webhook, new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        unconfigured.HttpContext.Request.Headers[PeppolWebhookController.SecretHeaderName] = "s3cret";
        Assert.IsType<UnauthorizedResult>(await unconfigured.Receive("sandbox", request, CancellationToken.None));
    }

    // --- Incoming review queue ---

    [Fact]
    public async Task Incoming_ReviewAndReject_AuditAndGuardDoubleDecisions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.PeppolIncomingDocuments.AddRange(
            NewIncoming(h.TenantId, "in-1"), NewIncoming(h.TenantId, "in-2"));
        await h.Db.Context.SaveChangesAsync();
        var page = await h.Incoming.SearchAsync(null, PageRequest.Of(1, 10), CancellationToken.None);
        Assert.Equal(2, page.TotalCount);

        var reviewed = await h.Incoming.MarkReviewedAsync(page.Items[0].Id, " Gekoppeld aan LF-1 ", CancellationToken.None);
        Assert.Equal("Linked", reviewed!.Status);
        Assert.Equal("Gekoppeld aan LF-1", reviewed.ReviewNote);

        var rejected = await h.Incoming.RejectAsync(page.Items[1].Id, null, CancellationToken.None);
        Assert.Equal("Rejected", rejected!.Status);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Incoming.RejectAsync(page.Items[0].Id, null, CancellationToken.None));
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(
            a => a.EntityType == "PeppolIncomingDocument" && a.Action == "Reviewed"));
    }

    [Fact]
    public async Task Incoming_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "x", Slug = "x", CreatedAt = DateTime.UtcNow });
        h.Db.Context.PeppolIncomingDocuments.Add(NewIncoming(foreignTenant, "in-9"));
        await h.Db.Context.SaveChangesAsync();

        var page = await h.Incoming.SearchAsync(null, PageRequest.Of(1, 10), CancellationToken.None);
        Assert.Equal(0, page.TotalCount);
        var foreignDocument = await h.Db.Context.PeppolIncomingDocuments.SingleAsync();
        Assert.Null(await h.Incoming.GetByIdAsync(foreignDocument.Id, CancellationToken.None));
    }

    private static PeppolIncomingDocument NewIncoming(Guid tenantId, string messageId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, SupplierParticipant = "0208:1", SupplierName = "Leverancier",
        DocumentNumber = messageId.ToUpperInvariant(), PayloadHash = new string('a', 64),
        ProviderMessageId = messageId, Status = PeppolIncomingDocumentStatus.Received,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };
}
