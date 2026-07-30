using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Qualifications.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

public interface IPeppolWebhookService
{
    /// <summary>
    /// Applies one provider callback. Idempotent: replays of the same message id / status are
    /// acknowledged without effect. Never throws for bad payloads — the caller always answers
    /// 200 after persist so the provider stops re-delivering; problems land in the log and the
    /// review queue, never as a 500.
    /// </summary>
    Task<PeppolWebhookOutcomeDto> ProcessAsync(string providerKey, PeppolWebhookRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inbound side of the provider seam. Tenant-less by nature (webhooks are anonymous):
/// status updates resolve their tenant via the transmission's ProviderMessageId; incoming
/// documents resolve it via the receiver participant (a legal entity's Peppol identity).
/// </summary>
public class PeppolWebhookService : IPeppolWebhookService
{
    private readonly TransportationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<PeppolWebhookService>? _logger;

    public PeppolWebhookService(
        TransportationDbContext db, TimeProvider timeProvider, IFileStorageService fileStorage,
        ILogger<PeppolWebhookService>? logger = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<PeppolWebhookOutcomeDto> ProcessAsync(
        string providerKey, PeppolWebhookRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderMessageId))
        {
            return new PeppolWebhookOutcomeDto(false, "providerMessageId ontbreekt.");
        }

        // The message id is an idempotency key: truncating it would break dedupe, so an
        // oversized one is rejected outright instead of silently lost on the column limit.
        if (request.ProviderMessageId.Length > 100)
        {
            return new PeppolWebhookOutcomeDto(false, "providerMessageId is te lang (max. 100 tekens).");
        }

        try
        {
            return string.Equals(request.Kind, "incoming", StringComparison.OrdinalIgnoreCase)
                ? await ProcessIncomingAsync(providerKey, request, cancellationToken)
                : await ProcessStatusAsync(providerKey, request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Persist-side failures must not bounce the webhook into an endless redelivery
            // loop; the log carries the evidence for manual follow-up.
            _logger?.LogError(exception, "Peppol webhook processing failed for provider message {ProviderMessageId}.",
                request.ProviderMessageId);
            return new PeppolWebhookOutcomeDto(false, "Verwerking mislukt; gelogd voor opvolging.");
        }
    }

    private async Task<PeppolWebhookOutcomeDto> ProcessStatusAsync(
        string providerKey, PeppolWebhookRequest request, CancellationToken cancellationToken)
    {
        // Scoped to the calling provider: message ids are only unique per provider, and one
        // provider's callbacks must never move another provider's transmissions.
        var transmission = await _db.PeppolTransmissions
            .FirstOrDefaultAsync(
                t => t.ProviderMessageId == request.ProviderMessageId && t.ProviderKey == providerKey,
                cancellationToken);
        if (transmission is null)
        {
            // Uniform wording — no existence oracle for message ids.
            return new PeppolWebhookOutcomeDto(false, "Genegeerd.");
        }

        if (!Enum.TryParse<PeppolTransmissionStatus>(request.Status, ignoreCase: true, out var status)
            || status is not (PeppolTransmissionStatus.AcceptedByProvider
                or PeppolTransmissionStatus.Delivered or PeppolTransmissionStatus.Rejected))
        {
            return new PeppolWebhookOutcomeDto(false, "Onbekende status; genegeerd.");
        }

        // Idempotency + monotony: replays of the current status and updates on a terminal or
        // already-delivered transmission change nothing.
        if (status == transmission.Status
            || transmission.Status is PeppolTransmissionStatus.Delivered or PeppolTransmissionStatus.Failed
                or PeppolTransmissionStatus.Rejected or PeppolTransmissionStatus.Cancelled)
        {
            return new PeppolWebhookOutcomeDto(true, "Al verwerkt.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        transmission.Status = status;
        if (status == PeppolTransmissionStatus.Rejected)
        {
            transmission.ErrorDetail = Truncate(request.Detail) ?? "Geweigerd door het Peppol-netwerk.";
        }

        _db.PeppolTransmissionEvents.Add(new PeppolTransmissionEvent
        {
            Id = Guid.NewGuid(),
            TenantId = transmission.TenantId,
            TransmissionId = transmission.Id,
            Status = status,
            Timestamp = now,
            Detail = Truncate(request.Detail) ?? "Statusmelding via webhook.",
        });
        await _db.SaveChangesAsync(cancellationToken);

        // Webhook-driven mutations are audited like user-driven ones (system actor).
        var tenant = new Modules.Tenancy.Services.DevTenantContext(transmission.TenantId);
        await new Modules.Auditing.Services.AuditService(
                _db, tenant, new Modules.Identity.Services.DevCurrentUserContext(null))
            .RecordAsync("PeppolTransmission", transmission.Id.ToString(), "WebhookStatusChanged",
                null, new { Status = status.ToString(), request.ProviderMessageId }, cancellationToken);

        // Same notifications as the dispatcher's poll path: with a real (webhook-driven)
        // provider this is the ONLY channel that ever learns about delivery or rejection.
        if (status == PeppolTransmissionStatus.Delivered)
        {
            await NotifyAsync(transmission, Modules.Messaging.Entities.MessageKinds.InvoicePeppolDelivered, null, cancellationToken);
        }
        else if (status == PeppolTransmissionStatus.Rejected)
        {
            await NotifyAsync(transmission, Modules.Messaging.Entities.MessageKinds.InvoicePeppolFailed,
                transmission.ErrorDetail ?? "Geweigerd door het Peppol-netwerk.", cancellationToken);
        }

        return new PeppolWebhookOutcomeDto(true, null);
    }

    /// <summary>Per-tenant notification publication from this tenant-less anonymous scope
    /// (mirrors <see cref="PeppolDispatcher"/>); failures never bounce the webhook.</summary>
    private async Task NotifyAsync(
        PeppolTransmission transmission, string eventKey, string? reason, CancellationToken cancellationToken)
    {
        try
        {
            var invoice = await _db.Invoices.AsNoTracking()
                .Where(i => i.TenantId == transmission.TenantId && i.Id == transmission.InvoiceId)
                .Select(i => new { i.Id, i.InvoiceNumber, i.CustomerId })
                .FirstOrDefaultAsync(cancellationToken);
            if (invoice is null)
            {
                return;
            }

            var customerName = await _db.Customers.AsNoTracking()
                .Where(c => c.TenantId == transmission.TenantId && c.Id == invoice.CustomerId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            var tokens = new Dictionary<string, string>
            {
                ["invoiceNumber"] = invoice.InvoiceNumber,
                ["customerName"] = customerName,
            };
            if (reason is not null)
            {
                tokens["reason"] = reason;
            }

            var tenant = new Modules.Tenancy.Services.DevTenantContext(transmission.TenantId);
            var user = new Modules.Identity.Services.DevCurrentUserContext(null);
            var eventService = new Modules.Messaging.Services.NotificationEventService(
                _db, tenant,
                new Modules.Messaging.Services.MessageOutboxService(_db, tenant, _timeProvider),
                new Modules.Notifications.Services.NotificationService(_db, tenant, user, _timeProvider),
                new Modules.Partners.Services.CustomerCommunicationService(
                    _db, tenant, new Modules.Auditing.Services.AuditService(_db, tenant, user)),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
                    .CreateLogger<Modules.Messaging.Services.NotificationEventService>());
            var message = eventKey == Modules.Messaging.Entities.MessageKinds.InvoicePeppolDelivered
                ? $"Factuur {invoice.InvoiceNumber} is afgeleverd via Peppol."
                : $"Peppol-verzending van factuur {invoice.InvoiceNumber} is mislukt: {reason}";
            await eventService.PublishAsync(eventKey,
                new Modules.Messaging.Services.NotificationEventContext("Invoice", invoice.Id.ToString(), tokens)
                {
                    CustomerId = invoice.CustomerId,
                    LinkPath = $"/invoices/{invoice.Id}",
                    InAppMessage = message,
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Peppol webhook notification '{EventKey}' failed for transmission {TransmissionId}.",
                eventKey, transmission.Id);
        }
    }

    private async Task<PeppolWebhookOutcomeDto> ProcessIncomingAsync(
        string providerKey, PeppolWebhookRequest request, CancellationToken cancellationToken)
    {
        var document = request.Document;
        if (document is null || string.IsNullOrWhiteSpace(document.ReceiverParticipant))
        {
            return new PeppolWebhookOutcomeDto(false, "Documentgegevens of ontvanger ontbreken.");
        }

        var (scheme, id) = SplitParticipant(document.ReceiverParticipant);
        var tenantId = await _db.LegalEntities.AsNoTracking()
            .Where(e => e.PeppolScheme == scheme && e.PeppolId == id && e.IsActive)
            .Select(e => (Guid?)e.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId is null)
        {
            _logger?.LogWarning("Peppol incoming document for unknown receiver {Receiver}; ignored.", document.ReceiverParticipant);
            // Uniform wording — no existence oracle for receiver participants.
            return new PeppolWebhookOutcomeDto(false, "Genegeerd.");
        }

        // Idempotency 1: the provider's message id is unique per tenant.
        var duplicateById = await _db.PeppolIncomingDocuments.AnyAsync(
            d => d.TenantId == tenantId && d.ProviderMessageId == request.ProviderMessageId, cancellationToken);
        if (duplicateById)
        {
            return new PeppolWebhookOutcomeDto(true, "Al ontvangen (zelfde berichtreferentie).");
        }

        var payload = document.PayloadXml ?? string.Empty;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        var supplierParticipant = Truncate(document.SupplierParticipant, 80) ?? string.Empty;
        var documentNumber = Truncate(document.DocumentNumber, 100) ?? string.Empty;

        // Idempotency 2: same content from the same supplier under a new message id → duplicate.
        var duplicateByContent = await _db.PeppolIncomingDocuments.AnyAsync(
            d => d.TenantId == tenantId && d.PayloadHash == hash
                 && d.SupplierParticipant == supplierParticipant
                 && d.DocumentNumber == documentNumber, cancellationToken);
        if (duplicateByContent)
        {
            return new PeppolWebhookOutcomeDto(true, "Al ontvangen (zelfde inhoud).");
        }

        string? storageKey = null;
        if (payload.Length > 0)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            storageKey = await _fileStorage.SaveAsync(
                tenantId.Value, "peppol", $"incoming-{request.ProviderMessageId}.xml", stream, cancellationToken);
        }

        _db.PeppolIncomingDocuments.Add(new PeppolIncomingDocument
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            DocumentKind = Enum.TryParse<PeppolIncomingDocumentKind>(document.DocumentKind, ignoreCase: true, out var kind)
                ? kind
                : PeppolIncomingDocumentKind.SupplierInvoice,
            SupplierParticipant = supplierParticipant,
            SupplierName = Truncate(document.SupplierName, 200),
            DocumentNumber = documentNumber,
            DocumentDate = document.DocumentDate,
            Amount = document.Amount,
            Currency = Truncate(document.Currency, 3),
            PayloadStorageKey = storageKey,
            PayloadHash = hash,
            Status = PeppolIncomingDocumentStatus.Received,
            ProviderMessageId = request.ProviderMessageId,
        });
        await _db.SaveChangesAsync(cancellationToken);
        return new PeppolWebhookOutcomeDto(true, null);
    }

    private static (string Scheme, string Id) SplitParticipant(string participant)
    {
        var separator = participant.IndexOf(':');
        return separator < 0
            ? (string.Empty, participant)
            : (participant[..separator], participant[(separator + 1)..]);
    }

    private static string? Truncate(string? value, int max = 2000) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
