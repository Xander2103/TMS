using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

public interface ITransportOrderTimelineService
{
    /// <summary>Chronological business timeline of one order, or null when the order does not exist.</summary>
    Task<IReadOnlyList<OrderTimelineEventDto>?> GetTimelineAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>
/// Read model that projects the existing trails — audit log, immutable status history,
/// package chain-of-custody, stop execution history and invoicing — into ONE chronological,
/// user-readable timeline. It introduces no second logging mechanism: every source is data
/// that is already being written today.
/// </summary>
public class TransportOrderTimelineService : ITransportOrderTimelineService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    /// <summary>Audit actions whose richer representation comes from the status history.</summary>
    private static readonly string[] StatusAuditActions = ["StatusChanged", "Cancelled", "StatusCorrected"];

    public TransportOrderTimelineService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<OrderTimelineEventDto>?> GetTimelineAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var orderExists = await _dbContext.TransportOrders
            .AnyAsync(o => o.Id == orderId && o.TenantId == tenantId, cancellationToken);
        if (!orderExists)
        {
            return null;
        }

        var events = new List<(DateTime Timestamp, string Category, string Title, string? Detail, Guid? UserId)>();

        // 1. Generic audit entries (created/updated/stop planning …); status flows come from #2.
        var orderKey = orderId.ToString();
        var auditRows = await _dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EntityType == "TransportOrder" && a.EntityId == orderKey
                && !StatusAuditActions.Contains(a.Action))
            .Select(a => new { a.Action, a.Timestamp, a.UserId })
            .ToListAsync(cancellationToken);
        foreach (var row in auditRows)
        {
            events.Add((row.Timestamp, "order", AuditTitle(row.Action), null, row.UserId));
        }

        // 2. Immutable status history (incl. reasons and corrections).
        var statusRows = await _dbContext.TransportOrderStatusHistories.AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.TransportOrderId == orderId)
            .ToListAsync(cancellationToken);
        foreach (var row in statusRows)
        {
            var title = row.IsCorrection
                ? $"Status gecorrigeerd: {StatusLabel(row.FromStatus)} → {StatusLabel(row.ToStatus)}"
                : $"Status: {StatusLabel(row.FromStatus)} → {StatusLabel(row.ToStatus)}";
            events.Add((row.ChangedAt, "status", title, row.Reason, row.ChangedByUserId));
        }

        // 3. Package chain of custody (generation, scans, custody moves, incidents).
        var packageRows = await _dbContext.PackageEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.TransportOrderId == orderId)
            .Join(_dbContext.Packages.AsNoTracking(), e => e.PackageId, p => p.Id,
                (e, p) => new { e.EventType, e.CreatedAt, e.UserId, e.Result, p.PackageNumber })
            .ToListAsync(cancellationToken);
        foreach (var row in packageRows)
        {
            events.Add((row.CreatedAt, "package", $"Collo {row.PackageNumber}: {PackageEventTitle(row.EventType)}", row.Result, row.UserId));
        }

        // 4. Stop execution trail (arrival, completion, skips) of this order's stops.
        var stopIds = await _dbContext.Set<Entities.TransportOrderStop>().AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.TransportOrderId == orderId)
            .Select(s => new { s.Id, s.Sequence })
            .ToListAsync(cancellationToken);
        var stopIdList = stopIds.Select(s => s.Id).ToList();
        if (stopIdList.Count > 0)
        {
            var stopHistory = await _dbContext.StopStatusHistories.AsNoTracking()
                .Join(_dbContext.StopExecutions.AsNoTracking(), h => h.StopExecutionId, e => e.Id,
                    (h, e) => new { h.TenantId, h.OccurredAt, h.FromStatus, h.ToStatus, h.Reason, h.UserId, e.TransportOrderStopId })
                .Where(x => x.TenantId == tenantId && stopIdList.Contains(x.TransportOrderStopId))
                .ToListAsync(cancellationToken);
            var sequenceByStop = stopIds.ToDictionary(s => s.Id, s => s.Sequence);
            foreach (var row in stopHistory)
            {
                var sequence = sequenceByStop.TryGetValue(row.TransportOrderStopId, out var seq) ? seq : 0;
                events.Add((row.OccurredAt, "stop", $"Stop {sequence}: {StopStatusLabel(row.FromStatus)} → {StopStatusLabel(row.ToStatus)}", row.Reason, row.UserId));
            }
        }

        // 5. Invoicing.
        var invoiceRows = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId == orderId)
            .Join(_dbContext.Invoices.AsNoTracking(), l => l.InvoiceId, i => i.Id,
                (l, i) => new { i.InvoiceNumber, i.CreatedAt, i.CreatedByUserId })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var row in invoiceRows)
        {
            events.Add((row.CreatedAt, "invoice", $"Factuur {row.InvoiceNumber} aangemaakt", null, row.CreatedByUserId));
        }

        // Resolve acting users tenant-safely.
        var userIds = events.Where(e => e.UserId is not null).Select(e => e.UserId!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? []
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        return events
            .OrderBy(e => e.Timestamp)
            .Select(e => new OrderTimelineEventDto(
                e.Timestamp, e.Category, e.Title, e.Detail,
                e.UserId is { } userId && userNames.TryGetValue(userId, out var name) && name.Length > 0 ? name : null))
            .ToList();
    }

    /// <summary>Package chain-of-custody events in the planner's words, never the enum name (B32).</summary>
    private static string PackageEventTitle(Packages.Entities.PackageEventType eventType) => eventType switch
    {
        Packages.Entities.PackageEventType.Created => "aangemaakt",
        Packages.Entities.PackageEventType.Labelled => "geëtiketteerd",
        Packages.Entities.PackageEventType.LabelReprinted => "etiket opnieuw afgedrukt",
        Packages.Entities.PackageEventType.Relabelled => "opnieuw geëtiketteerd",
        Packages.Entities.PackageEventType.StatusChanged => "status gewijzigd",
        Packages.Entities.PackageEventType.LoadScan => "geladen (scan)",
        Packages.Entities.PackageEventType.LoadMissing => "ontbrak bij het laden",
        Packages.Entities.PackageEventType.WrongPackageScan => "verkeerd collo gescand",
        Packages.Entities.PackageEventType.UnloadScan => "gelost (scan)",
        Packages.Entities.PackageEventType.Delivered => "geleverd",
        Packages.Entities.PackageEventType.Refused => "geweigerd",
        Packages.Entities.PackageEventType.PartialDelivery => "gedeeltelijk geleverd",
        Packages.Entities.PackageEventType.DamageReported => "schade gemeld",
        Packages.Entities.PackageEventType.MarkedMissing => "als vermist gemarkeerd",
        Packages.Entities.PackageEventType.ExceptionResolved => "afwijking afgehandeld",
        Packages.Entities.PackageEventType.GroupAssigned => "aan een groep toegevoegd",
        Packages.Entities.PackageEventType.GroupRemoved => "uit de groep gehaald",
        Packages.Entities.PackageEventType.GroupBroken => "groep opgeheven",
        Packages.Entities.PackageEventType.MovedToTrip => "naar een andere rit verplaatst",
        _ => eventType.ToString(),
    };

    // B25: every action written for an order reads as a Dutch sentence; an unknown future action
    // reads neutrally instead of as a raw code.
    private static string AuditTitle(string action) => action switch
    {
        "Created" => "Opdracht aangemaakt",
        "Updated" => "Opdracht bijgewerkt",
        "Deleted" => "Opdracht verwijderd",
        "Cancelled" => "Opdracht geannuleerd",
        "StatusChanged" => "Status gewijzigd",
        "StatusCorrected" => "Status gecorrigeerd",
        "CustomerChanged" => "Klant gewijzigd",
        "LegalEntityChanged" => "Facturerende entiteit gewijzigd",
        "PriorityChanged" => "Prioriteit gewijzigd",
        "DieselSurchargeOverridden" => "Dieseltoeslag handmatig aangepast",
        "StopExecutionPlanUpdated" => "Uitvoeringsplanning van een stop bijgewerkt",
        "StopLinkedToAddressBook" => "Stop gekoppeld aan het adresboek",
        "StopSnapshotRefreshed" => "Stopadres bijgewerkt vanuit het adresboek",
        "DocumentPreferenceChanged" => "Documentvoorkeur gewijzigd",
        "PortalSubmitted" => "Ingediend via het klantportaal",
        "PortalInfoRequested" => "Extra informatie gevraagd aan de klant",
        "FileAttached" => "Bestand toegevoegd",
        "FileRemoved" => "Bestand verwijderd",
        "Issued" => "Document uitgegeven",
        "Moved" => "Verplaatst",
        _ => "Opdracht gewijzigd",
    };

    private static string StopStatusLabel(Planning.Entities.StopExecutionStatus status) => status switch
    {
        Planning.Entities.StopExecutionStatus.Planned => "Gepland",
        Planning.Entities.StopExecutionStatus.EnRoute => "Onderweg",
        Planning.Entities.StopExecutionStatus.Arrived => "Aangekomen",
        Planning.Entities.StopExecutionStatus.Loading => "Laden",
        Planning.Entities.StopExecutionStatus.Loaded => "Geladen",
        Planning.Entities.StopExecutionStatus.Unloading => "Lossen",
        Planning.Entities.StopExecutionStatus.Completed => "Afgerond",
        Planning.Entities.StopExecutionStatus.PartiallyCompleted => "Gedeeltelijk afgerond",
        Planning.Entities.StopExecutionStatus.Failed => "Mislukt",
        Planning.Entities.StopExecutionStatus.Skipped => "Overgeslagen",
        _ => status.ToString(),
    };

    private static string StatusLabel(Entities.TransportOrderStatus status) => TransportOrderStatusLabels.Dutch(status);
}
