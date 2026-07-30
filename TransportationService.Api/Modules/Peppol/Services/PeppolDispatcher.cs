using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Services;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

/// <summary>
/// Outbox-style background worker for Peppol transmissions, across ALL tenants (rows carry
/// their TenantId; per-tenant services are constructed explicitly, the hosted-service
/// pattern used by the expiry sweep). Two passes per run:
/// 1. submit: Queued rows whose NextAttemptAt has passed → provider SendDocument →
///    SubmittedToProvider / retry with exponential backoff (5m × 2^n, max 5) → Failed;
/// 2. poll: SubmittedToProvider/AcceptedByProvider rows → provider GetTransmissionStatus →
///    AcceptedByProvider / Delivered / Rejected.
/// Delivered is ONLY ever set from a provider status response — never on local success.
/// </summary>
public class PeppolDispatcher
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(5);

    private readonly TransportationDbContext _db;
    private readonly IPeppolProviderFactory _providerFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<PeppolDispatcher>? _logger;

    public PeppolDispatcher(
        TransportationDbContext db, IPeppolProviderFactory providerFactory,
        IFileStorageService fileStorage, TimeProvider timeProvider, ILoggerFactory? loggerFactory = null)
    {
        _db = db;
        _providerFactory = providerFactory;
        _fileStorage = fileStorage;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<PeppolDispatcher>();
    }

    public async Task ProcessPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var toSubmit = await _db.PeppolTransmissions
            .Where(t => t.Status == PeppolTransmissionStatus.Queued
                        && (t.NextAttemptAt == null || t.NextAttemptAt <= now))
            .OrderBy(t => t.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var transmission in toSubmit)
        {
            await SubmitAsync(transmission, cancellationToken);
        }

        var toPoll = await _db.PeppolTransmissions
            .Where(t => t.Status == PeppolTransmissionStatus.SubmittedToProvider
                        || t.Status == PeppolTransmissionStatus.AcceptedByProvider)
            .OrderBy(t => t.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var transmission in toPoll)
        {
            await PollAsync(transmission, cancellationToken);
        }
    }

    private async Task SubmitAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Second belt behind the invoice-cancel hook: a transmission whose invoice is no longer
        // Sent/Paid (cancelled in the backoff window) must never reach the network.
        var invoiceStatus = await _db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == transmission.TenantId && i.Id == transmission.InvoiceId)
            .Select(i => (Invoicing.Entities.InvoiceStatus?)i.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoiceStatus is not (Invoicing.Entities.InvoiceStatus.Sent or Invoicing.Entities.InvoiceStatus.Paid))
        {
            transmission.Status = PeppolTransmissionStatus.Cancelled;
            AddEvent(transmission, PeppolTransmissionStatus.Cancelled, now,
                "Factuur is niet langer verzonden/betaald; verzending vervallen.");
            await SaveGuardedAsync(transmission, cancellationToken);
            return;
        }

        try
        {
            var provider = _providerFactory.Resolve(transmission.ProviderKey);
            var xml = await ReadPayloadAsync(transmission, cancellationToken);
            var (sellerScheme, sellerId) = SplitParticipant(transmission.SellerParticipant);
            var (buyerScheme, buyerId) = SplitParticipant(transmission.BuyerParticipant);

            var result = await provider.SendDocumentAsync(new PeppolOutboundRequest(
                sellerScheme, sellerId, buyerScheme, buyerId, xml,
                transmission.PayloadHash ?? string.Empty, transmission.CorrelationId), cancellationToken);

            if (result.Accepted)
            {
                transmission.Status = PeppolTransmissionStatus.SubmittedToProvider;
                transmission.ProviderMessageId = result.ProviderMessageId;
                AddEvent(transmission, PeppolTransmissionStatus.SubmittedToProvider, now,
                    $"Aangeboden aan provider ({transmission.ProviderKey}).");
            }
            else
            {
                await HandleSubmitFailureAsync(transmission, Sanitize(result.Error) ?? "Provider weigerde het document.", now, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Peppol submit failed for transmission {TransmissionId}.", transmission.Id);
            await HandleSubmitFailureAsync(transmission, "Technische fout bij het aanbieden aan de provider.", now, cancellationToken);
        }

        await SaveGuardedAsync(transmission, cancellationToken);
    }

    /// <summary>
    /// Saves a transmission transition; when a concurrent actor (cancel, webhook) changed the
    /// status first, OUR transition is discarded — the other side's decision wins and the next
    /// run re-evaluates from fresh state.
    /// </summary>
    private async Task SaveGuardedAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger?.LogWarning("Concurrent status change on Peppol transmission {TransmissionId}; local transition discarded.",
                transmission.Id);
            var entry = _db.Entry(transmission);
            await entry.ReloadAsync(cancellationToken);
            // Drop any event rows staged for the discarded transition.
            foreach (var staged in _db.ChangeTracker.Entries<PeppolTransmissionEvent>()
                         .Where(e => e.State == EntityState.Added && e.Entity.TransmissionId == transmission.Id)
                         .ToList())
            {
                staged.State = EntityState.Detached;
            }
        }
    }

    private async Task HandleSubmitFailureAsync(
        PeppolTransmission transmission, string reason, DateTime now, CancellationToken cancellationToken)
    {
        transmission.RetryCount++;
        transmission.ErrorDetail = reason;
        if (transmission.RetryCount >= MaxAttempts)
        {
            transmission.Status = PeppolTransmissionStatus.Failed;
            AddEvent(transmission, PeppolTransmissionStatus.Failed, now,
                $"Definitief mislukt na {transmission.RetryCount} pogingen: {reason}");
            await NotifyAsync(transmission, MessageKinds.InvoicePeppolFailed, reason, cancellationToken);
        }
        else
        {
            // Stays Queued; the backoff moment gates the next attempt.
            transmission.NextAttemptAt = now + BaseBackoff * Math.Pow(2, transmission.RetryCount - 1);
            AddEvent(transmission, PeppolTransmissionStatus.Queued, now,
                $"Poging {transmission.RetryCount} mislukt: {reason} Volgende poging gepland.");
        }
    }

    private async Task PollAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        if (transmission.ProviderMessageId is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            var provider = _providerFactory.Resolve(transmission.ProviderKey);
            var status = await provider.GetTransmissionStatusAsync(transmission.ProviderMessageId, cancellationToken);
            if (!Enum.TryParse<PeppolTransmissionStatus>(status.Status, ignoreCase: true, out var mapped)
                || mapped == transmission.Status)
            {
                return;
            }

            switch (mapped)
            {
                case PeppolTransmissionStatus.AcceptedByProvider:
                    transmission.Status = mapped;
                    AddEvent(transmission, mapped, now, Sanitize(status.Detail));
                    break;
                case PeppolTransmissionStatus.Delivered:
                    transmission.Status = mapped;
                    AddEvent(transmission, mapped, now, Sanitize(status.Detail));
                    await NotifyAsync(transmission, MessageKinds.InvoicePeppolDelivered, null, cancellationToken);
                    break;
                case PeppolTransmissionStatus.Rejected:
                    transmission.Status = mapped;
                    transmission.ErrorDetail = Sanitize(status.Detail) ?? "Geweigerd door het Peppol-netwerk.";
                    AddEvent(transmission, mapped, now, transmission.ErrorDetail);
                    await NotifyAsync(transmission, MessageKinds.InvoicePeppolFailed, transmission.ErrorDetail, cancellationToken);
                    break;
                default:
                    return; // never regress or jump to unexpected states from a poll
            }

            await SaveGuardedAsync(transmission, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Polling is repeated forever by the next run; a transient failure changes nothing.
            _logger?.LogError(exception, "Peppol status poll failed for transmission {TransmissionId}.", transmission.Id);
        }
    }

    private async Task<string> ReadPayloadAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        if (transmission.PayloadStorageKey is null)
        {
            throw new InvalidOperationException("Transmission has no stored payload.");
        }

        await using var stream = await _fileStorage.OpenReadAsync(transmission.PayloadStorageKey, cancellationToken);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private void AddEvent(PeppolTransmission transmission, PeppolTransmissionStatus status, DateTime timestamp, string? detail)
        => _db.PeppolTransmissionEvents.Add(new PeppolTransmissionEvent
        {
            Id = Guid.NewGuid(),
            TenantId = transmission.TenantId,
            TransmissionId = transmission.Id,
            Status = status,
            Timestamp = timestamp,
            Detail = detail,
        });

    /// <summary>Per-tenant notification publication from a tenant-less background scope.</summary>
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

            var message = eventKey == MessageKinds.InvoicePeppolDelivered
                ? $"Factuur {invoice.InvoiceNumber} is afgeleverd via Peppol."
                : $"Peppol-verzending van factuur {invoice.InvoiceNumber} is mislukt: {reason}";

            await BuildEventService(transmission.TenantId).PublishAsync(eventKey,
                new NotificationEventContext("Invoice", invoice.Id.ToString(), tokens)
                {
                    CustomerId = invoice.CustomerId,
                    LinkPath = $"/invoices/{invoice.Id}",
                    InAppMessage = message,
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Peppol notification '{EventKey}' failed for transmission {TransmissionId}.",
                eventKey, transmission.Id);
        }
    }

    private INotificationEventService BuildEventService(Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        return new NotificationEventService(
            _db, tenant,
            new MessageOutboxService(_db, tenant, _timeProvider),
            new NotificationService(_db, tenant, user, _timeProvider),
            new CustomerCommunicationService(_db, tenant, new AuditService(_db, tenant, user)),
            (_loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
                .CreateLogger<NotificationEventService>());
    }

    private static (string Scheme, string Id) SplitParticipant(string participant)
    {
        var separator = participant.IndexOf(':');
        return separator < 0
            ? (string.Empty, participant)
            : (participant[..separator], participant[(separator + 1)..]);
    }

    /// <summary>Provider errors are surfaced verbatim ONLY when they carry no secrets; our own
    /// providers return sanitized Dutch text. Truncated to the column limit.</summary>
    private static string? Sanitize(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? null : detail.Length <= 2000 ? detail : detail[..2000];
}

/// <summary>Drains the Peppol transmission queue every 30 seconds.</summary>
public sealed class PeppolDispatcherHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PeppolDispatcherHostedService> _logger;

    public PeppolDispatcherHostedService(
        IServiceScopeFactory scopeFactory, ILogger<PeppolDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = new PeppolDispatcher(
                    scope.ServiceProvider.GetRequiredService<TransportationDbContext>(),
                    scope.ServiceProvider.GetRequiredService<IPeppolProviderFactory>(),
                    scope.ServiceProvider.GetRequiredService<IFileStorageService>(),
                    scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                    scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
                await dispatcher.ProcessPendingAsync(50, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Peppol dispatch run failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
