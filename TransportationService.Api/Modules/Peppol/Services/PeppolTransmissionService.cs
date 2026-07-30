using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Peppol.Dtos;
using TransportationService.Api.Modules.Peppol.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Peppol.Services;

public interface IPeppolTransmissionService
{
    /// <summary>
    /// Validates, renders and stores the UBL payload and creates a Queued transmission for a
    /// Sent/Paid invoice. The dispatcher — never this method — talks to the provider.
    /// Null when the invoice does not exist in this tenant.
    /// </summary>
    Task<PeppolTransmissionDto?> QueueAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>New transmission (version + 1) reusing the stored payload of a Failed/Rejected one.</summary>
    Task<PeppolTransmissionDto?> RetryAsync(Guid transmissionId, CancellationToken cancellationToken);

    /// <summary>Withdraws a transmission that is still Queued (nothing has left the building).</summary>
    Task<PeppolTransmissionDto?> CancelAsync(Guid transmissionId, CancellationToken cancellationToken);

    /// <summary>All transmissions of one invoice, newest first, with their event timelines.</summary>
    Task<IReadOnlyList<PeppolTransmissionDto>> ListForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);

    /// <summary>Paged Uitgaand overview (invoice number, customer, totals, status).</summary>
    Task<PagedResult<PeppolTransmissionListItemDto>> SearchAsync(
        string? status, string? search, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the OUTBOUND transmission lifecycle rows. The duplicate check + filtered unique index
/// guarantee at most one non-terminal transmission per invoice; payloads are immutable once
/// stored (retry reuses the same storage key and hash — the invoice is frozen after Sent).
/// </summary>
public class PeppolTransmissionService : IPeppolTransmissionService
{
    private const string EntityType = "PeppolTransmission";

    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IAuditService _audit;
    private readonly IPeppolInvoiceService _invoiceDocuments;
    private readonly IFileStorageService _fileStorage;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<PeppolTransmissionService>? _logger;

    public PeppolTransmissionService(
        TransportationDbContext db, ITenantContext tenant, IAuditService audit,
        IPeppolInvoiceService invoiceDocuments, IFileStorageService fileStorage, TimeProvider timeProvider,
        INotificationEventService? notificationEvents = null, ILogger<PeppolTransmissionService>? logger = null)
    {
        _db = db;
        _tenant = tenant;
        _audit = audit;
        _invoiceDocuments = invoiceDocuments;
        _fileStorage = fileStorage;
        _timeProvider = timeProvider;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    private Guid TenantId => _tenant.TenantId;

    public async Task<PeppolTransmissionDto?> QueueAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.TenantId == TenantId && i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.Paid))
        {
            throw new DomainValidationException(
                "Alleen verzonden of betaalde facturen kunnen via Peppol verstuurd worden.");
        }

        // Friendly guard in front of the filtered unique index (which stays the concurrency-safe backstop).
        var activeExists = await _db.PeppolTransmissions.AnyAsync(
            t => t.TenantId == TenantId && t.InvoiceId == invoiceId
                 && t.Status != PeppolTransmissionStatus.Failed
                 && t.Status != PeppolTransmissionStatus.Rejected
                 && t.Status != PeppolTransmissionStatus.Cancelled,
            cancellationToken);
        if (activeExists)
        {
            throw new DomainValidationException(
                "Er is al een actieve Peppol-verzending voor deze factuur. Wacht tot die is afgerond of annuleer haar eerst.");
        }

        var rendered = await _invoiceDocuments.GetXmlAsync(invoiceId, cancellationToken)
            ?? throw new DomainValidationException("De factuur kon niet worden geladen.");
        if (rendered.Xml is null)
        {
            var issues = rendered.Failure?.Issues.Select(i => i.Message) ?? [];
            throw new DomainValidationException(
                "De factuur is nog niet klaar voor Peppol: " + string.Join(" ", issues));
        }

        var graph = await LoadParticipantsAsync(invoice, cancellationToken);
        var payloadBytes = Encoding.UTF8.GetBytes(rendered.Xml);
        var hash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
        var version = await _db.PeppolTransmissions
            .Where(t => t.TenantId == TenantId && t.InvoiceId == invoiceId)
            .Select(t => (int?)t.PayloadVersion)
            .MaxAsync(cancellationToken) ?? 0;

        using var payloadStream = new MemoryStream(payloadBytes);
        var storageKey = await _fileStorage.SaveAsync(
            TenantId, "peppol", $"{invoice.InvoiceNumber.Replace('/', '-')}-v{version + 1}.xml",
            payloadStream, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var transmission = new PeppolTransmission
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            InvoiceId = invoiceId,
            DocumentKind = invoice.Kind == InvoiceKind.CreditNote ? PeppolDocumentKind.CreditNote : PeppolDocumentKind.Invoice,
            Status = PeppolTransmissionStatus.Queued,
            Environment = graph.Environment,
            ProviderKey = graph.ProviderKey,
            SellerParticipant = graph.SellerParticipant,
            BuyerParticipant = graph.BuyerParticipant,
            PayloadStorageKey = storageKey,
            PayloadHash = hash,
            PayloadVersion = version + 1,
            NextAttemptAt = now,
        };
        transmission.Events.Add(NewEvent(transmission, PeppolTransmissionStatus.Queued, now, "In wachtrij geplaatst."));
        _db.PeppolTransmissions.Add(transmission);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request slipped past the friendly guard; the partial unique index
            // caught it. Same message, proper 400 instead of a raw 500.
            throw new DomainValidationException(
                "Er is al een actieve Peppol-verzending voor deze factuur. Wacht tot die is afgerond of annuleer haar eerst.");
        }

        await _audit.RecordAsync(EntityType, transmission.Id.ToString(), "Queued", null,
            new { invoice.InvoiceNumber, transmission.PayloadVersion, transmission.PayloadHash, Environment = transmission.Environment.ToString() },
            cancellationToken);
        await PublishAsync(MessageKinds.InvoicePeppolQueued, invoice, transmission,
            $"Factuur {invoice.InvoiceNumber} staat in de Peppol-wachtrij.", null, cancellationToken);

        return await MapAsync(transmission, cancellationToken);
    }

    public async Task<PeppolTransmissionDto?> RetryAsync(Guid transmissionId, CancellationToken cancellationToken)
    {
        var failed = await _db.PeppolTransmissions
            .FirstOrDefaultAsync(t => t.TenantId == TenantId && t.Id == transmissionId, cancellationToken);
        if (failed is null)
        {
            return null;
        }

        if (failed.Status is not (PeppolTransmissionStatus.Failed or PeppolTransmissionStatus.Rejected))
        {
            throw new DomainValidationException(
                "Alleen mislukte of geweigerde verzendingen kunnen opnieuw geprobeerd worden.");
        }

        // The invoice may have been cancelled since the original attempt.
        var invoiceStatus = await _db.Invoices.AsNoTracking()
            .Where(i => i.TenantId == TenantId && i.Id == failed.InvoiceId)
            .Select(i => (InvoiceStatus?)i.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (invoiceStatus is not (InvoiceStatus.Sent or InvoiceStatus.Paid))
        {
            throw new DomainValidationException(
                "De factuur is niet langer verzonden of betaald; opnieuw versturen is niet mogelijk.");
        }

        // A newer transmission may already be running for this invoice.
        var activeExists = await _db.PeppolTransmissions.AnyAsync(
            t => t.TenantId == TenantId && t.InvoiceId == failed.InvoiceId
                 && t.Status != PeppolTransmissionStatus.Failed
                 && t.Status != PeppolTransmissionStatus.Rejected
                 && t.Status != PeppolTransmissionStatus.Cancelled,
            cancellationToken);
        if (activeExists)
        {
            throw new DomainValidationException(
                "Er is al een actieve Peppol-verzending voor deze factuur.");
        }

        var nextVersion = await _db.PeppolTransmissions
            .Where(t => t.TenantId == TenantId && t.InvoiceId == failed.InvoiceId)
            .MaxAsync(t => t.PayloadVersion, cancellationToken) + 1;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // The invoice is frozen once Sent, so the stored payload stays valid: same storage key
        // and hash, next version number, fresh correlation id.
        var retry = new PeppolTransmission
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            InvoiceId = failed.InvoiceId,
            DocumentKind = failed.DocumentKind,
            Status = PeppolTransmissionStatus.Queued,
            Environment = failed.Environment,
            ProviderKey = failed.ProviderKey,
            SellerParticipant = failed.SellerParticipant,
            BuyerParticipant = failed.BuyerParticipant,
            PayloadStorageKey = failed.PayloadStorageKey,
            PayloadHash = failed.PayloadHash,
            PayloadVersion = nextVersion,
            NextAttemptAt = now,
        };
        retry.Events.Add(NewEvent(retry, PeppolTransmissionStatus.Queued,
            now, $"Nieuwe poging na {(failed.Status == PeppolTransmissionStatus.Failed ? "mislukking" : "weigering")}."));
        _db.PeppolTransmissions.Add(retry);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(EntityType, retry.Id.ToString(), "Retried",
            new { PreviousTransmissionId = failed.Id, PreviousStatus = failed.Status.ToString() },
            new { retry.PayloadVersion }, cancellationToken);

        return await MapAsync(retry, cancellationToken);
    }

    public async Task<PeppolTransmissionDto?> CancelAsync(Guid transmissionId, CancellationToken cancellationToken)
    {
        var transmission = await _db.PeppolTransmissions.Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.TenantId == TenantId && t.Id == transmissionId, cancellationToken);
        if (transmission is null)
        {
            return null;
        }

        if (transmission.Status != PeppolTransmissionStatus.Queued)
        {
            throw new DomainValidationException(
                "Alleen verzendingen die nog in de wachtrij staan kunnen geannuleerd worden.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        transmission.Status = PeppolTransmissionStatus.Cancelled;
        // Client-generated id on a tracked parent: mark Added explicitly (navigation discovery
        // would attach the new event as Modified).
        _db.PeppolTransmissionEvents.Add(NewEvent(transmission, PeppolTransmissionStatus.Cancelled, now, "Geannuleerd vóór verzending."));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The dispatcher submitted this row while we were cancelling; its transition wins.
            throw new DomainValidationException(
                "De verzending is intussen al aangeboden aan de provider en kan niet meer geannuleerd worden.");
        }

        await _audit.RecordAsync(EntityType, transmission.Id.ToString(), "Cancelled", null, null, cancellationToken);
        return await MapAsync(transmission, cancellationToken);
    }

    public async Task<IReadOnlyList<PeppolTransmissionDto>> ListForInvoiceAsync(
        Guid invoiceId, CancellationToken cancellationToken)
    {
        var transmissions = await _db.PeppolTransmissions.AsNoTracking()
            .Include(t => t.Events)
            .Where(t => t.TenantId == TenantId && t.InvoiceId == invoiceId)
            .OrderByDescending(t => t.PayloadVersion)
            .ToListAsync(cancellationToken);
        var result = new List<PeppolTransmissionDto>(transmissions.Count);
        foreach (var transmission in transmissions)
        {
            result.Add(await MapAsync(transmission, cancellationToken));
        }

        return result;
    }

    public async Task<PagedResult<PeppolTransmissionListItemDto>> SearchAsync(
        string? status, string? search, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.PeppolTransmissions.AsNoTracking().Where(t => t.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<PeppolTransmissionStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(t => t.Status == parsed);
        }

        var joined = from t in query
                     join i in _db.Invoices.AsNoTracking().Where(i => i.TenantId == TenantId)
                         on t.InvoiceId equals i.Id
                     join c in _db.Customers.AsNoTracking().Where(c => c.TenantId == TenantId)
                         on i.CustomerId equals c.Id
                     select new { t, i, CustomerName = c.Name };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            joined = joined.Where(x => x.i.InvoiceNumber.ToLower().Contains(term) || x.CustomerName.ToLower().Contains(term));
        }

        var total = await joined.CountAsync(cancellationToken);
        var rows = await joined
            .OrderByDescending(x => x.t.CreatedAt)
            .Skip(page.Skip).Take(page.PageSize)
            .ToListAsync(cancellationToken);

        var invoiceIds = rows.Select(r => r.i.Id).Distinct().ToList();
        var totals = await _db.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == TenantId && invoiceIds.Contains(l.InvoiceId))
            .GroupBy(l => l.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(l => l.Quantity * l.UnitPrice * (1 + l.VatRatePercent / 100m)) })
            .ToDictionaryAsync(x => x.InvoiceId, x => Math.Round(x.Total, 2), cancellationToken);

        var items = rows.Select(r => new PeppolTransmissionListItemDto(
            r.t.Id, r.i.Id, r.i.InvoiceNumber, r.t.DocumentKind.ToString(), r.CustomerName,
            totals.GetValueOrDefault(r.i.Id), r.i.Currency, r.i.InvoiceDate,
            r.t.Status.ToString(), r.t.Environment.ToString(), r.t.ProviderMessageId,
            r.t.ErrorDetail, r.t.RetryCount, r.t.PayloadVersion, r.t.CreatedAt)).ToList();
        return new PagedResult<PeppolTransmissionListItemDto>(items, total, page.Page, page.PageSize);
    }

    private async Task<PeppolTransmissionDto> MapAsync(PeppolTransmission transmission, CancellationToken cancellationToken)
    {
        var events = transmission.Events.Count > 0
            ? transmission.Events
            : await _db.PeppolTransmissionEvents.AsNoTracking()
                .Where(e => e.TenantId == TenantId && e.TransmissionId == transmission.Id)
                .ToListAsync(cancellationToken);
        return new PeppolTransmissionDto(
            transmission.Id, transmission.InvoiceId, transmission.DocumentKind.ToString(),
            transmission.Status.ToString(), transmission.Environment.ToString(), transmission.ProviderKey,
            transmission.ProviderMessageId, transmission.PayloadVersion, transmission.PayloadHash,
            transmission.ErrorDetail, transmission.RetryCount, transmission.CreatedAt,
            events.OrderBy(e => e.Timestamp).ThenBy(e => e.CreatedAt)
                .Select(e => new PeppolTransmissionEventDto(e.Status.ToString(), e.Timestamp, e.Detail))
                .ToList());
    }

    private sealed record ParticipantGraph(
        string SellerParticipant, string BuyerParticipant, PeppolEnvironment Environment, string ProviderKey);

    private async Task<ParticipantGraph> LoadParticipantsAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var seller = invoice.LegalEntityId is { } entityId
            ? await _db.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == TenantId && e.Id == entityId)
                .Select(e => new { e.PeppolScheme, e.PeppolId })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var buyer = await _db.Customers.AsNoTracking()
            .Where(c => c.TenantId == TenantId && c.Id == invoice.CustomerId)
            .Select(c => new { c.PeppolScheme, c.PeppolId })
            .FirstOrDefaultAsync(cancellationToken);
        var settings = invoice.LegalEntityId is { } settingsEntityId
            ? await _db.PeppolSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == TenantId && s.LegalEntityId == settingsEntityId, cancellationToken)
            : null;

        // GetXmlAsync validation guarantees these are present; defend anyway.
        if (seller?.PeppolId is null || buyer?.PeppolId is null)
        {
            throw new DomainValidationException("De Peppol-identiteit van verzender of ontvanger ontbreekt.");
        }

        return new ParticipantGraph(
            $"{seller.PeppolScheme}:{seller.PeppolId}",
            $"{buyer.PeppolScheme}:{buyer.PeppolId}",
            settings?.Environment ?? PeppolEnvironment.Sandbox,
            settings?.ProviderKey ?? SandboxPeppolProvider.ProviderKey);
    }

    private PeppolTransmissionEvent NewEvent(
        PeppolTransmission transmission, PeppolTransmissionStatus status, DateTime timestamp, string? detail) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        TransmissionId = transmission.Id,
        Status = status,
        Timestamp = timestamp,
        Detail = detail,
    };

    private async Task PublishAsync(
        string eventKey, Invoice invoice, PeppolTransmission transmission,
        string message, string? reason, CancellationToken cancellationToken)
    {
        if (_notificationEvents is null)
        {
            return;
        }

        try
        {
            var customerName = await _db.Customers.AsNoTracking()
                .Where(c => c.TenantId == TenantId && c.Id == invoice.CustomerId)
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

            await _notificationEvents.PublishAsync(eventKey,
                new NotificationEventContext("Invoice", invoice.Id.ToString(), tokens)
                {
                    CustomerId = invoice.CustomerId,
                    LinkPath = $"/invoices/{invoice.Id}",
                    InAppMessage = message,
                }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Peppol notification '{EventKey}' failed; transmission already persisted.", eventKey);
        }
    }
}
