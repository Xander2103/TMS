using TransportationService.Api.Common.Money;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public class InvoiceService : IInvoiceService
{
    private const string EntityType = "Invoice";

    /// <summary>
    /// H-06: a finalized document is never cancelled. Once Sent, the only way forward is Paid —
    /// cancelling would hand the orders on it back to Completed and re-open their prices, so a
    /// document the customer already has could silently be invoiced a second time. A mistake on
    /// a sent invoice is corrected with a credit note, which leaves the original untouched.
    /// </summary>
    private static readonly IReadOnlyDictionary<InvoiceStatus, InvoiceStatus[]> Transitions =
        new Dictionary<InvoiceStatus, InvoiceStatus[]>
        {
            [InvoiceStatus.Draft] = [InvoiceStatus.Sent, InvoiceStatus.Cancelled],
            [InvoiceStatus.Sent] = [InvoiceStatus.Paid],
            [InvoiceStatus.Paid] = [],
            [InvoiceStatus.Cancelled] = [],
        };

    /// <summary>
    /// Hint shown wherever a finalized document may not be touched any more; identical in intent
    /// to the wording the order side uses (TransportOrderService / OrderCustomerChangeService).
    /// </summary>
    private const string CreditNoteHint =
        "Corrigeer via een creditnota; de historische factuur blijft ongewijzigd.";

    /// <summary>
    /// Peppol statuses that prove the document left the building: the provider has seen it.
    /// Draft/Validated/Queued are purely local, Cancelled is a local withdrawal before submission.
    /// </summary>
    private static readonly Modules.Peppol.Entities.PeppolTransmissionStatus[] TransmittedStatuses =
    [
        Modules.Peppol.Entities.PeppolTransmissionStatus.SubmittedToProvider,
        Modules.Peppol.Entities.PeppolTransmissionStatus.AcceptedByProvider,
        Modules.Peppol.Entities.PeppolTransmissionStatus.Delivered,
        Modules.Peppol.Entities.PeppolTransmissionStatus.Failed,
        Modules.Peppol.Entities.PeppolTransmissionStatus.Rejected,
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IInvoiceNumberService _numberService;
    private readonly Partners.Services.ICustomerBillingConfigService _billingConfig;
    private readonly Accounting.Services.IAccountingService _accounting;
    private readonly INotificationEventService? _notificationEvents;
    private readonly ILogger<InvoiceService>? _logger;
    private readonly Identity.Services.ICurrentUserContext? _currentUser;

    public InvoiceService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IInvoiceNumberService numberService,
        Partners.Services.ICustomerBillingConfigService billingConfig,
        Accounting.Services.IAccountingService accounting,
        INotificationEventService? notificationEvents = null,
        ILogger<InvoiceService>? logger = null,
        Identity.Services.ICurrentUserContext? currentUser = null)
    {
        _currentUser = currentUser;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _numberService = numberService;
        _billingConfig = billingConfig;
        _accounting = accounting;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    /// <summary>Fire-and-forget event publication: a notification failure never breaks the
    /// already-committed business operation.</summary>
    private async Task PublishEventAsync(string eventKey, NotificationEventContext context, CancellationToken cancellationToken)
    {
        if (_notificationEvents is null)
        {
            return;
        }

        try
        {
            await _notificationEvents.PublishAsync(eventKey, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.", eventKey);
        }
    }

    private IQueryable<Invoice> TenantScoped() =>
        _dbContext.Invoices.Where(i => i.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<InvoiceListItemDto>> SearchAsync(
        string? search, InvoiceStatus? status, Guid? customerId, PageRequest page, CancellationToken cancellationToken, string? sort = null, string? dir = null,
        DateOnly? dateFrom = null, DateOnly? dateTo = null, string? paid = null, string? peppolStatus = null)
    {
        var tenantId = _tenantContext.TenantId;
        var query = TenantScoped().AsNoTracking();

        if (status is { } s) query = query.Where(i => i.Status == s);
        if (customerId is { } c) query = query.Where(i => i.CustomerId == c);
        if (dateFrom is { } from) query = query.Where(i => i.InvoiceDate >= from);
        if (dateTo is { } to) query = query.Where(i => i.InvoiceDate <= to);
        // ACC-M10: "betaald / openstaand" — an issued invoice is open until it is Paid.
        switch (paid?.Trim().ToLowerInvariant())
        {
            case "paid": query = query.Where(i => i.Status == InvoiceStatus.Paid); break;
            case "unpaid": query = query.Where(i => i.Status == InvoiceStatus.Sent); break;
        }

        if (!string.IsNullOrWhiteSpace(peppolStatus)
            && Enum.TryParse<Modules.Peppol.Entities.PeppolTransmissionStatus>(peppolStatus, true, out var wantedPeppol))
        {
            // The newest transmission (highest payload version, then most recent) decides.
            query = query.Where(i => _dbContext.PeppolTransmissions
                .Where(t => t.TenantId == tenantId && t.InvoiceId == i.Id)
                .OrderByDescending(t => t.PayloadVersion).ThenByDescending(t => t.CreatedAt)
                .Select(t => (Modules.Peppol.Entities.PeppolTransmissionStatus?)t.Status)
                .FirstOrDefault() == wantedPeppol);
        }

        // MD-H3/MD-B2: an issued invoice is listed under the customer name it was issued to, and a
        // (legacy) deleted customer never makes its invoices disappear from the list.
        var joined = from i in query
                     join cu in _dbContext.Customers.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId)
                         on i.CustomerId equals cu.Id
                     select new { i, CustomerName = i.PartySnapshotFrozenAt != null && i.CustomerNameSnapshot != null ? i.CustomerNameSnapshot : cu.Name, cu.CustomerNumber };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            // Number, customer (name/number), PO, and the invoiced work itself: order number,
            // customer reference and the dossier the order or activity belongs to (ACC-M10).
            var orders = _dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == tenantId);
            var dossiers = _dbContext.TransportDossiers.AsNoTracking().Where(d => d.TenantId == tenantId);
            var dossierOrders = _dbContext.DossierOrders.AsNoTracking().Where(d => d.TenantId == tenantId);
            var activities = _dbContext.DossierActivities.AsNoTracking().Where(a => a.TenantId == tenantId);
            var lines = _dbContext.InvoiceLines.AsNoTracking().Where(l => l.TenantId == tenantId);
            joined = joined.Where(x =>
                (x.i.InvoiceNumber ?? string.Empty).ToLower().Contains(term)
                || x.CustomerName.ToLower().Contains(term)
                || x.CustomerNumber.ToLower().Contains(term)
                || (x.i.PurchaseOrderNumber != null && x.i.PurchaseOrderNumber.ToLower().Contains(term))
                || lines.Any(l => l.InvoiceId == x.i.Id && l.TransportOrderId != null && orders.Any(o =>
                    o.Id == l.TransportOrderId
                    && (o.OrderNumber.ToLower().Contains(term)
                        || (o.CustomerReference != null && o.CustomerReference.ToLower().Contains(term))
                        || dossierOrders.Any(d => d.TransportOrderId == o.Id && dossiers.Any(td => td.Id == d.DossierId && td.DossierNumber.ToLower().Contains(term))))))
                || lines.Any(l => l.InvoiceId == x.i.Id && l.DossierActivityId != null && activities.Any(a =>
                    a.Id == l.DossierActivityId && dossiers.Any(td => td.Id == a.DossierId && td.DossierNumber.ToLower().Contains(term)))));
        }

        var totalCount = await joined.CountAsync(cancellationToken);

        var listSort = ListSort.Parse(sort, dir, "date", ["number", "date", "due", "customer", "status"], ["date", "due"]);
        var ordered = listSort.Key switch
        {
            "number" => joined.OrderBy(x => x.i.InvoiceNumber, listSort.Descending),
            "due" => joined.OrderBy(x => x.i.DueDate, listSort.Descending).ThenBy(x => x.i.InvoiceNumber, true),
            "customer" => joined.OrderBy(x => x.CustomerName, listSort.Descending).ThenBy(x => x.i.InvoiceNumber, true),
            "status" => joined.OrderBy(x => x.i.Status == InvoiceStatus.Draft ? 0
                : x.i.Status == InvoiceStatus.Sent ? 1
                : x.i.Status == InvoiceStatus.Paid ? 2 : 3, listSort.Descending).ThenBy(x => x.i.InvoiceNumber, true),
            _ => joined.OrderBy(x => x.i.InvoiceDate, listSort.Descending).ThenBy(x => x.i.InvoiceNumber, listSort.Descending),
        };
        var pageRows = await ordered
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new { x.i.Id, x.i.InvoiceNumber, x.i.InvoiceDate, x.i.DueDate, x.i.CustomerId, x.CustomerName, x.CustomerNumber, x.i.Status, x.i.Currency, x.i.Kind, x.i.PaidAt })
            .ToListAsync(cancellationToken);

        var invoiceIds = pageRows.Select(r => r.Id).ToList();
        var lineRows = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && invoiceIds.Contains(l.InvoiceId))
            .Select(l => new { l.InvoiceId, l.Quantity, l.UnitPrice, l.VatRatePercent })
            .ToListAsync(cancellationToken);
        var linesByInvoice = lineRows.ToLookup(l => l.InvoiceId);
        var peppolByInvoice = invoiceIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _dbContext.PeppolTransmissions.AsNoTracking()
                .Where(t => t.TenantId == tenantId && invoiceIds.Contains(t.InvoiceId))
                .Select(t => new { t.InvoiceId, t.Status, t.PayloadVersion, t.CreatedAt })
                .ToListAsync(cancellationToken))
                .GroupBy(t => t.InvoiceId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.PayloadVersion).ThenByDescending(t => t.CreatedAt).First().Status.ToString());

        var items = pageRows.Select(r =>
        {
            var invoiceLines = linesByInvoice[r.Id].ToList();
            var subtotal = Math.Round(invoiceLines.Sum(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice)), 2);
            // Same per-rate group rounding as InvoiceTotals/the UBL document.
            var vat = invoiceLines
                .GroupBy(l => l.VatRatePercent)
                .Sum(g => Math.Round(
                    Math.Round(g.Sum(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice)), 2) * g.Key / 100m, 2, MidpointRounding.AwayFromZero));
            return new InvoiceListItemDto(
                r.Id, r.InvoiceNumber, r.InvoiceDate, r.DueDate, r.CustomerId, r.CustomerName,
                r.Status, r.Currency, subtotal, vat, subtotal + vat, invoiceLines.Count, r.Kind,
                r.CustomerNumber, peppolByInvoice.GetValueOrDefault(r.Id), r.PaidAt,
                DisplayNumberOf(r.InvoiceNumber, r.Id));
        }).ToList();

        return new PagedResult<InvoiceListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

    /// <summary>The legal number once issued; a draft shows a reference derived from its id (ACC-H3).</summary>
    private static string DisplayNumberOf(string? invoiceNumber, Guid id) =>
        string.IsNullOrEmpty(invoiceNumber) ? "CONCEPT-" + id.ToString("N")[..8].ToUpperInvariant() : invoiceNumber;

    public async Task<InvoiceDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().AsNoTracking()
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return invoice is null ? null : await MapDetailAsync(invoice, cancellationToken);
    }

    public async Task<IReadOnlyList<UninvoicedOrderDto>> ListUninvoicedOrdersAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var invoicedOrderIds = _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.TransportOrderId != null)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled),
                l => l.InvoiceId, i => i.Id, (l, i) => l.TransportOrderId!.Value);

        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.CustomerId == customerId
                        && o.Status == TransportOrderStatus.Completed
                        && !invoicedOrderIds.Contains(o.Id))
            .OrderBy(o => o.OrderDate)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.OrderDate, o.GoodsDescription, o.AgreedPrice, o.LegalEntityId,
                o.InvoiceReadiness, o.InvoiceReadinessReasons,
                o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount,
                IsFree = o.FreeConfirmed || (o.PricingSource == OrderPricingSource.OneOff && o.OneOffFixedAmount == 0m) || (o.PriceIsManual && o.AgreedPrice == 0m),
            })
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var stops = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderStops.AsNoTracking()
                .Where(st => st.TenantId == tenantId && orderIds.Contains(st.TransportOrderId))
                .GroupJoin(_dbContext.Locations.AsNoTracking().Where(l => l.TenantId == tenantId),
                    st => st.LocationId, l => l.Id,
                    (st, locations) => new { Stop = st, Locations = locations })
                .SelectMany(x => x.Locations.DefaultIfEmpty(), (x, l) => new
                {
                    x.Stop.TransportOrderId, x.Stop.Sequence, x.Stop.StopType,
                    City = x.Stop.City ?? (l != null ? l.City : null),
                })
                .ToListAsync(cancellationToken);
        var stopsByOrder = stops.ToLookup(s => s.TransportOrderId);

        return orders.Select(o =>
        {
            var orderStops = stopsByOrder[o.Id].OrderBy(s => s.Sequence).ToList();
            return new UninvoicedOrderDto(
                o.Id, o.OrderNumber, o.OrderDate, o.GoodsDescription ?? string.Empty,
                orderStops.FirstOrDefault(s => s.StopType == StopType.Loading)?.City,
                // D2: on-site work has no unloading stop — its site is the destination shown.
                orderStops.LastOrDefault(s => s.StopType is StopType.Unloading or StopType.Site)?.City,
                o.AgreedPrice,
                o.LegalEntityId,
                o.InvoiceReadiness, o.InvoiceReadinessReasons,
                o.IsFree,
                // ACC-B1: only a resolved order (ready, or explicitly free) may be billed. A completed
                // order whose readiness projection was never evaluated (still "NotReady" — the
                // evaluator always yields Ready/ReviewRequired for a completed order) is judged by
                // its pricing provenance instead, so it is never silently billed at € 0,00 either.
                Modules.Orders.Services.InvoiceReadinessEvaluator.IsBillable(
                    o.InvoiceReadiness, o.InvoiceReadinessReasons, o.IsFree,
                    Modules.Orders.Services.OrderPricingState.IsPriced(o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount, o.AgreedPrice)));
        }).ToList();
    }

    /// <summary>
    /// Closure sprint 2026-09-23: standalone activities join the invoice builder next to orders.
    /// A candidate is priced by provenance (<see cref="ActivityPricingState"/>: never "amount &gt; 0",
    /// so an unpriced activity can never become a € 0 line), has NO linked order (an order-backed
    /// activity is billed through its order — never twice), belongs to a dossier of this customer
    /// and is not on a live (non-cancelled) invoice. A draft-released price (Locked) is a
    /// candidate again, exactly like a released order. T-H8: never a cancelled activity, and a
    /// planning-relevant one only once Completed (<c>InvoiceControlService</c> uses the same rule).
    /// </summary>
    public async Task<IReadOnlyList<UninvoicedActivityDto>> ListUninvoicedActivitiesAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var invoicedActivityIds = _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DossierActivityId != null)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.Status != InvoiceStatus.Cancelled),
                l => l.InvoiceId, i => i.Id, (l, i) => l.DossierActivityId!.Value);

        // € 0 is only invoiceable when it was CONFIRMED free; an unconfirmed zero still carries
        // the pricing.zero warning and stays out until someone decides.
        var rows = await _dbContext.DossierActivityPricings.AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Status != Modules.Orders.Entities.OrderPricingStatus.Invoiced)
            .Where(ActivityPricingState.IsPricedExpression)
            .Where(p => p.AgreedPrice != 0m || p.FreeConfirmed)
            .Join(_dbContext.DossierActivities.AsNoTracking()
                    .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == null),
                p => p.DossierActivityId, a => a.Id, (p, a) => new { Pricing = p, Activity = a })
            .Join(_dbContext.TransportDossiers.AsNoTracking()
                    .Where(d => d.TenantId == tenantId && d.CustomerId == customerId),
                x => x.Activity.DossierId, d => d.Id, (x, d) => new { x.Pricing, x.Activity, Dossier = d })
            .Join(_dbContext.ActivityTypes.AsNoTracking().Where(t => t.TenantId == tenantId && t.IsBillable),
                x => x.Activity.ActivityTypeId, t => t.Id, (x, t) => new { x.Pricing, x.Activity, x.Dossier, Type = t })
            .Where(x => !invoicedActivityIds.Contains(x.Activity.Id))
            // T-H8: a cancelled activity is never billed, and operational (planning-relevant) work
            // is only invoice-ready once COMPLETED — the same rule as orders (only Completed orders
            // are candidates). Purely commercial units (storage …) keep billing on their price alone.
            .Where(x => x.Activity.ExecutionStatus != DossierActivityExecutionStatus.Cancelled
                        && (!x.Type.PlanningRelevant || x.Activity.ExecutionStatus == DossierActivityExecutionStatus.Completed))
            .OrderBy(x => x.Dossier.DossierNumber).ThenBy(x => x.Activity.Sequence)
            .Select(x => new
            {
                x.Activity.Id, x.Activity.DossierId, x.Dossier.DossierNumber, DossierTitle = x.Dossier.Title,
                TypeName = x.Type.Name, x.Activity.Label, x.Activity.PlannedDate,
                x.Pricing.AgreedPrice, x.Pricing.FreeConfirmed, x.Pricing.PricingSource, PricingId = x.Pricing.Id,
                x.Dossier.LegalEntityId,
            })
            .ToListAsync(cancellationToken);

        var pricingIds = rows.Select(r => r.PricingId).ToList();
        var lineCounts = pricingIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await _dbContext.DossierActivityPriceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && pricingIds.Contains(l.DossierActivityPricingId))
                .GroupBy(l => l.DossierActivityPricingId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return rows.Select(r =>
        {
            var agreed = r.AgreedPrice ?? 0m;
            return new UninvoicedActivityDto(
                r.Id, r.DossierId, r.DossierNumber, r.DossierTitle, r.TypeName, r.Label, r.PlannedDate,
                agreed, agreed == 0m && r.FreeConfirmed,
                r.PricingSource.ToString(), lineCounts.GetValueOrDefault(r.PricingId),
                r.LegalEntityId);
        }).ToList();
    }

    public async Task<InvoiceOperationResult> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var customerRow = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.TenantId == tenantId, cancellationToken);
        if (customerRow is null)
        {
            return InvoiceOperationResult.InvalidReference("De gekoppelde klant bestaat niet.");
        }

        foreach (var line in request.ManualLines)
        {
            if (string.IsNullOrWhiteSpace(line.Description))
            {
                return InvoiceOperationResult.Invalid("Elke factuurlijn heeft een omschrijving nodig.");
            }

            if (line.Quantity <= 0)
            {
                return InvoiceOperationResult.Invalid("De hoeveelheid van een factuurlijn moet groter zijn dan nul.");
            }
        }

        var requestedActivityIds = (request.DossierActivityIds ?? []).Distinct().ToList();
        if (requestedActivityIds.Count != (request.DossierActivityIds?.Count ?? 0))
        {
            return InvoiceOperationResult.Invalid(
                "Dezelfde activiteit staat meermaals in de selectie; kies elke activiteit maar één keer.");
        }

        if (request.OrderIds.Count == 0 && request.ManualLines.Count == 0 && requestedActivityIds.Count == 0)
        {
            return InvoiceOperationResult.Invalid("Een factuur heeft minstens één lijn nodig.");
        }

        // H-06: the same order twice on one invoice is double billing, not a selection quirk —
        // it would produce two identical lines while only one order flips to Invoiced.
        var requestedOrderIds = request.OrderIds.Distinct().ToList();
        if (requestedOrderIds.Count != request.OrderIds.Count)
        {
            return InvoiceOperationResult.Invalid(
                "Dezelfde opdracht staat meermaals in de selectie; kies elke opdracht maar één keer.");
        }

        // Orders: completed, of this customer, not yet on a live invoice.
        List<UninvoicedOrderDto> orderDtos = [];
        if (requestedOrderIds.Count > 0)
        {
            var candidates = await ListUninvoicedOrdersAsync(request.CustomerId, cancellationToken);
            var byId = candidates.ToDictionary(o => o.Id);
            foreach (var orderId in requestedOrderIds)
            {
                if (!byId.TryGetValue(orderId, out var dto))
                {
                    return InvoiceOperationResult.Invalid(
                        "Een geselecteerde opdracht is niet factureerbaar (niet afgerond, andere klant of al gefactureerd).");
                }

                // ACC-B1 (layer 1): a completed order without a resolved price is "Nakijken" — it is
                // listed for context but can never become a € 0,00 invoice line.
                if (!dto.IsInvoiceable)
                {
                    var reasons = InvoiceReadinessReasonLabels(dto.InvoiceReadinessReasons);
                    return InvoiceOperationResult.Invalid(
                        $"Opdracht {dto.OrderNumber} is niet factureerbaar ({reasons}). Prijs de opdracht of markeer ze expliciet als gratis; niet-geprijsd werk wordt nooit als € 0,00 gefactureerd.");
                }

                orderDtos.Add(dto);
            }
        }

        // Standalone activities: priced, of this customer, no linked order, not on a live invoice
        // — the candidate list IS the rule, so nothing can be billed that it would not offer.
        List<UninvoicedActivityDto> activityDtos = [];
        if (requestedActivityIds.Count > 0)
        {
            var candidates = await ListUninvoicedActivitiesAsync(request.CustomerId, cancellationToken);
            var byId = candidates.ToDictionary(a => a.Id);
            foreach (var activityId in requestedActivityIds)
            {
                if (!byId.TryGetValue(activityId, out var dto))
                {
                    return InvoiceOperationResult.Invalid(
                        "Een geselecteerde activiteit is niet factureerbaar (niet geprijsd, via een opdracht gefactureerd, andere klant of al gefactureerd).");
                }

                activityDtos.Add(dto);
            }
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        // Default line VAT: the customer's own rate wins; without one, non-domestic VAT
        // treatments (reverse charge, intra-community, export, exempt) default to 0%.
        // Audit fix: the resolver is the ONE place deciding a line's treatment (override → sales
        // code → customer → tenant). The invoice-level rate below is what the customer's own
        // treatment yields; a sales code with a statutory classification deviates per line.
        var tenantDefaultRate = settings?.DefaultVatRatePercent ?? 21m;
        _defaultTenantRate = tenantDefaultRate;
        var vatRate = Modules.Accounting.Services.InvoiceLineFiscalResolver
            .Resolve(null, null, customerRow.VatTreatment, customerRow.DefaultVatRatePercent, tenantDefaultRate)
            .RatePercent;
        // MD-M3: "today" is the tenant's calendar day (UTC is still yesterday after local midnight).
        var invoiceDate = request.InvoiceDate ?? await Common.TenantTimeZone.TodayAsync(_dbContext, _tenantContext.TenantId, _timeProvider, cancellationToken);

        // Issuing entity: explicit choice → customer default → tenant default.
        LegalEntity? legalEntity;
        if (request.LegalEntityId is { } explicitEntityId)
        {
            legalEntity = await _dbContext.LegalEntities.FirstOrDefaultAsync(
                e => e.TenantId == tenantId && e.Id == explicitEntityId && e.IsActive, cancellationToken);
            if (legalEntity is null)
            {
                return InvoiceOperationResult.InvalidReference("De facturerende entiteit bestaat niet of is niet actief.");
            }
        }
        else
        {
            legalEntity = customerRow.DefaultLegalEntityId is { } customerDefault
                ? await _dbContext.LegalEntities.FirstOrDefaultAsync(
                    e => e.TenantId == tenantId && e.Id == customerDefault && e.IsActive, cancellationToken)
                : null;
            legalEntity ??= await GetDefaultLegalEntityAsync(cancellationToken);
        }

        // One invoice = one issuing entity: every selected order must belong to the resolved
        // entity. A mismatch is a validation error — the entity is never silently switched to
        // make the batch valid. Orders without an entity (pre-entity legacy data) are exempt.
        var orderEntityIds = orderDtos
            .Where(o => o.LegalEntityId is not null)
            .Select(o => o.LegalEntityId!.Value)
            .Concat(activityDtos.Where(a => a.LegalEntityId is not null).Select(a => a.LegalEntityId!.Value))
            .Distinct()
            .ToList();
        if (orderEntityIds.Count > 1)
        {
            return InvoiceOperationResult.Invalid(
                "De geselecteerde opdrachten en activiteiten horen bij verschillende facturerende entiteiten en kunnen niet op één factuur gecombineerd worden.");
        }

        if (orderEntityIds.Count == 1 && orderEntityIds[0] != legalEntity?.Id)
        {
            var mismatched = string.Join(", ", orderDtos
                .Where(o => o.LegalEntityId == orderEntityIds[0])
                .Select(o => o.OrderNumber)
                .Concat(activityDtos.Where(a => a.LegalEntityId == orderEntityIds[0]).Select(a => $"{a.DossierNumber}/{a.ActivityTypeName}")));
            return InvoiceOperationResult.Invalid(
                $"De facturerende entiteit van de factuur wijkt af van die van opdracht(en)/activiteit(en) {mismatched}. " +
                "Kies de entiteit van de opdrachten of pas de opdrachten aan.");
        }

        // Wave 2 (spec Part O): the invoice entity must also be in the customer's allowed set
        // (empty set = no restriction) — the same policy the dossier/order side enforces.
        if (await Modules.Partners.Services.CustomerEntityPolicy.ValidateAsync(
                _dbContext, tenantId, request.CustomerId, legalEntity?.Id, cancellationToken) is { } entityPolicyError)
        {
            return InvoiceOperationResult.Invalid(entityPolicyError);
        }

        // Invoice period drives the numbering sequence: default = invoice-date month; an
        // explicitly picked earlier month is allowed (invoicing July in August), a future
        // month never is.
        var periodYear = request.InvoicePeriodYear ?? invoiceDate.Year;
        var periodMonth = request.InvoicePeriodMonth ?? invoiceDate.Month;
        if (ValidatePeriod(periodYear, periodMonth, invoiceDate) is { } periodError)
        {
            return InvoiceOperationResult.Invalid(periodError);
        }

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = request.CustomerId,
            LegalEntityId = legalEntity?.Id,
            InvoicePeriodYear = periodYear,
            InvoicePeriodMonth = periodMonth,
            InvoiceDate = invoiceDate,
            // ACC-H5: customer term → legal entity term → tenant default; the draft's due date follows
            // the invoice date until a user deliberately sets another one.
            PaymentTermDays = EffectivePaymentTermDays(customerRow, legalEntity, settings),
            Currency = legalEntity?.DefaultCurrency ?? settings?.DefaultCurrency ?? "EUR",
            Notes = Trim(request.Notes),
            // Wave 2 §3: the document language freezes at creation, like the seller snapshot.
            LanguageCode = customerRow.InvoiceLanguageCode ?? customerRow.DefaultLanguageCode ?? "nl",
        };
        invoice.DueDate = invoiceDate.AddDays(invoice.PaymentTermDays);

        // ACC-M12: the customer reads WHAT was transported, not only an order number and a route.
        var cargoByOrder = request.OrderIds.Count == 0
            ? new Dictionary<Guid, List<Modules.Orders.Entities.CargoItem>>()
            : (await _dbContext.CargoItems.AsNoTracking()
                .Where(c => c.TenantId == tenantId && request.OrderIds.Contains(c.TransportOrderId) && !c.IsDeleted)
                .OrderBy(c => c.Sequence)
                .ToListAsync(cancellationToken))
                .GroupBy(c => c.TransportOrderId)
                .ToDictionary(g => g.Key, g => g.ToList());
        var unitNames = await _dbContext.UnitTypes.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Code, u.Name, u.Symbol })
            .ToListAsync(cancellationToken);
        var unitNameByCode = unitNames.ToDictionary(u => u.Code, u => u.Name, StringComparer.OrdinalIgnoreCase);
        string? UnitLabelFor(string? unit) =>
            string.IsNullOrWhiteSpace(unit) ? null
            : unitNameByCode.TryGetValue(unit.Trim(), out var name) ? name
            : unitNames.FirstOrDefault(u => string.Equals(u.Name, unit.Trim(), StringComparison.OrdinalIgnoreCase))?.Name
            ?? UnEceUnitCodeMap.LabelFor(unit) ?? unit.Trim();

        // Snapshotted service lines become separate invoice lines; the base transport line
        // excludes their amounts (AgreedPrice is the order TOTAL incl. services).
        var selectedOrderIds = orderDtos.Select(o => o.Id).ToList();
        var serviceLinesByOrder = selectedOrderIds.Count == 0
            ? new Dictionary<Guid, List<Modules.Orders.Entities.TransportOrderServiceLine>>()
            : (await _dbContext.TransportOrderServiceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && selectedOrderIds.Contains(l.TransportOrderId))
                .ToListAsync(cancellationToken))
                .GroupBy(l => l.TransportOrderId)
                .ToDictionary(g => g.Key, g => g.ToList());

        // Sales categorisation (§7.2 + Wave 2): the sales code frozen on the order's price and
        // service lines wins (stamped from service option → rule → agreement at calculation);
        // system roles stay the structural fallback — the base transport line, the
        // service/supplement lines and the diesel lines; manual lines carry the caller's
        // explicit choice. The ledger mapping itself resolves at Send, never here.
        await _accounting.EnsureSeededAsync(cancellationToken);
        var salesCategories = await _dbContext.SalesCategories.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .ToListAsync(cancellationToken);
        var activeCategoryById = salesCategories.ToDictionary(c => c.Id);
        Guid? CategoryForRole(Modules.Accounting.Entities.SalesCategorySystemRole role) =>
            salesCategories.FirstOrDefault(c => c.SystemRole == role)?.Id;

        // Per-line rate: identical to the invoice rate unless the line's sales code carries a
        // statutory classification of its own (sprint 5D).
        decimal RateFor(Guid? categoryId) =>
            categoryId is { } cid && activeCategoryById.TryGetValue(cid, out var category)
                ? Modules.Accounting.Services.InvoiceLineFiscalResolver
                    .Resolve(null, category, customerRow.VatTreatment, customerRow.DefaultVatRatePercent, tenantDefaultRate)
                    .RatePercent
                : vatRate;

        // The stamped codes of the lines that make up the aggregated base transport amount: one
        // unanimous code moves the base line off the Transport role; a mix stays on the role
        // (one aggregate line cannot represent two codes — Wave 3 may split it).
        var stampedCategoriesByOrder = selectedOrderIds.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _dbContext.TransportOrderPricingLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && selectedOrderIds.Contains(l.TransportOrderId)
                            && !l.Informational && !l.Proposed && l.SalesCategoryId != null)
                .Select(l => new { l.TransportOrderId, l.SalesCategoryId })
                .ToListAsync(cancellationToken))
                .GroupBy(l => l.TransportOrderId)
                .ToDictionary(g => g.Key, g => g.Select(l => l.SalesCategoryId!.Value).Distinct().ToList());
        Guid? StampedBaseCategory(Guid orderId)
        {
            var stamped = stampedCategoriesByOrder.GetValueOrDefault(orderId);
            return stamped is [var single] && activeCategoryById.ContainsKey(single) ? single : null;
        }
        var transportCategoryId = CategoryForRole(Modules.Accounting.Entities.SalesCategorySystemRole.Transport);
        var surchargeCategoryId = CategoryForRole(Modules.Accounting.Entities.SalesCategorySystemRole.Surcharge);
        var dieselCategoryId = CategoryForRole(Modules.Accounting.Entities.SalesCategorySystemRole.Diesel);
        var manualCategoryIds = request.ManualLines
            .Where(m => m.SalesCategoryId is not null).Select(m => m.SalesCategoryId!.Value).Distinct().ToList();
        if (manualCategoryIds.Count > 0
            && await _dbContext.SalesCategories.CountAsync(
                c => c.TenantId == tenantId && manualCategoryIds.Contains(c.Id), cancellationToken) != manualCategoryIds.Count)
        {
            throw new Common.InvalidTenantReferenceException("verkoopcategorie");
        }

        // Customer-facing wording on generated lines follows the frozen invoice language via
        // the stored string catalog (rule E: no machine translation, no Dutch on a French invoice).
        var strings = InvoicePdfStrings.For(invoice.LanguageCode);

        var sequence = 1;
        foreach (var order in orderDtos)
        {
            var route = order.FirstLoadingCity is not null || order.LastUnloadingCity is not null
                ? $" ({order.FirstLoadingCity ?? "?"} → {order.LastUnloadingCity ?? "?"})"
                : string.Empty;
            var orderServiceLines = serviceLinesByOrder.GetValueOrDefault(order.Id) ?? [];
            var serviceTotal = orderServiceLines.Sum(l => l.Amount);
            var goods = GoodsSummary(order.GoodsDescription, cargoByOrder.GetValueOrDefault(order.Id), UnitLabelFor, strings);
            var freeSuffix = order.IsFree ? $" ({strings.FreeLabel})" : string.Empty;
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransportOrderId = order.Id,
                Sequence = sequence++,
                Description = $"{order.OrderNumber} — {goods}{route}{freeSuffix}",
                Quantity = 1m,
                // ACC-M1: an explicitly free transport is a deliberate € 0 line; an unpriced one never
                // gets here (ListUninvoicedOrdersAsync marks it non-invoiceable, the guard above refuses).
                UnitPrice = order.IsFree ? 0m : (order.AgreedPrice ?? 0m) - serviceTotal,
                VatRatePercent = RateFor(StampedBaseCategory(order.Id) ?? transportCategoryId),
                SalesCategoryId = StampedBaseCategory(order.Id) ?? transportCategoryId,
            });
            foreach (var serviceLine in orderServiceLines)
            {
                // The frozen effective invoice description wins (customer override > global > name).
                var description = serviceLine.InvoiceDescriptionSnapshot ?? serviceLine.NameSnapshot;
                var quantitySuffix = serviceLine.Quantity is { } serviceQuantity
                    ? $" ({serviceQuantity:0.##} {(serviceLine.Kind == Modules.Tarification.Entities.SurchargeKind.PerHour ? strings.HourUnit : strings.StopsUnit)})"
                    : string.Empty;
                invoice.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    TransportOrderId = order.Id,
                    Sequence = sequence++,
                    Description = $"{order.OrderNumber} — {description}{quantitySuffix}",
                    Quantity = 1m,
                    UnitPrice = serviceLine.Amount,
                    VatRatePercent = RateFor(
                        serviceLine.SalesCategoryId is { } stampedForRate && activeCategoryById.ContainsKey(stampedForRate)
                            ? stampedForRate
                            : surchargeCategoryId),
                    SalesCategoryId = serviceLine.SalesCategoryId is { } stamped && activeCategoryById.ContainsKey(stamped)
                        ? stamped
                        : surchargeCategoryId,
                    UnitCode = serviceLine.Kind == Modules.Tarification.Entities.SurchargeKind.PerHour ? "HUR" : "C62",
                    UnitLabel = serviceLine.Quantity is null ? null
                        : serviceLine.Kind == Modules.Tarification.Entities.SurchargeKind.PerHour ? strings.HourUnit : strings.StopsUnit,
                });
            }
        }

        // ACC-H6: standalone activity lines carry no system role; without a stamped sales code they
        // default to the "Diensten & toeslagen" category (services rendered), so every line is
        // classified before issue — the issue preflight still blocks a line that ends up without one.
        var activityDefaultCategoryId = surchargeCategoryId;

        // Standalone activities: a OneOff price is one line; a Lines price reproduces every
        // sales line (quantity, unit, unit price, stamped sales code). A confirmed-free activity
        // is an explicit € 0 line — the decision is documented, and the activity leaves the
        // candidate list like any other. No system role fits an activity: the stamped sales code
        // of a price line wins, otherwise the line stays uncategorised for the invoicing user.
        var activityIdsSelected = activityDtos.Select(a => a.Id).ToList();
        var activityPricings = activityIdsSelected.Count == 0
            ? []
            : await _dbContext.DossierActivityPricings
                .Where(p => p.TenantId == tenantId && activityIdsSelected.Contains(p.DossierActivityId))
                .ToListAsync(cancellationToken);
        var activityPricingIds = activityPricings.Select(p => p.Id).ToList();
        var activityPriceLines = activityPricingIds.Count == 0
            ? new Dictionary<Guid, List<Modules.Dossiers.Entities.DossierActivityPriceLine>>()
            : (await _dbContext.DossierActivityPriceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && activityPricingIds.Contains(l.DossierActivityPricingId))
                .OrderBy(l => l.Sequence)
                .ToListAsync(cancellationToken))
                .GroupBy(l => l.DossierActivityPricingId)
                .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var activity in activityDtos)
        {
            var pricing = activityPricings.Single(p => p.DossierActivityId == activity.Id);
            var heading = string.IsNullOrWhiteSpace(activity.Label)
                ? $"{activity.DossierNumber} — {activity.ActivityTypeName}"
                : $"{activity.DossierNumber} — {activity.ActivityTypeName}: {activity.Label.Trim()}";
            var priceLines = activityPriceLines.GetValueOrDefault(pricing.Id) ?? [];
            if (pricing.PricingSource == Modules.Dossiers.Entities.ActivityPricingSource.Lines && priceLines.Count > 0)
            {
                foreach (var priceLine in priceLines)
                {
                    var stamped = priceLine.SalesCategoryId is { } sc && activeCategoryById.ContainsKey(sc) ? sc : (Guid?)null;
                    invoice.Lines.Add(new InvoiceLine
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        DossierActivityId = activity.Id,
                        Sequence = sequence++,
                        Description = $"{heading} — {priceLine.Label}",
                        Quantity = priceLine.Quantity,
                        UnitPrice = priceLine.UnitPrice,
                        VatRatePercent = RateFor(stamped ?? activityDefaultCategoryId),
                        SalesCategoryId = stamped ?? activityDefaultCategoryId,
                        // P7: structured UN/ECE code from the unit type / symbol, human label kept apart.
                        UnitCode = UnEceUnitCodeMap.Resolve(priceLine.Unit),
                        UnitLabel = UnitLabelFor(priceLine.Unit),
                    });
                }
            }
            else
            {
                invoice.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    DossierActivityId = activity.Id,
                    Sequence = sequence++,
                    Description = activity.IsFree ? $"{heading} ({strings.FreeLabel})" : heading,
                    Quantity = 1m,
                    UnitPrice = activity.AgreedPrice,
                    VatRatePercent = RateFor(activityDefaultCategoryId),
                    SalesCategoryId = activityDefaultCategoryId,
                });
            }

            // Same lifecycle as an order's pricing snapshot: invoice generation is the only
            // path to Invoiced; every pricing guard refuses edits from here on.
            pricing.Status = Modules.Orders.Entities.OrderPricingStatus.Invoiced;
            pricing.Version = Guid.NewGuid();
        }

        foreach (var manual in request.ManualLines)
        {
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Sequence = sequence++,
                Description = manual.Description.Trim(),
                Quantity = manual.Quantity,
                UnitPrice = manual.UnitPrice,
                // ACC-H1: null = what the treatment dictates (never a silent 0 under domestic VAT).
                VatRatePercent = manual.VatRatePercent ?? RateFor(manual.SalesCategoryId),
                SalesCategoryId = manual.SalesCategoryId,
                // Wave 2: the sales code's default unit fills in when the caller gave none
                // (NormalizeUnitCode itself falls back to C62 when both are empty).
                UnitCode = NormalizeUnitCode(
                    Trim(manual.UnitCode)
                    ?? (manual.SalesCategoryId is { } mcid
                        ? activeCategoryById.GetValueOrDefault(mcid)?.DefaultUnitCode
                        : null)),
                UnitLabel = UnEceUnitCodeMap.LabelFor(Trim(manual.UnitCode)),
            });
        }

        // Claim the invoice number and flip the orders to Invoiced in the same save.
        var orders = orderDtos.Count == 0
            ? []
            : await _dbContext.TransportOrders
                .Where(o => o.TenantId == tenantId && requestedOrderIds.Contains(o.Id))
                .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.Status = TransportOrderStatus.Invoiced;
        }

        // Order pricing status lifecycle (spec ch. 24-26): invoice generation is the only path
        // that reaches Invoiced. No error when an order carries no pricing snapshot at all.
        if (orders.Count > 0)
        {
            var invoicedOrderIds = orders.Select(o => o.Id).ToList();
            var pricingSnapshots = await _dbContext.TransportOrderPricingSnapshots
                .Where(s => s.TenantId == tenantId && invoicedOrderIds.Contains(s.TransportOrderId))
                .ToListAsync(cancellationToken);
            foreach (var pricingSnapshot in pricingSnapshots)
            {
                pricingSnapshot.Status = Modules.Orders.Entities.OrderPricingStatus.Invoiced;
            }
        }

        // Diesel surcharge: customer config (order overrides respected) → separate lines.
        var surchargeConfig = await _dbContext.CustomerDieselSurcharges
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.CustomerId == request.CustomerId, cancellationToken);
        if (orderDtos.Count > 0 && DieselSurchargeCalculator.Applies(surchargeConfig, invoiceDate))
        {
            var overridesByOrder = orders.ToDictionary(
                o => o.Id, o => o.DieselSurchargeOverride ? o.DieselSurchargePercentOverride : null);
            // Rule F: the base is decided by the sales code of each generated line — only codes
            // flagged "meetellen in basis dieseltoeslag" count, and the diesel code itself is
            // excluded structurally. Never the raw order amount.
            var bases = orderDtos
                .Select(o => new DieselSurchargeCalculator.OrderBase(
                    o.Id, o.OrderNumber,
                    Modules.Accounting.Services.InvoiceLineFiscalResolver.DieselBase(
                        invoice.Lines
                            .Where(l => l.TransportOrderId == o.Id)
                            .Select(l => (
                                l.SalesCategoryId is { } cid ? activeCategoryById.GetValueOrDefault(cid) : null,
                                MoneyRounding.LineTotal(l.Quantity, l.UnitPrice)))),
                    overridesByOrder.GetValueOrDefault(o.Id)))
                .ToList();
            foreach (var surchargeLine in DieselSurchargeCalculator.BuildLines(surchargeConfig!, bases, invoiceDate, strings))
            {
                invoice.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    TransportOrderId = surchargeLine.OrderId,
                    Sequence = sequence++,
                    Description = surchargeLine.Description,
                    Quantity = 1m,
                    UnitPrice = surchargeLine.Amount,
                    VatRatePercent = RateFor(dieselCategoryId),
                    SalesCategoryId = dieselCategoryId,
                });
            }
        }

        // PO number: explicit request value → effective customer PO → single distinct order reference.
        invoice.PurchaseOrderNumber = Trim(request.PurchaseOrderNumber)
            ?? await _billingConfig.ResolveEffectivePoNumberAsync(request.CustomerId, invoiceDate, cancellationToken)
            ?? SingleDistinctReference(orders);

        await ApplySnapshotsAsync(invoice, legalEntity, customerRow, cancellationToken);

        // ACC-H3: a draft has NO legal number — it is claimed atomically at issue, so deleting or
        // cancelling this draft never leaves a gap in the series.
        invoice.InvoiceNumber = null;
        _dbContext.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "Created", null,
            new { invoice.DraftReference, invoice.CustomerId, LineCount = invoice.Lines.Count }, cancellationToken);

        var draftCustomerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId && c.TenantId == tenantId)
            .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        await PublishEventAsync(MessageKinds.InvoiceDraftReady, new NotificationEventContext(
            EntityType, invoice.Id.ToString(),
            new Dictionary<string, string> { ["invoiceNumber"] = invoice.DisplayNumber, ["customerName"] = draftCustomerName })
        {
            CustomerId = invoice.CustomerId,
            LinkPath = $"/invoices/{invoice.Id}",
            InAppMessage = $"Conceptfactuur {invoice.DisplayNumber} voor {draftCustomerName} is klaar.",
        }, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
    }

    public async Task<InvoiceOperationResult> UpdateAsync(
        Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceOperationResult.NotFound;
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return InvoiceOperationResult.InvalidState("Alleen conceptfacturen kunnen worden bewerkt.");
        }

        if (request.Lines.Count == 0)
        {
            return InvoiceOperationResult.Invalid("Een factuur heeft minstens één lijn nodig.");
        }

        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description))
            {
                return InvoiceOperationResult.Invalid("Elke factuurlijn heeft een omschrijving nodig.");
            }

            if (line.Quantity <= 0)
            {
                return InvoiceOperationResult.Invalid("De hoeveelheid van een factuurlijn moet groter zijn dan nul.");
            }
        }

        // H-06: a mirrored credit-note line reproduces the credited line's sales code and the
        // whole fiscal freeze that hangs off it. Accepting a different code here would leave
        // SalesCategoryId pointing at one code and the frozen snapshots at another, so the edit
        // is refused out loud instead of being silently ignored. Amounts, wording and dropping
        // the line entirely stay editable — that is how a partial credit is made.
        var mirroredById = invoice.Lines
            .Where(l => !l.IsDeleted && InvoiceLineMirror.IsMirrored(invoice, l))
            .ToDictionary(l => l.Id);
        foreach (var line in request.Lines)
        {
            if (line.Id is { } mirroredId && mirroredById.TryGetValue(mirroredId, out var mirrored)
                && line.SalesCategoryId != mirrored.SalesCategoryId)
            {
                return InvoiceOperationResult.Invalid(
                    "De verkoopcategorie van een gecrediteerde lijn ligt vast: een creditnota volgt de factuur die ze crediteert. "
                    + "Verwijder de lijn en voeg een nieuwe toe als je een andere categorie nodig hebt.");
            }
        }

        var before = new { invoice.InvoiceDate, LineCount = invoice.Lines.Count };

        // Optional invoice-period change (Draft only): re-issues a number in the new period.
        var newPeriodYear = request.InvoicePeriodYear ?? invoice.InvoicePeriodYear;
        var newPeriodMonth = request.InvoicePeriodMonth ?? invoice.InvoicePeriodMonth;
        var periodChanged = newPeriodYear != invoice.InvoicePeriodYear || newPeriodMonth != invoice.InvoicePeriodMonth;
        if (periodChanged && ValidatePeriod(newPeriodYear, newPeriodMonth, request.InvoiceDate) is { } periodError)
        {
            return InvoiceOperationResult.Invalid(periodError);
        }

        // ACC-H5: the due date follows the invoice date through the effective term, unless the user
        // deliberately set another date — a due date equal to the previous (automatic) value is
        // "unchanged" and is recalculated; a different one is a manual override that is kept.
        var previousAutoDue = invoice.InvoiceDate.AddDays(invoice.PaymentTermDays);
        var dueIsManual = invoice.DueDateIsManual
            ? request.DueDate != previousAutoDue && request.DueDate != request.InvoiceDate.AddDays(invoice.PaymentTermDays)
            : request.DueDate != invoice.DueDate && request.DueDate != previousAutoDue && request.DueDate != request.InvoiceDate.AddDays(invoice.PaymentTermDays);
        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDateIsManual = dueIsManual;
        invoice.DueDate = dueIsManual
            ? (request.DueDate < request.InvoiceDate ? request.InvoiceDate : request.DueDate)
            : request.InvoiceDate.AddDays(invoice.PaymentTermDays);
        invoice.Notes = Trim(request.Notes);
        invoice.PurchaseOrderNumber = Trim(request.PurchaseOrderNumber);
        var paymentReference = Trim(request.PaymentReference);
        if (paymentReference is { Length: > 30 })
        {
            return InvoiceOperationResult.Invalid("De betalingsreferentie mag maximaal 30 tekens lang zijn.");
        }

        invoice.PaymentReference = paymentReference;

        // While Draft, snapshots track current master data; Sent freezes them for good.
        // Credit notes are the exception: they mirror the CREDITED document and must never be
        // re-snapshotted from live master data, or their frozen line VAT categories would
        // contradict a since-changed customer treatment.
        if (invoice.Kind != InvoiceKind.CreditNote)
        {
            var snapshotEntity = invoice.LegalEntityId is { } snapEntityId
                ? await _dbContext.LegalEntities.FirstOrDefaultAsync(
                    e => e.TenantId == _tenantContext.TenantId && e.Id == snapEntityId, cancellationToken)
                : null;
            var snapshotCustomer = await _dbContext.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == invoice.CustomerId, cancellationToken);
            if (snapshotCustomer is not null)
            {
                await ApplySnapshotsAsync(invoice, snapshotEntity, snapshotCustomer, cancellationToken);
            }
        }

        // ACC-H1: a line without an explicit rate takes what the treatment dictates.
        var defaultRate = DefaultVatRate(invoice);

        var requestedCategoryIds = request.Lines
            .Where(l => l.SalesCategoryId is not null).Select(l => l.SalesCategoryId!.Value).Distinct().ToList();
        if (requestedCategoryIds.Count > 0
            && await _dbContext.SalesCategories.CountAsync(
                c => c.TenantId == _tenantContext.TenantId && requestedCategoryIds.Contains(c.Id), cancellationToken)
                != requestedCategoryIds.Count)
        {
            return InvoiceOperationResult.Invalid("Een gekozen verkoopcategorie bestaat niet.");
        }

        var existingById = invoice.Lines.Where(l => !l.IsDeleted).ToDictionary(l => l.Id);
        var keptIds = request.Lines.Where(l => l.Id is not null).Select(l => l.Id!.Value).ToHashSet();

        // Dropped order-backed lines release their orders back to Completed.
        var releasedOrderIds = existingById.Values
            .Where(l => !keptIds.Contains(l.Id) && l.TransportOrderId is not null)
            .Select(l => l.TransportOrderId!.Value)
            .ToList();
        if (releasedOrderIds.Count > 0)
        {
            var releasedOrders = await _dbContext.TransportOrders
                .Where(o => o.TenantId == _tenantContext.TenantId && releasedOrderIds.Contains(o.Id)
                            && o.Status == TransportOrderStatus.Invoiced)
                .ToListAsync(cancellationToken);
            foreach (var order in releasedOrders)
            {
                order.Status = TransportOrderStatus.Completed;
            }

            await ReleasePricingSnapshotsAsync(
                releasedOrders.Select(o => o.Id).ToList(), cancellationToken);
        }

        // Dropped activity lines release their activity only when NONE of its lines is kept
        // (a Lines-priced activity spans several invoice lines).
        var keptActivityIds = existingById.Values
            .Where(l => keptIds.Contains(l.Id) && l.DossierActivityId is not null)
            .Select(l => l.DossierActivityId!.Value)
            .ToHashSet();
        await ActivityPricingRelease.ReleaseAsync(_dbContext, _tenantContext.TenantId,
            existingById.Values
                .Where(l => !keptIds.Contains(l.Id) && l.DossierActivityId is { } aid && !keptActivityIds.Contains(aid))
                .Select(l => l.DossierActivityId!.Value)
                .Distinct()
                .ToList(),
            cancellationToken);

        var removed = existingById.Values.Where(l => !keptIds.Contains(l.Id)).ToList();
        _dbContext.RemoveRange(removed);

        var newLines = new List<InvoiceLine>();
        var sequence = 1;
        foreach (var line in request.Lines)
        {
            if (line.Id is { } lineId && existingById.TryGetValue(lineId, out var existing))
            {
                existing.Sequence = sequence++;
                existing.Description = line.Description.Trim();
                existing.Quantity = line.Quantity;
                existing.UnitPrice = line.UnitPrice;
                existing.VatRatePercent = line.VatRatePercent ?? defaultRate;
                existing.UnitCode = NormalizeUnitCode(line.UnitCode ?? existing.UnitCode);
                existing.UnitLabel = Trim(line.UnitLabel) ?? existing.UnitLabel ?? UnEceUnitCodeMap.LabelFor(existing.UnitCode);
                // The draft editor always round-trips the current value, so an explicit null IS
                // a deliberate clear ("— Geen —") — silently keeping the old category would
                // freeze and export a category the user removed.
                existing.SalesCategoryId = line.SalesCategoryId;
            }
            else
            {
                newLines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.TenantId,
                    InvoiceId = invoice.Id,
                    Sequence = sequence++,
                    Description = line.Description.Trim(),
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    VatRatePercent = line.VatRatePercent ?? defaultRate,
                    SalesCategoryId = line.SalesCategoryId,
                    UnitCode = NormalizeUnitCode(line.UnitCode),
                    UnitLabel = Trim(line.UnitLabel) ?? UnEceUnitCodeMap.LabelFor(NormalizeUnitCode(line.UnitCode)),
                });
            }
        }

        // Client-generated ids: mark Added explicitly (navigation discovery would attach as Modified).
        _dbContext.AddRange(newLines);
        invoice.Lines = invoice.Lines.Where(l => keptIds.Contains(l.Id)).Concat(newLines).ToList();

        if (periodChanged)
        {
            // ACC-H3: the period only decides which sequence the number is claimed from AT ISSUE;
            // nothing is claimed or abandoned here.
            var oldPeriod = new { invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth };
            invoice.InvoicePeriodYear = newPeriodYear;
            invoice.InvoicePeriodMonth = newPeriodMonth;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "PeriodChanged", oldPeriod,
                new { invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth }, cancellationToken);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "Updated", before,
            new { invoice.InvoiceDate, LineCount = invoice.Lines.Count }, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
    }

    public async Task<InvoiceOperationResult> ChangeStatusAsync(
        Guid id, InvoiceStatus target, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceOperationResult.NotFound;
        }

        // H-06 — the finalization gate comes FIRST so a refused cancellation says why (and points
        // at the credit note) instead of producing the generic transition message.
        if (target == InvoiceStatus.Cancelled && await IsFinalizedAsync(invoice, cancellationToken))
        {
            return InvoiceOperationResult.InvalidState(
                invoice.Kind == InvoiceKind.CreditNote
                    ? "Deze creditnota is al definitief (verzonden of doorgestuurd) en kan niet meer geannuleerd worden."
                    : $"Deze factuur is al definitief (verzonden of doorgestuurd) en kan niet meer geannuleerd worden. {CreditNoteHint}");
        }

        if (!Transitions[invoice.Status].Contains(target))
        {
            return InvoiceOperationResult.InvalidState($"Een factuur met status '{invoice.Status}' kan niet naar '{target}'.");
        }

        if (target == InvoiceStatus.Sent)
        {
            // ACC-H4: the final refresh from master data happens here, right before the freeze, so
            // the issued document shows the customer and seller as they are at issue — never later.
            if (invoice.Kind != InvoiceKind.CreditNote)
            {
                var issueEntity = invoice.LegalEntityId is { } issueEntityId
                    ? await _dbContext.LegalEntities.AsNoTracking().FirstOrDefaultAsync(
                        e => e.TenantId == _tenantContext.TenantId && e.Id == issueEntityId, cancellationToken)
                    : null;
                var issueCustomer = await _dbContext.Customers.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == invoice.CustomerId, cancellationToken);
                if (issueCustomer is not null)
                {
                    await ApplySnapshotsAsync(invoice, issueEntity, issueCustomer, cancellationToken);
                }
            }

            // ACC-B1/H1/H6/M4: ONE preflight decides; the same list is shown beforehand by the
            // issue-preflight endpoint, so nothing here can surprise the accountant.
            var issues = await InvoiceIssuePreflight.EvaluateAsync(_dbContext, _tenantContext.TenantId, invoice, cancellationToken);
            if (issues.Count > 0)
            {
                return InvoiceOperationResult.InvalidState(InvoiceIssuePreflight.Summarize(issues));
            }
        }

        if (target == InvoiceStatus.Paid)
        {
            // ACC-M6: a document that is fully credited has nothing left to pay.
            var credited = await TenantScoped().AsNoTracking()
                .Where(i => i.CreditedInvoiceId == invoice.Id && i.Status != InvoiceStatus.Cancelled)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);
            if (credited.Count > 0)
            {
                var creditedTotal = await _dbContext.InvoiceLines.AsNoTracking()
                    .Where(l => l.TenantId == _tenantContext.TenantId && credited.Contains(l.InvoiceId))
                    .Select(l => new { l.Quantity, l.UnitPrice })
                    .ToListAsync(cancellationToken);
                var creditedAmount = creditedTotal.Sum(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice));
                var invoiceAmount = invoice.Lines.Where(l => !l.IsDeleted).Sum(l => MoneyRounding.LineTotal(l.Quantity, l.UnitPrice));
                if (creditedAmount >= invoiceAmount)
                {
                    return InvoiceOperationResult.InvalidState(
                        "Deze factuur is volledig gecrediteerd; er valt niets meer te betalen. Markeer ze niet als betaald.");
                }
            }

            invoice.PaidAt = _timeProvider.GetUtcNow().UtcDateTime;
            invoice.PaidByUserId = _currentUser?.CurrentUserId;
        }

        var before = new { invoice.Status };
        invoice.Status = target;

        if (target == InvoiceStatus.Sent)
        {
            // Never guess a VAT category: without the treatment snapshot the freeze below would
            // stamp permanent, possibly wrong categories on the lines.
            if (string.IsNullOrWhiteSpace(invoice.CustomerVatTreatment))
            {
                return InvoiceOperationResult.InvalidState(
                    "De btw-regeling van de klant ontbreekt op deze factuur; bewerk en bewaar de conceptfactuur eerst opnieuw.");
            }

            // H-06: resolve which lines are copies of a credited document BEFORE the first pass
            // stamps snapshots of its own — after that, a freshly frozen line is indistinguishable
            // from a copy. Both passes then skip exactly this set.
            var mirroredLineIds = InvoiceLineMirror.MirroredIds(invoice);

            // §7.3: freeze the sales category + ledger account from the THEN-current mapping.
            // Later mapping changes never rewrite these lines; the accounting export reads
            // exclusively from these snapshots.
            await FreezeLedgerSnapshotsAsync(invoice, mirroredLineIds, cancellationToken);
            // Sprint 5H: the sales-code resolver freezes treatment, rate, category, description
            // language and cost centre for every line WITH a sales code. It must run before the
            // customer-level category fallback below, otherwise a sales code's statutory
            // exception could never win (audit fix).
            await FreezeSalesCodeSnapshotsAsync(invoice, mirroredLineIds, cancellationToken);

            // Gap filler for lines without any category at all; never overwrites a frozen value,
            // so on a credit note it only touches what the credited document itself left open.
            FreezeVatCategories(invoice);
        }

        if (target == InvoiceStatus.Cancelled)
        {
            await ReleaseOrdersAsync(invoice, cancellationToken);
            await CancelQueuedPeppolTransmissionsAsync(invoice, cancellationToken);
        }

        if (target == InvoiceStatus.Sent)
        {
            // ACC-H3/P4: the legal number is claimed HERE, atomically with the status flip and the
            // frozen snapshots (ClaimAsync saves everything in one SaveChanges and retries on a
            // concurrent claim). A manually reserved number (NumberIsManual) is kept as is.
            invoice.IssuedAt = _timeProvider.GetUtcNow().UtcDateTime;
            // MD-H3: from here on PDF, UBL and Peppol validation read this snapshot only.
            invoice.PartySnapshotFrozenAt = invoice.IssuedAt;
            if (invoice.NumberIsManual && !string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var issuingEntity = invoice.LegalEntityId is { } numberingEntityId
                    ? await _dbContext.LegalEntities.FirstOrDefaultAsync(
                        e => e.TenantId == _tenantContext.TenantId && e.Id == numberingEntityId, cancellationToken)
                    : null;
                if (issuingEntity is not null)
                {
                    await _numberService.ClaimAsync(issuingEntity, invoice.Kind, invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth,
                        number => invoice.InvoiceNumber = number, cancellationToken);
                }
                else
                {
                    // No legal entity configured (pre-seed edge case): legacy tenant counter.
                    var legacySettings = await _dbContext.TenantSettings
                        .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
                    await TenantNumbering.SaveWithClaimedNumberAsync(
                        _dbContext, legacySettings,
                        () => invoice.InvoiceNumber = (invoice.Kind == InvoiceKind.CreditNote ? "CN" : string.Empty) + GenerateInvoiceNumber(legacySettings),
                        cancellationToken);
                }
            }

            await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "Issued",
                new { invoice.DraftReference }, new { invoice.InvoiceNumber, invoice.IssuedAt }, cancellationToken);
        }
        else
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), target == InvoiceStatus.Paid ? "Paid" : "StatusChanged", before,
            target == InvoiceStatus.Paid ? new { invoice.Status, invoice.PaidAt, invoice.PaidByUserId } : new { invoice.Status, PaidAt = (DateTime?)null, PaidByUserId = (Guid?)null }, cancellationToken);

        if (target == InvoiceStatus.Sent)
        {
            var sentCustomerName = await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == invoice.CustomerId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            await PublishEventAsync(MessageKinds.InvoiceSent, new NotificationEventContext(
                EntityType, invoice.Id.ToString(),
                new Dictionary<string, string> { ["invoiceNumber"] = invoice.InvoiceNumber ?? string.Empty, ["customerName"] = sentCustomerName })
            {
                CustomerId = invoice.CustomerId,
                LinkPath = $"/invoices/{invoice.Id}",
            }, cancellationToken);
        }

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
    }

    public async Task<InvoiceOperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceOperationResult.NotFound;
        }

        if (invoice.Status is not (InvoiceStatus.Draft or InvoiceStatus.Cancelled))
        {
            return InvoiceOperationResult.InvalidState("Alleen concept- of geannuleerde facturen kunnen worden verwijderd.");
        }

        // H-06 — deletion is the other way out, and it must obey the same rule as cancellation,
        // whatever the current status says. Two reasons: a DRAFT row that already reached the
        // provider (status written around the API, or rolled back by an older build) would have
        // its orders released below, which is the leak itself; and a document that consumed an
        // issued invoice number must stay readable for the audit trail even once cancelled.
        // A genuine draft leaves none of this evidence, so nothing legitimate is blocked.
        if (await WasEverFinalizedAsync(invoice, cancellationToken))
        {
            var document = invoice.Kind == InvoiceKind.CreditNote ? "creditnota" : "factuur";
            return InvoiceOperationResult.InvalidState(
                $"Deze {document} is ooit verzonden en kan niet verwijderd worden; ze blijft bewaard als historisch document.");
        }

        if (invoice.Status == InvoiceStatus.Draft)
        {
            await ReleaseOrdersAsync(invoice, cancellationToken);
        }

        _dbContext.RemoveRange(invoice.Lines);
        _dbContext.Remove(invoice); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "Deleted",
            new { invoice.DisplayNumber, invoice.Status }, null, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
    }

    public async Task<InvoiceOperationResult> OverrideNumberAsync(
        Guid id, OverrideInvoiceNumberRequest request, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceOperationResult.NotFound;
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return InvoiceOperationResult.InvalidState(
                "Een toegekend factuurnummer wordt nooit meer gewijzigd. Alleen op een concept kan vooraf een handmatig nummer worden vastgelegd; het wordt bij verzenden gebruikt.");
        }

        var newNumber = request.InvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(newNumber) || newNumber.Length > 30)
        {
            return InvoiceOperationResult.Invalid("Een factuurnummer van maximaal 30 tekens is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return InvoiceOperationResult.Invalid("Een reden is verplicht bij een handmatige nummercorrectie.");
        }

        var duplicate = await TenantScoped().AnyAsync(
            i => i.Id != invoice.Id && i.InvoiceNumber == newNumber, cancellationToken);
        if (duplicate)
        {
            return InvoiceOperationResult.Invalid($"Factuurnummer '{newNumber}' is al in gebruik.");
        }

        var oldNumber = invoice.InvoiceNumber;
        invoice.InvoiceNumber = newNumber;
        invoice.NumberIsManual = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "NumberOverridden",
            new { InvoiceNumber = oldNumber },
            new { InvoiceNumber = newNumber, request.Reason }, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
    }

    public async Task<InvoiceNumberPreviewDto?> PreviewNextNumberAsync(
        Guid? legalEntityId, int? year, int? month, CancellationToken cancellationToken)
    {
        var legalEntity = legalEntityId is { } id
            ? await _dbContext.LegalEntities.FirstOrDefaultAsync(
                e => e.TenantId == _tenantContext.TenantId && e.Id == id && e.IsActive, cancellationToken)
            : await GetDefaultLegalEntityAsync(cancellationToken);
        if (legalEntity is null)
        {
            return null;
        }

        var today = await Common.TenantTimeZone.TodayAsync(_dbContext, _tenantContext.TenantId, _timeProvider, cancellationToken);
        var previewYear = year ?? today.Year;
        var previewMonth = Math.Clamp(month ?? today.Month, 1, 12);
        var number = await _numberService.PreviewAsync(legalEntity, InvoiceKind.Invoice, previewYear, previewMonth, cancellationToken);
        return new InvoiceNumberPreviewDto(number, legalEntity.Id, previewYear, previewMonth);
    }

    /// <summary>
    /// MD-M4 (master-data acceptance 2026-10-18): the invoice address was always the customer's
    /// registered address, even when an address was explicitly marked "Standaard facturatie". The
    /// active default billing address of the customer — when there is one — is the invoice address;
    /// otherwise the registered address, as before.
    /// </summary>
    private async Task ApplySnapshotsAsync(Invoice invoice, LegalEntity? entity, Customer customer, CancellationToken cancellationToken)
    {
        ApplySnapshots(invoice, entity, customer);
        var billing = await _dbContext.CustomerLocationLinks.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.CustomerId == customer.Id && l.IsDefaultBilling && l.IsActive)
            .Join(_dbContext.Locations.AsNoTracking().Where(x => x.IsActive), l => l.LocationId, x => x.Id,
                (l, x) => new { x.Street, x.HouseNumber, x.PostalCode, x.City, x.CountryCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (billing is not null && (billing.Street is not null || billing.City is not null))
        {
            invoice.CustomerStreetSnapshot = billing.Street;
            invoice.CustomerHouseNumberSnapshot = billing.HouseNumber;
            invoice.CustomerPostalCodeSnapshot = billing.PostalCode;
            invoice.CustomerCitySnapshot = billing.City;
            invoice.CustomerCountryCodeSnapshot = Trim(billing.CountryCode)?.ToUpperInvariant() ?? invoice.CustomerCountryCodeSnapshot;
        }
    }

    /// <summary>
    /// Freezes seller + customer presentation and fiscal facts on the invoice (refreshed while
    /// Draft, final refresh at issue, never afterwards — ACC-H4/P1/P5/P6). Structured address
    /// fields feed the UBL party blocks; the joined address line feeds the PDF.
    /// </summary>
    private static void ApplySnapshots(Invoice invoice, LegalEntity? entity, Customer customer)
    {
        if (entity is not null)
        {
            invoice.SellerName = entity.TradingName ?? entity.LegalName;
            invoice.SellerLegalName = entity.LegalName;
            invoice.SellerCompanyNumber = entity.CompanyNumber;
            invoice.SellerVatNumber = entity.VatNumber;
            invoice.SellerIban = entity.Iban;
            invoice.SellerBic = entity.Bic;
            invoice.SellerStreet = entity.Street;
            invoice.SellerHouseNumber = entity.HouseNumber;
            invoice.SellerPostalCode = entity.PostalCode;
            invoice.SellerCity = entity.City;
            invoice.SellerCountryCode = Trim(entity.CountryCode)?.ToUpperInvariant();
            invoice.SellerPeppolScheme = entity.PeppolScheme;
            invoice.SellerPeppolId = entity.PeppolId;
            invoice.SellerInvoiceFooter = entity.InvoiceFooter;
            invoice.SellerAddressLine = AddressLine(entity.Street, entity.HouseNumber, entity.PostalCode, entity.City, null);
            // MD-H3: the logo file VERSION of the moment (a new upload gets a new key).
            invoice.SellerLogoStorageKey = entity.LogoStorageKey;
        }

        invoice.CustomerVatTreatment = customer.VatTreatment.ToString();
        invoice.CustomerVatNumberSnapshot = customer.VatNumber;
        invoice.CustomerNameSnapshot = customer.Name;
        invoice.CustomerLegalNameSnapshot = customer.LegalName;
        invoice.CustomerCompanyNumberSnapshot = customer.CompanyNumber;
        invoice.CustomerStreetSnapshot = customer.Street;
        invoice.CustomerHouseNumberSnapshot = customer.HouseNumber;
        invoice.CustomerPostalCodeSnapshot = customer.PostalCode;
        invoice.CustomerCitySnapshot = customer.City;
        invoice.CustomerCountryCodeSnapshot = Trim(customer.CountryCode)?.ToUpperInvariant();
        invoice.CustomerPeppolSchemeSnapshot = customer.PeppolScheme;
        invoice.CustomerPeppolIdSnapshot = customer.PeppolId;
        invoice.CustomerBuyerReferenceSnapshot = customer.BuyerReference;
        invoice.VatLegalText = Partners.Services.VatTreatmentCatalog.Resolve(customer.VatTreatment).InvoiceLegalText;
    }

    /// <summary>"Straat 12, 2000 Antwerpen, BE" — the display form of a structured address.</summary>
    public static string? AddressLine(string? street, string? houseNumber, string? postalCode, string? city, string? countryCode)
    {
        var streetPart = string.Join(' ', new[] { street, houseNumber }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var cityPart = string.Join(' ', new[] { postalCode, city }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var line = string.Join(", ", new[] { streetPart, cityPart, countryCode }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    /// <summary>ACC-H5: customer term → legal entity term → tenant default.</summary>
    private static int EffectivePaymentTermDays(Customer customer, LegalEntity? entity, TenantSettings? settings) =>
        customer.PaymentTermDays > 0 ? customer.PaymentTermDays
        : entity is { PaymentTermDays: > 0 } ? entity.PaymentTermDays
        : settings?.PaymentTermDays ?? 30;

    /// <summary>The rate a manual line takes under this invoice's frozen treatment (ACC-H1).</summary>
    private decimal DefaultVatRate(Invoice invoice)
    {
        var treatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed) ? parsed : VatTreatment.DomesticVat;
        var info = Partners.Services.VatTreatmentCatalog.Resolve(treatment);
        if (info.DefaultRatePercent is { } fixedRate)
        {
            return fixedRate;
        }

        // Domestic: the rate the invoice already charges (its order/activity lines), else the tenant default.
        var charged = invoice.Lines.Where(l => !l.IsDeleted && l.VatRatePercent > 0m).Select(l => l.VatRatePercent).FirstOrDefault();
        return charged > 0m ? charged : _defaultTenantRate ?? 21m;
    }

    private decimal? _defaultTenantRate;

    /// <summary>"2 Europallet TEST pallets; 1 Colli kabels" — what the customer reads on the transport line (ACC-M12).</summary>
    private static string GoodsSummary(
        string? goodsDescription, IReadOnlyList<Modules.Orders.Entities.CargoItem>? cargo, Func<string?, string?> unitLabel, InvoicePdfStrings strings)
    {
        if (cargo is { Count: > 0 })
        {
            var parts = cargo.Take(3).Select(c =>
            {
                var unit = unitLabel(c.QuantityUnitCode ?? c.QuantityUnit) ?? c.QuantityUnit;
                var quantity = $"{c.ExpectedQuantity:0.##}{(string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit)}";
                return string.IsNullOrWhiteSpace(c.Description) ? quantity : $"{quantity} {c.Description.Trim()}";
            });
            var summary = string.Join("; ", parts) + (cargo.Count > 3 ? " …" : string.Empty);
            return string.IsNullOrWhiteSpace(goodsDescription) ? summary : $"{goodsDescription.Trim()} — {summary}";
        }

        return string.IsNullOrWhiteSpace(goodsDescription) ? strings.TransportLabel : goodsDescription.Trim();
    }

    /// <summary>Readiness reason codes as the accountant reads them in Facturatiecontrole.</summary>
    private static string InvoiceReadinessReasonLabels(string? reasons) =>
        string.IsNullOrWhiteSpace(reasons)
            ? "niet klaar voor facturatie"
            : string.Join(", ", reasons.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(r => r switch
            {
                "pricing.none" => "geen verkoopprijs",
                "pricing.coverage.partial" => "niet alle onderdelen geprijsd",
                "pricing.coverage.none" => "geen onderdeel geprijsd",
                "pricing.stale" => "prijs is verouderd",
                "pricing.zero_unconfirmed" => "€ 0 zonder bevestiging als gratis",
                "pod.missing" => "afleverbewijs ontbreekt",
                "po.missing" => "PO-nummer ontbreekt",
                _ => r,
            }));

    public async Task<InvoiceIssuePreflightDto?> PreflightAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return new InvoiceIssuePreflightDto(false,
                [new InvoiceIssueIssueDto("invoice.not_draft", "Alleen een concept kan verzonden worden.", [])]);
        }

        // Same refresh the issue itself performs, so the preview judges tomorrow's freeze, not yesterday's.
        if (invoice.Kind != InvoiceKind.CreditNote)
        {
            var entity = invoice.LegalEntityId is { } entityId
                ? await _dbContext.LegalEntities.AsNoTracking().FirstOrDefaultAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == entityId, cancellationToken)
                : null;
            var customer = await _dbContext.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == _tenantContext.TenantId && c.Id == invoice.CustomerId, cancellationToken);
            if (customer is not null)
            {
                await ApplySnapshotsAsync(invoice, entity, customer, cancellationToken);
            }
        }

        var issues = await InvoiceIssuePreflight.EvaluateAsync(_dbContext, _tenantContext.TenantId, invoice, cancellationToken);
        return InvoiceIssuePreflight.ToDto(issues);
    }

    public async Task<decimal?> DefaultVatRateForCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId)
            .Select(c => new { c.VatTreatment, c.DefaultVatRatePercent })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var settings = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => (decimal?)s.DefaultVatRatePercent)
            .FirstOrDefaultAsync(cancellationToken);
        return Modules.Accounting.Services.InvoiceLineFiscalResolver
            .Resolve(null, null, customer.VatTreatment, customer.DefaultVatRatePercent, settings ?? 21m)
            .RatePercent;
    }

    private async Task<LegalEntity?> GetDefaultLegalEntityAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.LegalEntities
            .Where(e => e.TenantId == _tenantContext.TenantId && e.IsActive)
            .OrderByDescending(e => e.IsDefault).ThenBy(e => e.LegalName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Null when valid; a Dutch error otherwise. Past months are fine, future months never.</summary>
    private string? ValidatePeriod(int year, int month, DateOnly invoiceDate)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100)
        {
            return "De factuurperiode is ongeldig.";
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var maxIndex = Math.Max(today.Year * 12 + today.Month, invoiceDate.Year * 12 + invoiceDate.Month);
        return year * 12 + month > maxIndex
            ? "De factuurperiode mag niet in de toekomst liggen."
            : null;
    }

    /// <summary>
    /// H-06 — "finalized": the document left the building. Either its own status says so
    /// (Sent or Paid), or a Peppol transmission for it got past the local queue, which means the
    /// provider has seen it (see <see cref="TransmittedStatuses"/>). A finalized document is
    /// never cancelled, so the orders on it can never return from Invoiced to Completed.
    /// </summary>
    private async Task<bool> IsFinalizedAsync(Invoice invoice, CancellationToken cancellationToken) =>
        invoice.Status is InvoiceStatus.Sent or InvoiceStatus.Paid
        || await _dbContext.PeppolTransmissions.AnyAsync(
            t => t.TenantId == _tenantContext.TenantId && t.InvoiceId == invoice.Id
                 && TransmittedStatuses.Contains(t.Status), cancellationToken);

    /// <summary>
    /// Was this (now cancelled) document ever finalized? There is no "was sent" flag, so the
    /// traces Send leaves behind are the evidence: a Peppol transmission of any kind, a credit
    /// note issued against it (only Sent/Paid invoices can be credited), or — for an invoice —
    /// a line carrying a snapshot that only the Send freeze writes. Credit-note lines COPY those
    /// snapshots at creation, so for a credit note only the first two signals count.
    /// </summary>
    private async Task<bool> WasEverFinalizedAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        if (await IsFinalizedAsync(invoice, cancellationToken))
        {
            return true;
        }

        if (await _dbContext.PeppolTransmissions.AnyAsync(
                t => t.TenantId == _tenantContext.TenantId && t.InvoiceId == invoice.Id, cancellationToken))
        {
            return true;
        }

        if (await TenantScoped().AnyAsync(i => i.CreditedInvoiceId == invoice.Id, cancellationToken))
        {
            return true;
        }

        return invoice.Kind != InvoiceKind.CreditNote
               && invoice.Lines.Any(l => !l.IsDeleted && InvoiceLineMirror.HasFrozenFiscalData(l));
    }

    /// <summary>
    /// A cancelled invoice must never leave the building: any Peppol transmission still in the
    /// queue is withdrawn with it. Transmissions already at the provider are left alone — the
    /// dispatcher refuses to submit for non-Sent/Paid invoices as the second belt.
    /// </summary>
    private async Task CancelQueuedPeppolTransmissionsAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var queued = await _dbContext.PeppolTransmissions
            .Where(t => t.TenantId == _tenantContext.TenantId && t.InvoiceId == invoice.Id
                        && t.Status == Modules.Peppol.Entities.PeppolTransmissionStatus.Queued)
            .ToListAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var transmission in queued)
        {
            transmission.Status = Modules.Peppol.Entities.PeppolTransmissionStatus.Cancelled;
            _dbContext.PeppolTransmissionEvents.Add(new Modules.Peppol.Entities.PeppolTransmissionEvent
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                TransmissionId = transmission.Id,
                Status = Modules.Peppol.Entities.PeppolTransmissionStatus.Cancelled,
                Timestamp = now,
                Detail = "Factuur geannuleerd; verzending vervallen.",
            });
        }
    }

    private async Task ReleaseOrdersAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        // Standalone activities on the document follow the same release rule as its orders.
        await ActivityPricingRelease.ReleaseAsync(_dbContext, _tenantContext.TenantId,
            invoice.Lines.Where(l => !l.IsDeleted && l.DossierActivityId is not null)
                .Select(l => l.DossierActivityId!.Value).Distinct().ToList(),
            cancellationToken);

        var orderIds = invoice.Lines
            .Where(l => !l.IsDeleted && l.TransportOrderId is not null)
            .Select(l => l.TransportOrderId!.Value)
            .Distinct()
            .ToList();
        if (orderIds.Count == 0)
        {
            return;
        }

        var orders = await _dbContext.TransportOrders
            .Where(o => o.TenantId == _tenantContext.TenantId && orderIds.Contains(o.Id)
                        && o.Status == TransportOrderStatus.Invoiced)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.Status = TransportOrderStatus.Completed;
        }

        await ReleasePricingSnapshotsAsync(orders.Select(o => o.Id).ToList(), cancellationToken);
    }

    /// <summary>
    /// Mirrors an order-status release (Invoiced → Completed) on its pricing snapshot. The rule
    /// itself lives in <see cref="Modules.Orders.Services.OrderPricingSnapshotRelease"/> because
    /// the order side releases orders too (customer/entity change) and must apply exactly the
    /// same one.
    /// </summary>
    private Task ReleasePricingSnapshotsAsync(IReadOnlyList<Guid> orderIds, CancellationToken cancellationToken) =>
        Modules.Orders.Services.OrderPricingSnapshotRelease.ReleaseAsync(
            _dbContext, _tenantContext.TenantId, orderIds, cancellationToken);

    /// <summary>§7.3: resolve category + mapped account for every category-carrying line at Send.</summary>
    private async Task FreezeLedgerSnapshotsAsync(
        Invoice invoice, IReadOnlySet<Guid> mirroredLineIds, CancellationToken cancellationToken)
    {
        var lines = invoice.Lines.Where(l => !l.IsDeleted).ToList();
        var categoryIds = lines.Where(l => l.SalesCategoryId is not null).Select(l => l.SalesCategoryId!.Value).Distinct().ToList();
        if (categoryIds.Count == 0)
        {
            return;
        }

        // MD-H2 (master-data acceptance 2026-10-18): the ledger is resolved with ONE precedence —
        // the invoicing entity's mapping, else the category default (InvoiceLineFiscalResolver.
        // LedgerFor). Freezing the category default here first made the entity mapping unreachable.
        var categories = await _dbContext.SalesCategories.AsNoTracking()
            .Include(c => c.LedgerMappings)
            .Where(c => c.TenantId == _tenantContext.TenantId && categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var accountIds = categories.Values
            .SelectMany(c => c.LedgerMappings.Select(m => (Guid?)m.LedgerAccountId).Append(c.LedgerAccountId))
            .OfType<Guid>().Distinct().ToList();
        var accounts = await _dbContext.LedgerAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.AccountNumber, a.Name })
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var line in lines)
        {
            // Never overwrite an already-frozen snapshot: freezing runs at Send and again only
            // via the explicit gap-filling action for lines that still miss their account.
            if (line.LedgerAccountNumberSnapshot is not null)
            {
                continue;
            }

            // H-06: a credit-note line copied from the credited document carries that document's
            // freeze — a MISSING account included (its category had none at Send). Filling that
            // gap here would book the credit against an account the invoice it reverses never
            // touched. A line the user added to the draft credit note itself has no treatment
            // snapshot and is frozen normally.
            if (mirroredLineIds.Contains(line.Id))
            {
                continue;
            }

            if (line.SalesCategoryId is not { } categoryId || !categories.TryGetValue(categoryId, out var category))
            {
                continue;
            }

            var (ledgerAccountId, _) = Modules.Accounting.Services.InvoiceLineFiscalResolver.LedgerFor(category, invoice.LegalEntityId);
            var account = ledgerAccountId is { } accountId ? accounts.GetValueOrDefault(accountId) : null;
            line.SalesCategoryNameSnapshot = category.Name;
            line.LedgerAccountId = account?.Id;
            line.LedgerAccountNumberSnapshot = account?.AccountNumber;
            line.LedgerAccountNameSnapshot = account?.Name;
            // Wave 2: a sales code can force the UNCL5305 VAT category; an explicit line value
            // always wins, and null keeps the customer's VAT-treatment chain authoritative.
            line.VatCategoryCode ??= category.VatCategoryOverride;
        }
    }

    /// <summary>
    /// Sprint 5H — freezes everything a line needs to be reproduced later: the sales code as it
    /// read at finalization, the language its customer-facing description was taken in, the
    /// fiscal treatment WITH the level that decided it, its statutory wording, and the ledger /
    /// cost centre of the invoicing entity. Never overwrites an already-frozen value, so a
    /// re-run (or a later correction flow) cannot rewrite history.
    /// </summary>
    private async Task FreezeSalesCodeSnapshotsAsync(
        Invoice invoice, IReadOnlySet<Guid> mirroredLineIds, CancellationToken cancellationToken)
    {
        // H-06: the `VatTreatmentSnapshot is null` filter is an idempotency check, NOT a mirror
        // check — a credit-note line copied from a pre-2026-08-28 invoice has a null treatment
        // snapshot and would be selected here, so it needs the mirror guard explicitly. Without it
        // this method would rewrite the credit's treatment, source, legal text, RATE, UBL category,
        // cost centre and customer-facing description from today's master data, and the credit note
        // could state a different VAT rate than the invoice it reverses.
        var lines = invoice.Lines
            .Where(l => !l.IsDeleted && l.SalesCategoryId is not null && l.VatTreatmentSnapshot is null
                        && !mirroredLineIds.Contains(l.Id))
            .ToList();
        if (lines.Count == 0)
        {
            return;
        }

        var codeIds = lines.Select(l => l.SalesCategoryId!.Value).Distinct().ToList();
        var salesCodes = await _dbContext.SalesCategories
            .Include(c => c.LedgerMappings)
            .Where(c => c.TenantId == _tenantContext.TenantId && codeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var customerTreatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed)
            ? (VatTreatment?)parsed
            : null;

        var ledgerNumbers = await _dbContext.LedgerAccounts.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId)
            .Select(a => new { a.Id, a.AccountNumber, a.Name })
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var line in lines)
        {
            if (!salesCodes.TryGetValue(line.SalesCategoryId!.Value, out var salesCode))
            {
                continue;
            }

            var lineOverride = Enum.TryParse<VatTreatment>(line.VatTreatmentOverride, out var overridden)
                ? (VatTreatment?)overridden
                : null;

            var resolution = Modules.Accounting.Services.InvoiceLineFiscalResolver.Resolve(
                lineOverride, salesCode, customerTreatment, line.VatRatePercent, line.VatRatePercent);

            line.SalesCodeSnapshot = salesCode.Code;
            line.DescriptionLanguageSnapshot = invoice.LanguageCode;

            // Sprint 5E/5F: put the APPROVED description for the invoice language on the line —
            // but only when the current text is still the code's own default. A label typed or
            // configured specifically for this line/order is the user's wording and is kept.
            // Same function as the draft preview/PDF — the customer never sees a different text
            // after Send than before it.
            line.Description = InvoiceLineDescriptions.CustomerFacing(line.Description, salesCode, invoice.LanguageCode);
            line.VatTreatmentSnapshot = resolution.Treatment.ToString();
            line.VatTreatmentSourceSnapshot = resolution.Source.ToString();
            line.VatLegalTextSnapshot = resolution.LegalText;
            // Finalization: the rate and UBL category are what the resolved treatment dictates.
            // For the customer's own (domestic) treatment this echoes the line's rate; for a
            // statutory treatment it corrects a draft rate that would otherwise be charged
            // while the line declares itself exempt/reverse-charged.
            line.VatRatePercent = resolution.RatePercent;
            line.VatCategoryCode = resolution.VatCategoryCode;

            var (ledgerAccountId, costCentre) =
                Modules.Accounting.Services.InvoiceLineFiscalResolver.LedgerFor(salesCode, invoice.LegalEntityId);
            line.CostCentreSnapshot = costCentre;

            // The sales code's entity-specific mapping wins over the category default, but only
            // when the line has not already frozen an account.
            if (ledgerAccountId is { } accountId && line.LedgerAccountNumberSnapshot is null
                && ledgerNumbers.TryGetValue(accountId, out var account))
            {
                line.LedgerAccountId = accountId;
                line.LedgerAccountNumberSnapshot = account.AccountNumber;
                line.LedgerAccountNameSnapshot = account.Name;
            }
        }
    }

    /// <summary>
    /// Freezes the UNCL5305 VAT category per line at Send, derived from the invoice's own
    /// VAT-treatment snapshot — the same immutability rule as the ledger snapshots. Never
    /// overwrites an already-frozen value.
    /// </summary>
    private static void FreezeVatCategories(Invoice invoice)
    {
        var treatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsed)
            ? parsed
            : VatTreatment.DomesticVat;
        foreach (var line in invoice.Lines.Where(l => !l.IsDeleted && l.VatCategoryCode is null))
        {
            line.VatCategoryCode = Partners.Services.VatTreatmentCatalog.ResolveVatCategory(treatment, line.VatRatePercent).Code;
        }
    }

    public async Task<InvoiceOperationResult> CreateCreditNoteAsync(Guid id, CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        // ACC-M11: crediting is a deliberate, motivated act.
        var reason = Trim(request.Reason);
        if (reason is null)
        {
            return InvoiceOperationResult.Invalid("Geef een reden op voor de creditnota.");
        }

        if (reason.Length > 500)
        {
            return InvoiceOperationResult.Invalid("De reden mag maximaal 500 tekens lang zijn.");
        }

        var original = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (original is null)
        {
            return InvoiceOperationResult.NotFound;
        }

        // MD-H3: the credit note copies the credited document's FINAL snapshot — a legacy original
        // is frozen once first, so the copy never inherits "not frozen yet" gaps.
        if (original.Status != InvoiceStatus.Draft && !original.PartySnapshotIsFinal)
        {
            await InvoicePartySnapshotBackfill.FreezeAsync(_dbContext, original, _timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        }

        if (original.Kind != InvoiceKind.Invoice)
        {
            return InvoiceOperationResult.InvalidState("Een creditnota kan zelf niet gecrediteerd worden.");
        }

        if (original.Status is not (InvoiceStatus.Sent or InvoiceStatus.Paid))
        {
            return InvoiceOperationResult.InvalidState("Alleen verzonden of betaalde facturen kunnen gecrediteerd worden.");
        }

        var existingCredit = await TenantScoped().AnyAsync(
            i => i.CreditedInvoiceId == original.Id && i.Status != InvoiceStatus.Cancelled, cancellationToken);
        if (existingCredit)
        {
            return InvoiceOperationResult.InvalidState(
                "Er bestaat al een creditnota voor deze factuur. Annuleer die eerst als je opnieuw wilt crediteren.");
        }

        var tenantId = _tenantContext.TenantId;
        var settings = await _dbContext.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        var today = await Common.TenantTimeZone.TodayAsync(_dbContext, _tenantContext.TenantId, _timeProvider, cancellationToken);

        var creditNote = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = InvoiceKind.CreditNote,
            CreditedInvoiceId = original.Id,
            CustomerId = original.CustomerId,
            LegalEntityId = original.LegalEntityId,
            InvoicePeriodYear = today.Year,
            InvoicePeriodMonth = today.Month,
            InvoiceDate = today,
            // A credit note has nothing to pay: no term, the due date equals the document date.
            DueDate = today,
            PaymentTermDays = 0,
            Currency = original.Currency,
            PurchaseOrderNumber = original.PurchaseOrderNumber,
            LanguageCode = original.LanguageCode,
            CreditReason = reason,
            Notes = $"Creditnota voor factuur {original.InvoiceNumber}. Reden: {reason}",
            // Snapshot copy FROM the credited document, never from live master data: the credit
            // note must mirror exactly what it credits (ACC-H4: the whole party snapshot travels).
            SellerName = original.SellerName,
            SellerVatNumber = original.SellerVatNumber,
            SellerIban = original.SellerIban,
            SellerAddressLine = original.SellerAddressLine,
            SellerLegalName = original.SellerLegalName,
            SellerCompanyNumber = original.SellerCompanyNumber,
            SellerStreet = original.SellerStreet,
            SellerHouseNumber = original.SellerHouseNumber,
            SellerPostalCode = original.SellerPostalCode,
            SellerCity = original.SellerCity,
            SellerCountryCode = original.SellerCountryCode,
            SellerBic = original.SellerBic,
            SellerPeppolScheme = original.SellerPeppolScheme,
            SellerPeppolId = original.SellerPeppolId,
            SellerInvoiceFooter = original.SellerInvoiceFooter,
            SellerLogoStorageKey = original.SellerLogoStorageKey,
            CustomerVatTreatment = original.CustomerVatTreatment,
            CustomerVatNumberSnapshot = original.CustomerVatNumberSnapshot,
            CustomerNameSnapshot = original.CustomerNameSnapshot,
            CustomerLegalNameSnapshot = original.CustomerLegalNameSnapshot,
            CustomerCompanyNumberSnapshot = original.CustomerCompanyNumberSnapshot,
            CustomerStreetSnapshot = original.CustomerStreetSnapshot,
            CustomerHouseNumberSnapshot = original.CustomerHouseNumberSnapshot,
            CustomerPostalCodeSnapshot = original.CustomerPostalCodeSnapshot,
            CustomerCitySnapshot = original.CustomerCitySnapshot,
            CustomerCountryCodeSnapshot = original.CustomerCountryCodeSnapshot,
            CustomerPeppolSchemeSnapshot = original.CustomerPeppolSchemeSnapshot,
            CustomerPeppolIdSnapshot = original.CustomerPeppolIdSnapshot,
            CustomerBuyerReferenceSnapshot = original.CustomerBuyerReferenceSnapshot,
            VatLegalText = original.VatLegalText,
        };

        // H-06: every copy must carry at least one freeze marker, or it cannot be told apart from a
        // line typed on the credit note itself and Send would re-derive it from live master data.
        // A line so old that it predates the UBL category (Peppol wave) has none, so it is stamped
        // here with the category the CREDITED header dictates — byte for byte what FreezeVatCategories
        // would write at Send, only early enough to be recognisable as a mirror.
        var creditedTreatment = Enum.TryParse<VatTreatment>(original.CustomerVatTreatment, out var creditedParsed)
            ? creditedParsed
            : VatTreatment.DomesticVat;

        var sequence = 1;
        foreach (var line in original.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Sequence))
        {
            creditNote.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                // Deliberately NOT order-linked: crediting never re-opens the order lifecycle.
                TransportOrderId = null,
                Sequence = sequence++,
                Description = line.Description,
                // UBL credit notes carry POSITIVE amounts; the sign lives in the document kind.
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                VatRatePercent = line.VatRatePercent,
                UnitCode = line.UnitCode,
                UnitLabel = line.UnitLabel,
                VatCategoryCode = line.VatCategoryCode
                    ?? Partners.Services.VatTreatmentCatalog.ResolveVatCategory(creditedTreatment, line.VatRatePercent).Code,
                SalesCategoryId = line.SalesCategoryId,
                // H-06: the full fiscal freeze of the credited line travels with it. The credit
                // must book against the SAME ledger account, treatment and wording as the invoice
                // it reverses, whatever the sales-code mapping looks like today; Send therefore
                // skips both freeze passes for a credit note.
                SalesCategoryNameSnapshot = line.SalesCategoryNameSnapshot,
                LedgerAccountId = line.LedgerAccountId,
                LedgerAccountNumberSnapshot = line.LedgerAccountNumberSnapshot,
                LedgerAccountNameSnapshot = line.LedgerAccountNameSnapshot,
                SalesCodeSnapshot = line.SalesCodeSnapshot,
                DescriptionLanguageSnapshot = line.DescriptionLanguageSnapshot,
                VatTreatmentSnapshot = line.VatTreatmentSnapshot,
                VatTreatmentSourceSnapshot = line.VatTreatmentSourceSnapshot,
                VatLegalTextSnapshot = line.VatLegalTextSnapshot,
                CostCentreSnapshot = line.CostCentreSnapshot,
                VatTreatmentOverride = line.VatTreatmentOverride,
                VatTreatmentOverrideReason = line.VatTreatmentOverrideReason,
            });
        }

        // ACC-H3: like an invoice, the credit note gets its number from its own series at issue.
        creditNote.InvoiceNumber = null;
        _dbContext.Add(creditNote);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, creditNote.Id.ToString(), "CreditNoteCreated",
            new { CreditedInvoiceNumber = original.InvoiceNumber },
            new { creditNote.DraftReference, LineCount = creditNote.Lines.Count, Reason = reason }, cancellationToken);
        await _auditService.RecordAsync(EntityType, original.Id.ToString(), "Credited",
            null, new { CreditNoteReference = creditNote.DraftReference, CreditNoteId = creditNote.Id, Reason = reason }, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(creditNote, cancellationToken));
    }

    public async Task<InvoiceOperationResult> CompleteLedgerSnapshotsAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceOperationResult.NotFound;
        }

        if (invoice.Status is not (InvoiceStatus.Sent or InvoiceStatus.Paid))
        {
            return InvoiceOperationResult.InvalidState(
                "Alleen verzonden of betaalde facturen kunnen hun boekhoudsnapshot laten aanvullen; concepten bevriezen bij het verzenden.");
        }

        var missingBefore = invoice.Lines.Count(l => !l.IsDeleted && l.LedgerAccountNumberSnapshot is null);
        // H-06 (review M-3): this gap filler resolves against TODAY's mapping, so it must respect
        // the mirror rule too — a credit line that inherited a null account from the document it
        // credits keeps that null rather than booking somewhere the original never did.
        var mirroredIds = InvoiceLineMirror.MirroredIds(invoice);
        await FreezeLedgerSnapshotsAsync(invoice, mirroredIds, cancellationToken);
        // Wave 1 fix A (A9): …but a mirrored line may copy the account from the very line it
        // mirrors, once THAT line has one. Not today's mapping — the credited document's own
        // freeze, which is exactly what the mirror rule protects.
        await CopyLedgerSnapshotsFromCreditedLinesAsync(invoice, mirroredIds, cancellationToken);
        var missingAfter = invoice.Lines.Count(l => !l.IsDeleted && l.LedgerAccountNumberSnapshot is null);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "LedgerSnapshotsCompleted",
            new { MissingSnapshots = missingBefore }, new { MissingSnapshots = missingAfter }, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
    }

    /// <summary>
    /// Wave 1 fix A (A9) — fills the ledger gap of a MIRRORED credit-note line from the invoice
    /// line it credits.
    ///
    /// Why this is not "re-freezing": a credit line is a copy of one credited line, and its ledger
    /// account is a property of that line's own Send-time freeze. When the credited line was sent
    /// before its sales code had a mapping, both documents carry a null account and the accounting
    /// export blocks the whole date window for both. Completing the ORIGINAL fixes it there; before
    /// this method the credit note could never be fixed at all — the freeze pass skips mirrored
    /// lines by design, so the "Boekhoudsnapshot aanvullen" action was a silent no-op and the export
    /// stayed blocked forever, with no in-app remedy.
    ///
    /// The account is copied from the credited line, never resolved from today's mapping, so the
    /// credit still books exactly where the invoice it reverses booked. Lines are matched on their
    /// SalesCategoryId — the account IS a function of that code at freeze time — and only when the
    /// credited document agrees with itself: if two of its lines carry the same code but different
    /// frozen accounts (possible when one was completed later, under a changed mapping), nothing is
    /// copied rather than picking one at random.
    /// </summary>
    private async Task CopyLedgerSnapshotsFromCreditedLinesAsync(
        Invoice invoice, IReadOnlySet<Guid> mirroredLineIds, CancellationToken cancellationToken)
    {
        if (invoice.Kind != InvoiceKind.CreditNote || invoice.CreditedInvoiceId is not { } creditedInvoiceId)
        {
            return;
        }

        var gaps = invoice.Lines
            .Where(l => !l.IsDeleted && mirroredLineIds.Contains(l.Id)
                        && l.LedgerAccountNumberSnapshot is null && l.SalesCategoryId is not null)
            .ToList();
        if (gaps.Count == 0)
        {
            return;
        }

        var creditedLines = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && l.InvoiceId == creditedInvoiceId
                        && l.SalesCategoryId != null && l.LedgerAccountNumberSnapshot != null)
            .Select(l => new
            {
                CategoryId = l.SalesCategoryId!.Value,
                l.LedgerAccountId, l.LedgerAccountNumberSnapshot, l.LedgerAccountNameSnapshot, l.SalesCategoryNameSnapshot,
            })
            .ToListAsync(cancellationToken);
        if (creditedLines.Count == 0)
        {
            return;
        }

        var byCategory = creditedLines
            .GroupBy(l => l.CategoryId)
            .Where(g => g.Select(l => l.LedgerAccountNumberSnapshot).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var line in gaps)
        {
            if (!byCategory.TryGetValue(line.SalesCategoryId!.Value, out var credited))
            {
                continue;
            }

            line.LedgerAccountId = credited.LedgerAccountId;
            line.LedgerAccountNumberSnapshot = credited.LedgerAccountNumberSnapshot;
            line.LedgerAccountNameSnapshot = credited.LedgerAccountNameSnapshot;
            line.SalesCategoryNameSnapshot ??= credited.SalesCategoryNameSnapshot;
        }
    }

    private async Task<InvoiceDetailDto> MapDetailAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var customer = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId && c.TenantId == tenantId)
            .Select(c => new { c.Name, c.VatNumber })
            .FirstOrDefaultAsync(cancellationToken);

        var liveLines = invoice.Lines.Where(l => !l.IsDeleted).OrderBy(l => l.Sequence).ToList();

        var orderIds = liveLines.Where(l => l.TransportOrderId is not null).Select(l => l.TransportOrderId!.Value).Distinct().ToList();
        var orderNumbers = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
        var activityIds = liveLines.Where(l => l.DossierActivityId is not null).Select(l => l.DossierActivityId!.Value).Distinct().ToList();
        var dossierNumbersByActivity = activityIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.DossierActivities.AsNoTracking()
                .Where(a => a.TenantId == tenantId && activityIds.Contains(a.Id))
                .Join(_dbContext.TransportDossiers.AsNoTracking().Where(d => d.TenantId == tenantId),
                    a => a.DossierId, d => d.Id, (a, d) => new { a.Id, d.DossierNumber })
                .ToDictionaryAsync(x => x.Id, x => x.DossierNumber, cancellationToken);

        // Live category/mapping info while Draft; frozen snapshots afterwards (§7.3).
        var lineCategoryIds = liveLines.Where(l => l.SalesCategoryId is not null)
            .Select(l => l.SalesCategoryId!.Value).Distinct().ToList();
        // The whole sales code is needed while Draft: the fiscal preview resolves through the
        // same hierarchy that Send will freeze (line override → code → customer → tenant).
        var liveCategories = lineCategoryIds.Count == 0
            ? new Dictionary<Guid, Modules.Accounting.Entities.SalesCategory>()
            : await _dbContext.SalesCategories.AsNoTracking()
                .Include(c => c.LedgerAccount)
                .Where(c => c.TenantId == tenantId && lineCategoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        var isDraft = invoice.Status == InvoiceStatus.Draft;
        var mappedTreatment = Enum.TryParse<VatTreatment>(invoice.CustomerVatTreatment, out var parsedTreatment)
            ? parsedTreatment
            : VatTreatment.DomesticVat;
        var lines = liveLines.Select(l =>
        {
            var live = l.SalesCategoryId is { } categoryId ? liveCategories.GetValueOrDefault(categoryId) : null;
            // A mirrored line shows its freeze, draft or not: a draft credit note copied it from the
            // credited invoice and Send will not touch it (H-06). Same marker as the freeze guards,
            // so the preview can never disagree with what Send stores.
            var frozen = !isDraft || InvoiceLineMirror.IsMirrored(invoice, l);
            var categoryName = l.SalesCategoryNameSnapshot ?? live?.Name;
            var accountNumber = frozen ? l.LedgerAccountNumberSnapshot : live?.LedgerAccount?.AccountNumber;
            var accountName = frozen ? l.LedgerAccountNameSnapshot : live?.LedgerAccount?.Name;

            // Fiscal source: the frozen snapshot once frozen; while Draft, what Send WOULD freeze.
            string? fiscalTreatment, fiscalSource, fiscalLegalText, salesCode;
            if (!frozen)
            {
                var lineOverride = Enum.TryParse<VatTreatment>(l.VatTreatmentOverride, out var overridden) ? (VatTreatment?)overridden : null;
                var resolution = Modules.Accounting.Services.InvoiceLineFiscalResolver.Resolve(
                    lineOverride, live, mappedTreatment, l.VatRatePercent, l.VatRatePercent);
                fiscalTreatment = resolution.Treatment.ToString();
                fiscalSource = resolution.Source.ToString();
                fiscalLegalText = resolution.LegalText;
                salesCode = live?.Code;
            }
            else
            {
                fiscalTreatment = l.VatTreatmentSnapshot;
                fiscalSource = l.VatTreatmentSourceSnapshot;
                fiscalLegalText = l.VatLegalTextSnapshot;
                salesCode = l.SalesCodeSnapshot;
            }
            var warning = !frozen
                ? l.SalesCategoryId is null
                    ? "Geen verkoopcategorie gekozen voor deze lijn."
                    : accountNumber is null
                        ? $"Geen grootboekrekening ingesteld voor '{categoryName}'. Configureer deze bij Bedrijfsinstellingen → Boekhouding."
                        : null
                : null;
            return new InvoiceLineDto(
                l.Id, l.Sequence, l.TransportOrderId,
                l.TransportOrderId is { } oid ? orderNumbers.GetValueOrDefault(oid) : null,
                l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent,
                MoneyRounding.LineTotal(l.Quantity, l.UnitPrice),
                l.SalesCategoryId, categoryName, accountNumber, accountName, warning,
                l.UnitCode,
                // Frozen after Send; live-derived while Draft so the preview shows what WILL freeze.
                l.VatCategoryCode ?? Partners.Services.VatTreatmentCatalog.ResolveVatCategory(mappedTreatment, l.VatRatePercent).Code,
                fiscalTreatment, fiscalSource, fiscalLegalText, salesCode,
                // Draft: what Send will freeze (same rule as Send and the draft PDF); frozen: as stored.
                frozen ? l.Description : InvoiceLineDescriptions.CustomerFacing(l.Description, live, invoice.LanguageCode),
                l.DossierActivityId,
                l.DossierActivityId is { } aid ? dossierNumbersByActivity.GetValueOrDefault(aid) : null,
                l.UnitLabel ?? UnEceUnitCodeMap.LabelFor(l.UnitCode));
        }).ToList();

        var subtotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        var vat = InvoiceTotals.VatTotal(liveLines, invoice.CustomerVatTreatment);

        // MD-H3: an issued invoice names the seller AS ISSUED (found in the browser regression:
        // the header read the entity's later name while the PDF showed the snapshot).
        var legalEntityName = invoice.PartySnapshotIsFinal && (invoice.SellerName ?? invoice.SellerLegalName) is { } issuedSeller
            ? issuedSeller
            : invoice.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Id == entityId)
                .Select(e => e.TradingName ?? e.LegalName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var creditedInvoiceNumber = invoice.CreditedInvoiceId is { } creditedId
            ? await _dbContext.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.Id == creditedId)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        // The relation the other way round: which credit notes were issued against this invoice.
        var creditNoteRows = invoice.Kind == InvoiceKind.Invoice
            ? await _dbContext.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.CreditedInvoiceId == invoice.Id)
                .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber)
                .Select(i => new { i.Id, i.InvoiceNumber, i.Status })
                .ToListAsync(cancellationToken)
            : [];
        var creditNotes = creditNoteRows
            .Select(i => new InvoiceReferenceDto(i.Id, i.InvoiceNumber, i.Status, i.InvoiceNumber ?? "CONCEPT-" + i.Id.ToString("N")[..8].ToUpperInvariant()))
            .ToList();

        var paidByName = invoice.PaidByUserId is { } paidBy
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.Id == paidBy)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        // Presentation: the frozen snapshot once issued; while Draft the snapshot is refreshed on
        // every save, so it reads as the current master data (legacy drafts fall back to live).
        var customerAddress = AddressLine(invoice.CustomerStreetSnapshot, invoice.CustomerHouseNumberSnapshot,
            invoice.CustomerPostalCodeSnapshot, invoice.CustomerCitySnapshot, invoice.CustomerCountryCodeSnapshot);
        var paymentTermsText = invoice.Kind == InvoiceKind.CreditNote
            ? null
            : InvoicePdfStrings.For(invoice.LanguageCode).PaymentTermsText(invoice.PaymentTermDays, invoice.DueDate);

        return new InvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DueDate,
            invoice.CustomerId,
            // MD-H3: a FINAL snapshot never falls back to live data — an empty VAT stays empty.
            invoice.CustomerNameSnapshot ?? (invoice.PartySnapshotIsFinal ? null : customer?.Name) ?? string.Empty,
            invoice.PartySnapshotIsFinal ? invoice.CustomerVatNumberSnapshot : invoice.CustomerVatNumberSnapshot ?? customer?.VatNumber,
            invoice.Status, invoice.Currency, invoice.Notes,
            lines, subtotal, vat, subtotal + vat, Transitions[invoice.Status],
            invoice.LegalEntityId, legalEntityName,
            invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth, invoice.NumberIsManual,
            invoice.PurchaseOrderNumber,
            invoice.Kind, invoice.CreditedInvoiceId, creditedInvoiceNumber, invoice.PaymentReference,
            invoice.CustomerVatTreatment, invoice.LanguageCode,
            Partners.Services.VatTreatmentCatalog.Resolve(mappedTreatment).InvoiceLegalText,
            creditNotes,
            invoice.Status == InvoiceStatus.Draft ? invoice.DraftReference : null,
            invoice.DisplayNumber,
            DefaultVatRate(invoice),
            invoice.PaymentTermDays,
            invoice.DueDateIsManual,
            invoice.IssuedAt,
            invoice.PaidAt,
            paidByName,
            invoice.CreditReason,
            invoice.CustomerLegalNameSnapshot ?? invoice.CustomerNameSnapshot ?? customer?.Name,
            customerAddress,
            invoice.SellerLegalName ?? invoice.SellerName,
            invoice.SellerCompanyNumber,
            paymentTermsText);
    }

    private static string GenerateInvoiceNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"FAC-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }

        var number = $"{settings.InvoiceNumberPrefix}{settings.InvoiceNumberNextValue:0000}";
        settings.InvoiceNumberNextValue++;
        return number;
    }

    /// <summary>The orders' single distinct customer reference, if exactly one exists.</summary>
    private static string? SingleDistinctReference(IReadOnlyList<TransportOrder> orders)
    {
        var references = orders
            .Select(o => o.CustomerReference)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return references.Count == 1 ? references[0] : null;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Uppercased UN/ECE rec 20 code, max 10 chars; empty/null falls back to C62 (stuk).</summary>
    private static string NormalizeUnitCode(string? unitCode)
    {
        var trimmed = Trim(unitCode)?.ToUpperInvariant();
        if (trimmed is null)
        {
            return "C62";
        }

        if (trimmed.Length > 10 || !trimmed.All(char.IsAsciiLetterOrDigit))
        {
            throw new Common.DomainValidationException("unitCode",
                "De eenheidscode moet een UN/ECE-code van maximaal 10 letters of cijfers zijn (bv. C62, KGM, HUR).");
        }

        return trimmed;
    }
}
