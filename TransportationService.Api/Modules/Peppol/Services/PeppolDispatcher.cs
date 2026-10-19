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
/// pattern used by the expiry sweep). Three passes per run:
/// 0. reconcile: Submitting rows older than <see cref="SubmittingReconcileAfter"/> (a crash or
///    timeout around the network call) → provider lookup BY IDEMPOTENCY KEY → SubmittedToProvider
///    when the provider has it, back to Queued (same key) only when the provider confirms it
///    does not; an unknown answer leaves the row Submitting. Never a blind resubmit (H3).
/// 1. submit: Queued rows whose NextAttemptAt has passed → atomic claim (conditional UPDATE
///    Queued → Submitting + idempotency key, so two dispatchers/instances can never both send)
///    → provider SendDocument with the key → SubmittedToProvider / retry with exponential
///    backoff (5m × 2^n, max 5) → Failed;
/// 2. poll: SubmittedToProvider/AcceptedByProvider rows, least recently polled first with an
///    age-based backoff (EXT-03) → provider GetTransmissionStatus → AcceptedByProvider /
///    Delivered / Rejected.
/// Delivered is ONLY ever set from a provider status response — never on local success — and a
/// Live transmission is never submitted, polled or delivered through a provider that cannot
/// transmit to the live network (B4, <see cref="PeppolLivePolicy"/>). Notifications are
/// published only AFTER the guarded save of the transition they announce.
/// </summary>
public class PeppolDispatcher
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(5);

    /// <summary>A claim older than this is considered interrupted and reconciled via lookup.
    /// Far above any provider call timeout, so a slow but live send is never second-guessed.</summary>
    public static readonly TimeSpan SubmittingReconcileAfter = TimeSpan.FromMinutes(10);

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

    /// <summary>The provider-facing idempotency key of a transmission (stable, no secrets).</summary>
    public static string BuildIdempotencyKey(PeppolTransmission transmission) =>
        $"tms-peppol-{transmission.Id:N}";

    /// <summary>EXT-03: poll young transmissions often, old never-final ones rarely.</summary>
    public static TimeSpan PollDelay(TimeSpan age) =>
        age < TimeSpan.FromHours(1) ? TimeSpan.FromMinutes(1)
        : age < TimeSpan.FromDays(1) ? TimeSpan.FromMinutes(15)
        : TimeSpan.FromHours(1);

    public async Task ProcessPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await ReconcileInterruptedSubmissionsAsync(now, batchSize, cancellationToken);

        var toSubmit = await _db.PeppolTransmissions.AsNoTracking()
            .Where(t => t.Status == PeppolTransmissionStatus.Queued
                        && (t.NextAttemptAt == null || t.NextAttemptAt <= now))
            .OrderBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var transmissionId in toSubmit)
        {
            await SubmitAsync(transmissionId, cancellationToken);
        }

        var pollNow = _timeProvider.GetUtcNow().UtcDateTime;
        var toPoll = await _db.PeppolTransmissions.AsNoTracking()
            .Where(t => (t.Status == PeppolTransmissionStatus.SubmittedToProvider
                         || t.Status == PeppolTransmissionStatus.AcceptedByProvider)
                        && (t.NextPollAt == null || t.NextPollAt <= pollNow))
            .OrderBy(t => t.NextPollAt == null ? 0 : 1)
            .ThenBy(t => t.NextPollAt)
            .ThenBy(t => t.CreatedAt)
            .Select(t => t.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        foreach (var transmissionId in toPoll)
        {
            if (await LoadFreshAsync(transmissionId, cancellationToken) is { Status: PeppolTransmissionStatus.SubmittedToProvider or PeppolTransmissionStatus.AcceptedByProvider } transmission)
            {
                await PollAsync(transmission, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Current database state of one transmission. A row this context already tracks is RELOADED
    /// (a plain query would hand back the tracked, possibly stale instance) — every decision of the
    /// dispatcher is taken on fresh state, and the status concurrency token carries that state.
    /// </summary>
    private async Task<PeppolTransmission?> LoadFreshAsync(Guid transmissionId, CancellationToken cancellationToken)
    {
        var tracked = _db.ChangeTracker.Entries<PeppolTransmission>().FirstOrDefault(e => e.Entity.Id == transmissionId);
        if (tracked is null)
        {
            return await _db.PeppolTransmissions.FirstOrDefaultAsync(t => t.Id == transmissionId, cancellationToken);
        }

        await tracked.ReloadAsync(cancellationToken);
        return tracked.State == EntityState.Detached ? null : tracked.Entity;
    }

    /// <summary>
    /// H3: the atomic claim. One conditional UPDATE (Queued and due → Submitting + key + claim
    /// time); exactly one caller — across dispatchers, scopes and API instances — sees one
    /// affected row. Everyone else must leave the transmission alone.
    /// </summary>
    public async Task<bool> TryClaimAsync(Guid transmissionId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var affected = await _db.PeppolTransmissions
            .Where(t => t.Id == transmissionId
                        && t.Status == PeppolTransmissionStatus.Queued
                        && (t.NextAttemptAt == null || t.NextAttemptAt <= now))
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.Status, PeppolTransmissionStatus.Submitting)
                .SetProperty(t => t.IdempotencyKey, idempotencyKey)
                .SetProperty(t => t.SubmissionClaimedAt, now)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);
        return affected == 1;
    }

    private async Task SubmitAsync(Guid transmissionId, CancellationToken cancellationToken)
    {
        var transmission = await LoadFreshAsync(transmissionId, cancellationToken);
        if (transmission is null || transmission.Status != PeppolTransmissionStatus.Queued)
        {
            return;
        }

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
            await SaveGuardedAsync(transmission, CancellationToken.None);
            return;
        }

        // B4: a Live transmission never goes through a provider that cannot reach the live
        // network (today: the sandbox, which only simulates). Nothing is sent; the row fails
        // terminally with the reason, so nobody believes the invoice left the building.
        if (PeppolLivePolicy.IsBlocked(_providerFactory, transmission.Environment, transmission.ProviderKey))
        {
            await FailLiveWithoutProviderAsync(transmission, now, cancellationToken);
            return;
        }

        // H3: claim + key persisted BEFORE the provider is contacted.
        var key = transmission.IdempotencyKey ?? BuildIdempotencyKey(transmission);
        if (!await TryClaimAsync(transmission.Id, key, cancellationToken))
        {
            // Another dispatcher/instance owns it (or a cancel won); never send it here.
            await _db.Entry(transmission).ReloadAsync(cancellationToken);
            return;
        }

        await _db.Entry(transmission).ReloadAsync(cancellationToken); // Status = Submitting (token original value)

        IPeppolProvider provider;
        string xml;
        try
        {
            provider = _providerFactory.Resolve(transmission.ProviderKey);
            xml = await ReadPayloadAsync(transmission, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Definitely nothing sent yet: an ordinary failed attempt.
            _logger?.LogError(exception, "Peppol submit preparation failed for transmission {TransmissionId}.", transmission.Id);
            var notify = HandleSubmitFailure(transmission, "Technische fout bij het voorbereiden van de verzending.", now);
            await SaveAndNotifyAsync(transmission, notify, cancellationToken);
            return;
        }

        PeppolSubmissionResult result;
        try
        {
            var (sellerScheme, sellerId) = SplitParticipant(transmission.SellerParticipant);
            var (buyerScheme, buyerId) = SplitParticipant(transmission.BuyerParticipant);
            result = await provider.SendDocumentAsync(new PeppolOutboundRequest(
                sellerScheme, sellerId, buyerScheme, buyerId, xml,
                transmission.PayloadHash ?? string.Empty, transmission.CorrelationId, key), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Outcome UNKNOWN (timeout, connection reset after the request went out, …): the row
            // stays Submitting and is reconciled by key lookup — a resend could double-submit.
            _logger?.LogError(exception,
                "Peppol submit outcome unknown for transmission {TransmissionId}; left Submitting for reconciliation.",
                transmission.Id);
            AddEvent(transmission, PeppolTransmissionStatus.Submitting, now,
                "Uitkomst van het aanbieden onbekend (technische fout); wordt nagekeken bij de provider, niet opnieuw verzonden.");
            await SaveGuardedAsync(transmission, CancellationToken.None);
            return;
        }

        PendingNotification? notification = null;
        if (result.Accepted)
        {
            transmission.Status = PeppolTransmissionStatus.SubmittedToProvider;
            transmission.ProviderMessageId = result.ProviderMessageId;
            transmission.NextPollAt = now; // first status poll right away
            AddEvent(transmission, PeppolTransmissionStatus.SubmittedToProvider, now,
                $"Aangeboden aan provider ({transmission.ProviderKey}).");
        }
        else
        {
            notification = HandleSubmitFailure(transmission, Sanitize(result.Error) ?? "Provider weigerde het document.", now);
        }

        // The document may be out: persist the outcome even when shutdown was requested.
        await SaveAndNotifyAsync(transmission, notification, CancellationToken.None);
    }

    /// <summary>
    /// H3 crash recovery for claims older than <see cref="SubmittingReconcileAfter"/>: ask the
    /// provider whether it has the key. Found → SubmittedToProvider with the provider's id. Not
    /// found → the document never arrived: Queued again (same key, counts as an attempt). Unknown
    /// (lookup throws) → untouched, asked again next run. Never resubmits on its own.
    /// </summary>
    private async Task ReconcileInterruptedSubmissionsAsync(DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var cutoff = now - SubmittingReconcileAfter;
        var stuck = await _db.PeppolTransmissions.AsNoTracking()
            .Where(t => t.Status == PeppolTransmissionStatus.Submitting
                        && (t.SubmissionClaimedAt == null || t.SubmissionClaimedAt <= cutoff))
            .OrderBy(t => t.SubmissionClaimedAt)
            .Select(t => t.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var transmissionId in stuck)
        {
            if (await LoadFreshAsync(transmissionId, cancellationToken) is not { Status: PeppolTransmissionStatus.Submitting } transmission)
            {
                continue;
            }

            if (PeppolLivePolicy.IsBlocked(_providerFactory, transmission.Environment, transmission.ProviderKey))
            {
                await FailLiveWithoutProviderAsync(transmission, now, cancellationToken);
                continue;
            }

            var key = transmission.IdempotencyKey ?? BuildIdempotencyKey(transmission);
            PeppolSubmissionLookupResult lookup;
            try
            {
                lookup = await _providerFactory.Resolve(transmission.ProviderKey).FindSubmissionAsync(key, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger?.LogWarning(exception,
                    "Peppol submission lookup failed for interrupted transmission {TransmissionId}; it stays Submitting.",
                    transmission.Id);
                continue;
            }

            PendingNotification? notification = null;
            if (lookup.Found)
            {
                transmission.Status = PeppolTransmissionStatus.SubmittedToProvider;
                transmission.ProviderMessageId = lookup.ProviderMessageId;
                transmission.NextPollAt = now;
                AddEvent(transmission, PeppolTransmissionStatus.SubmittedToProvider, now,
                    "Hersteld na onderbreking: de provider bevestigt ontvangst van deze zending (niet opnieuw verzonden).");
                _logger?.LogWarning("Interrupted Peppol transmission {TransmissionId} reconciled: provider has it.", transmission.Id);
            }
            else
            {
                notification = HandleSubmitFailure(transmission,
                    "Onderbroken verzending; de provider kent haar niet en ze wordt opnieuw aangeboden met dezelfde sleutel.", now);
                if (transmission.Status == PeppolTransmissionStatus.Queued)
                {
                    transmission.NextAttemptAt = now; // the provider confirmed it never arrived
                }

                _logger?.LogWarning("Interrupted Peppol transmission {TransmissionId} reconciled: provider does not have it.",
                    transmission.Id);
            }

            await SaveAndNotifyAsync(transmission, notification, cancellationToken);
        }
    }

    private async Task FailLiveWithoutProviderAsync(PeppolTransmission transmission, DateTime now, CancellationToken cancellationToken)
    {
        _logger?.LogCritical(
            "Peppol transmission {TransmissionId} (tenant {TenantId}) is set to Live but provider '{ProviderKey}' cannot "
            + "transmit to the live Peppol network. Nothing was sent; the transmission is failed.",
            transmission.Id, transmission.TenantId, transmission.ProviderKey);
        transmission.Status = PeppolTransmissionStatus.Failed;
        transmission.ErrorDetail = PeppolLivePolicy.NotConfiguredMessage;
        AddEvent(transmission, PeppolTransmissionStatus.Failed, now, PeppolLivePolicy.NotConfiguredMessage);
        await SaveAndNotifyAsync(transmission,
            new PendingNotification(MessageKinds.InvoicePeppolFailed, PeppolLivePolicy.NotConfiguredLabel), cancellationToken);
    }

    /// <summary>Saves the transition; the notification goes out only when OUR transition won.</summary>
    private async Task SaveAndNotifyAsync(
        PeppolTransmission transmission, PendingNotification? notification, CancellationToken cancellationToken)
    {
        if (await SaveGuardedAsync(transmission, cancellationToken) && notification is { } pending)
        {
            await NotifyAsync(transmission, pending.EventKey, pending.Reason, CancellationToken.None);
        }
    }

    /// <summary>
    /// Saves a transmission transition; when a concurrent actor (cancel, webhook, another
    /// instance) changed the status first, OUR transition is discarded — the other side's
    /// decision wins and the next run re-evaluates from fresh state. False when discarded.
    /// </summary>
    private async Task<bool> SaveGuardedAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger?.LogWarning("Concurrent status change on Peppol transmission {TransmissionId}; local transition discarded.",
                transmission.Id);
            var entry = _db.Entry(transmission);
            await entry.ReloadAsync(CancellationToken.None);
            // Drop any event rows staged for the discarded transition.
            foreach (var staged in _db.ChangeTracker.Entries<PeppolTransmissionEvent>()
                         .Where(e => e.State == EntityState.Added && e.Entity.TransmissionId == transmission.Id)
                         .ToList())
            {
                staged.State = EntityState.Detached;
            }

            return false;
        }
    }

    private sealed record PendingNotification(string EventKey, string? Reason);

    /// <summary>Applies a failed attempt in memory; returns the notification to publish after the save.</summary>
    private PendingNotification? HandleSubmitFailure(PeppolTransmission transmission, string reason, DateTime now)
    {
        transmission.RetryCount++;
        transmission.ErrorDetail = reason;
        if (transmission.RetryCount >= MaxAttempts)
        {
            transmission.Status = PeppolTransmissionStatus.Failed;
            AddEvent(transmission, PeppolTransmissionStatus.Failed, now,
                $"Definitief mislukt na {transmission.RetryCount} pogingen: {reason}");
            return new PendingNotification(MessageKinds.InvoicePeppolFailed, reason);
        }

        // Back to Queued; the backoff moment gates the next attempt (same idempotency key).
        transmission.Status = PeppolTransmissionStatus.Queued;
        transmission.NextAttemptAt = now + BaseBackoff * Math.Pow(2, transmission.RetryCount - 1);
        AddEvent(transmission, PeppolTransmissionStatus.Queued, now,
            $"Poging {transmission.RetryCount} mislukt: {reason} Volgende poging gepland.");
        return null;
    }

    private async Task PollAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // B4: a Live transmission is never polled through a provider that cannot reach the live
        // network — that is exactly how the sandbox used to fake "Delivered" for Live invoices.
        if (PeppolLivePolicy.IsBlocked(_providerFactory, transmission.Environment, transmission.ProviderKey))
        {
            await FailLiveWithoutProviderAsync(transmission, now, cancellationToken);
            return;
        }

        var age = now - (transmission.SubmissionClaimedAt ?? transmission.CreatedAt);
        transmission.NextPollAt = now + PollDelay(age);

        if (transmission.ProviderMessageId is null)
        {
            await SaveGuardedAsync(transmission, cancellationToken);
            return;
        }

        PendingNotification? notification = null;
        try
        {
            var provider = _providerFactory.Resolve(transmission.ProviderKey);
            var status = await provider.GetTransmissionStatusAsync(transmission.ProviderMessageId, cancellationToken);
            if (Enum.TryParse<PeppolTransmissionStatus>(status.Status, ignoreCase: true, out var mapped)
                && mapped != transmission.Status)
            {
                switch (mapped)
                {
                    case PeppolTransmissionStatus.AcceptedByProvider:
                        transmission.Status = mapped;
                        AddEvent(transmission, mapped, now, Sanitize(status.Detail));
                        break;
                    case PeppolTransmissionStatus.Delivered:
                        transmission.Status = mapped;
                        AddEvent(transmission, mapped, now, Sanitize(status.Detail));
                        notification = new PendingNotification(MessageKinds.InvoicePeppolDelivered, null);
                        break;
                    case PeppolTransmissionStatus.Rejected:
                        transmission.Status = mapped;
                        transmission.ErrorDetail = Sanitize(status.Detail) ?? "Geweigerd door het Peppol-netwerk.";
                        AddEvent(transmission, mapped, now, transmission.ErrorDetail);
                        notification = new PendingNotification(MessageKinds.InvoicePeppolFailed, transmission.ErrorDetail);
                        break;
                    default:
                        break; // never regress or jump to unexpected states from a poll
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Polling is repeated by a later run (NextPollAt); a transient failure changes nothing else.
            _logger?.LogError(exception, "Peppol status poll failed for transmission {TransmissionId}.", transmission.Id);
        }

        await SaveAndNotifyAsync(transmission, notification, cancellationToken);
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
