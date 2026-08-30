using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
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

    public InvoiceService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IInvoiceNumberService numberService,
        Partners.Services.ICustomerBillingConfigService billingConfig,
        Accounting.Services.IAccountingService accounting,
        INotificationEventService? notificationEvents = null,
        ILogger<InvoiceService>? logger = null)
    {
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
        string? search, InvoiceStatus? status, Guid? customerId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = TenantScoped().AsNoTracking();

        if (status is { } s) query = query.Where(i => i.Status == s);
        if (customerId is { } c) query = query.Where(i => i.CustomerId == c);

        var joined = from i in query
                     join cu in _dbContext.Customers.AsNoTracking().Where(x => x.TenantId == _tenantContext.TenantId)
                         on i.CustomerId equals cu.Id
                     select new { i, CustomerName = cu.Name };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            joined = joined.Where(x =>
                x.i.InvoiceNumber.ToLower().Contains(term) || x.CustomerName.ToLower().Contains(term));
        }

        var totalCount = await joined.CountAsync(cancellationToken);

        var pageRows = await joined
            .OrderByDescending(x => x.i.InvoiceDate).ThenByDescending(x => x.i.InvoiceNumber)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(x => new { x.i.Id, x.i.InvoiceNumber, x.i.InvoiceDate, x.i.DueDate, x.i.CustomerId, x.CustomerName, x.i.Status, x.i.Currency, x.i.Kind })
            .ToListAsync(cancellationToken);

        var invoiceIds = pageRows.Select(r => r.Id).ToList();
        var lines = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == _tenantContext.TenantId && invoiceIds.Contains(l.InvoiceId))
            .Select(l => new { l.InvoiceId, l.Quantity, l.UnitPrice, l.VatRatePercent })
            .ToListAsync(cancellationToken);
        var linesByInvoice = lines.ToLookup(l => l.InvoiceId);

        var items = pageRows.Select(r =>
        {
            var invoiceLines = linesByInvoice[r.Id].ToList();
            var subtotal = Math.Round(invoiceLines.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), 2);
            // Same per-rate group rounding as InvoiceTotals/the UBL document.
            var vat = invoiceLines
                .GroupBy(l => l.VatRatePercent)
                .Sum(g => Math.Round(
                    Math.Round(g.Sum(l => Math.Round(l.Quantity * l.UnitPrice, 2)), 2) * g.Key / 100m, 2));
            return new InvoiceListItemDto(
                r.Id, r.InvoiceNumber, r.InvoiceDate, r.DueDate, r.CustomerId, r.CustomerName,
                r.Status, r.Currency, subtotal, vat, subtotal + vat, invoiceLines.Count, r.Kind);
        }).ToList();

        return new PagedResult<InvoiceListItemDto>(items, totalCount, page.Page, page.PageSize);
    }

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
                orderStops.LastOrDefault(s => s.StopType == StopType.Unloading)?.City,
                o.AgreedPrice,
                o.LegalEntityId,
                o.InvoiceReadiness, o.InvoiceReadinessReasons);
        }).ToList();
    }

    public async Task<InvoiceOperationResult> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var customerVat = await _dbContext.Customers
            .Where(c => c.Id == request.CustomerId && c.TenantId == tenantId)
            .Select(c => new
            {
                c.VatTreatment, c.DefaultVatRatePercent, c.DefaultLegalEntityId, c.VatNumber,
                c.InvoiceLanguageCode, c.DefaultLanguageCode,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (customerVat is null)
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

        if (request.OrderIds.Count == 0 && request.ManualLines.Count == 0)
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

                orderDtos.Add(dto);
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
        var vatRate = Modules.Accounting.Services.InvoiceLineFiscalResolver
            .Resolve(null, null, customerVat.VatTreatment, customerVat.DefaultVatRatePercent, tenantDefaultRate)
            .RatePercent;
        var invoiceDate = request.InvoiceDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

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
            legalEntity = customerVat.DefaultLegalEntityId is { } customerDefault
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
            .Distinct()
            .ToList();
        if (orderEntityIds.Count > 1)
        {
            return InvoiceOperationResult.Invalid(
                "De geselecteerde opdrachten horen bij verschillende facturerende entiteiten en kunnen niet op één factuur gecombineerd worden.");
        }

        if (orderEntityIds.Count == 1 && orderEntityIds[0] != legalEntity?.Id)
        {
            var mismatched = string.Join(", ", orderDtos
                .Where(o => o.LegalEntityId == orderEntityIds[0])
                .Select(o => o.OrderNumber));
            return InvoiceOperationResult.Invalid(
                $"De facturerende entiteit van de factuur wijkt af van die van opdracht(en) {mismatched}. " +
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
            DueDate = invoiceDate.AddDays(settings?.PaymentTermDays ?? 30),
            Currency = legalEntity?.DefaultCurrency ?? settings?.DefaultCurrency ?? "EUR",
            Notes = Trim(request.Notes),
            // Wave 2 §3: the document language freezes at creation, like the seller snapshot.
            LanguageCode = customerVat.InvoiceLanguageCode ?? customerVat.DefaultLanguageCode ?? "nl",
        };

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
                    .Resolve(null, category, customerVat.VatTreatment, customerVat.DefaultVatRatePercent, tenantDefaultRate)
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
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransportOrderId = order.Id,
                Sequence = sequence++,
                Description = $"{order.OrderNumber} — {order.GoodsDescription}{route}",
                Quantity = 1m,
                UnitPrice = (order.AgreedPrice ?? 0m) - serviceTotal,
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
                });
            }
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
                VatRatePercent = manual.VatRatePercent ?? RateFor(manual.SalesCategoryId),
                SalesCategoryId = manual.SalesCategoryId,
                // Wave 2: the sales code's default unit fills in when the caller gave none
                // (NormalizeUnitCode itself falls back to C62 when both are empty).
                UnitCode = NormalizeUnitCode(
                    Trim(manual.UnitCode)
                    ?? (manual.SalesCategoryId is { } mcid
                        ? activeCategoryById.GetValueOrDefault(mcid)?.DefaultUnitCode
                        : null)),
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
                                Math.Round(l.Quantity * l.UnitPrice, 2)))),
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

        ApplySnapshots(invoice, legalEntity, customerVat.VatTreatment, customerVat.VatNumber);

        _dbContext.Add(invoice);
        if (legalEntity is not null)
        {
            // Entity-scoped monthly sequence (concurrency-safe claim + save in one go).
            await _numberService.ClaimAsync(legalEntity, periodYear, periodMonth,
                number => invoice.InvoiceNumber = number, cancellationToken);
        }
        else
        {
            // No legal entity configured (pre-seed edge case): legacy tenant counter.
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () => invoice.InvoiceNumber = GenerateInvoiceNumber(settings),
                cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "Created", null,
            new { invoice.InvoiceNumber, invoice.CustomerId, LineCount = invoice.Lines.Count }, cancellationToken);

        var draftCustomerName = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.Id == invoice.CustomerId && c.TenantId == tenantId)
            .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        await PublishEventAsync(MessageKinds.InvoiceDraftReady, new NotificationEventContext(
            EntityType, invoice.Id.ToString(),
            new Dictionary<string, string> { ["invoiceNumber"] = invoice.InvoiceNumber ?? string.Empty, ["customerName"] = draftCustomerName })
        {
            CustomerId = invoice.CustomerId,
            LinkPath = $"/invoices/{invoice.Id}",
            InAppMessage = $"Conceptfactuur {invoice.InvoiceNumber} voor {draftCustomerName} is klaar.",
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

        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDate = request.DueDate < request.InvoiceDate ? request.InvoiceDate : request.DueDate;
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
                .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == invoice.CustomerId)
                .Select(c => new { c.VatTreatment, c.VatNumber })
                .FirstOrDefaultAsync(cancellationToken);
            if (snapshotCustomer is not null)
            {
                ApplySnapshots(invoice, snapshotEntity, snapshotCustomer.VatTreatment, snapshotCustomer.VatNumber);
            }
        }

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
                existing.VatRatePercent = line.VatRatePercent;
                existing.UnitCode = NormalizeUnitCode(line.UnitCode ?? existing.UnitCode);
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
                    VatRatePercent = line.VatRatePercent,
                    SalesCategoryId = line.SalesCategoryId,
                    UnitCode = NormalizeUnitCode(line.UnitCode),
                });
            }
        }

        // Client-generated ids: mark Added explicitly (navigation discovery would attach as Modified).
        _dbContext.AddRange(newLines);
        invoice.Lines = invoice.Lines.Where(l => keptIds.Contains(l.Id)).Concat(newLines).ToList();

        if (periodChanged)
        {
            var oldPeriod = new { invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth, invoice.InvoiceNumber };
            invoice.InvoicePeriodYear = newPeriodYear;
            invoice.InvoicePeriodMonth = newPeriodMonth;

            var legalEntity = invoice.LegalEntityId is { } entityId
                ? await _dbContext.LegalEntities.FirstOrDefaultAsync(
                    e => e.TenantId == _tenantContext.TenantId && e.Id == entityId, cancellationToken)
                : null;

            if (legalEntity is not null && !invoice.NumberIsManual)
            {
                // The old number is abandoned, never reused: sequences only move forward.
                await _numberService.ClaimAsync(legalEntity, newPeriodYear, newPeriodMonth,
                    number => invoice.InvoiceNumber = number, cancellationToken);
            }
            else
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "PeriodChanged", oldPeriod,
                new { invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth, invoice.InvoiceNumber }, cancellationToken);
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
            // Fail-safe: sending requires a valid, active, same-tenant issuing entity. Only
            // a tenant with NO entities configured at all (pre-seed legacy) is exempt.
            var entityValid = invoice.LegalEntityId is { } entityId
                ? await _dbContext.LegalEntities.AnyAsync(
                    e => e.TenantId == _tenantContext.TenantId && e.Id == entityId && e.IsActive, cancellationToken)
                : !await _dbContext.LegalEntities.AnyAsync(
                    e => e.TenantId == _tenantContext.TenantId, cancellationToken);
            if (!entityValid)
            {
                return InvoiceOperationResult.InvalidState(
                    "Deze factuur heeft geen geldige facturerende entiteit en kan niet worden verzonden.");
            }

            // Hard entity gate (also covers drafts that predate the create-time validation):
            // an order-backed line whose order carries a different issuing entity blocks Send.
            var lineOrderIds = invoice.Lines
                .Where(l => l.TransportOrderId is not null)
                .Select(l => l.TransportOrderId!.Value)
                .Distinct()
                .ToList();
            if (lineOrderIds.Count > 0)
            {
                var mismatchedOrders = await _dbContext.TransportOrders
                    .Where(o => o.TenantId == _tenantContext.TenantId && lineOrderIds.Contains(o.Id)
                                && o.LegalEntityId != null && o.LegalEntityId != invoice.LegalEntityId)
                    .Select(o => o.OrderNumber)
                    .ToListAsync(cancellationToken);
                if (mismatchedOrders.Count > 0)
                {
                    return InvoiceOperationResult.InvalidState(
                        $"De facturerende entiteit wijkt af van die van opdracht(en) {string.Join(", ", mismatchedOrders)}. " +
                        "Corrigeer de factuur of de opdrachten voor verzending.");
                }
            }

            var customerChecks = await _dbContext.Customers
                .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == invoice.CustomerId)
                .Select(c => new { c.PurchaseOrderPolicy, c.VatTreatment, c.VatNumber })
                .FirstOrDefaultAsync(cancellationToken);

            // PO policy: a Required customer blocks sending without an effective PO number.
            if (customerChecks?.PurchaseOrderPolicy == Partners.Entities.PurchaseOrderPolicy.Required
                && string.IsNullOrWhiteSpace(invoice.PurchaseOrderNumber))
            {
                return InvoiceOperationResult.InvalidState(
                    "Deze klant vereist een PO-nummer. Voeg een geldig PO-nummer toe voor verzending.");
            }

            // Coherent VAT model: treatments like reverse charge / intra-community require
            // the customer's VAT number on the invoice.
            if (customerChecks is not null
                && Partners.Services.VatTreatmentCatalog.Resolve(customerChecks.VatTreatment).RequiresVatNumber
                && string.IsNullOrWhiteSpace(invoice.CustomerVatNumberSnapshot ?? customerChecks.VatNumber))
            {
                return InvoiceOperationResult.InvalidState(
                    "Deze btw-regeling vereist een BTW-nummer van de klant. Vul het BTW-nummer aan voor verzending.");
            }
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

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "StatusChanged", before,
            new { invoice.Status }, cancellationToken);

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
            new { invoice.InvoiceNumber, invoice.Status }, null, cancellationToken);

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
            return InvoiceOperationResult.InvalidState("Alleen het nummer van een conceptfactuur kan worden gecorrigeerd.");
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

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var previewYear = year ?? today.Year;
        var previewMonth = Math.Clamp(month ?? today.Month, 1, 12);
        var number = await _numberService.PreviewAsync(legalEntity, previewYear, previewMonth, cancellationToken);
        return new InvoiceNumberPreviewDto(number, legalEntity.Id, previewYear, previewMonth);
    }

    /// <summary>Freezes seller + customer fiscal facts on the invoice (refreshed while Draft only).</summary>
    private static void ApplySnapshots(Invoice invoice, LegalEntity? entity, VatTreatment treatment, string? customerVatNumber)
    {
        if (entity is not null)
        {
            invoice.SellerName = entity.TradingName ?? entity.LegalName;
            invoice.SellerVatNumber = entity.VatNumber;
            invoice.SellerIban = entity.Iban;
            var streetPart = string.Join(' ', new[] { entity.Street, entity.HouseNumber }.Where(v => !string.IsNullOrWhiteSpace(v)));
            var cityPart = string.Join(' ', new[] { entity.PostalCode, entity.City }.Where(v => !string.IsNullOrWhiteSpace(v)));
            var line = string.Join(", ", new[] { streetPart, cityPart }.Where(v => !string.IsNullOrWhiteSpace(v)));
            invoice.SellerAddressLine = string.IsNullOrWhiteSpace(line) ? null : line;
        }

        invoice.CustomerVatTreatment = treatment.ToString();
        invoice.CustomerVatNumberSnapshot = customerVatNumber;
        invoice.VatLegalText = Partners.Services.VatTreatmentCatalog.Resolve(treatment).InvoiceLegalText;
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
    /// Mirrors an order-status release (Invoiced → Completed) on its pricing snapshot: an
    /// invoice cancellation/delete/line-drop must give the price back a way out of the
    /// terminal Invoiced state, or it can never be corrected (spec ch. 24-26). No-op when an
    /// order carries no snapshot, or its snapshot isn't Invoiced (e.g. was never priced).
    /// </summary>
    private async Task ReleasePricingSnapshotsAsync(IReadOnlyList<Guid> orderIds, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0)
        {
            return;
        }

        var snapshots = await _dbContext.TransportOrderPricingSnapshots
            .Where(s => s.TenantId == _tenantContext.TenantId && orderIds.Contains(s.TransportOrderId)
                        && s.Status == Modules.Orders.Entities.OrderPricingStatus.Invoiced)
            .ToListAsync(cancellationToken);
        foreach (var snapshot in snapshots)
        {
            snapshot.Status = Modules.Orders.Entities.OrderPricingStatus.Locked;
        }
    }

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

        var categories = await _dbContext.SalesCategories.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && categoryIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id, c.Name, c.LedgerAccountId,
                AccountNumber = (string?)c.LedgerAccount!.AccountNumber,
                AccountName = (string?)c.LedgerAccount.Name,
                c.VatCategoryOverride,
            })
            .ToDictionaryAsync(c => c.Id, cancellationToken);

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

            line.SalesCategoryNameSnapshot = category.Name;
            line.LedgerAccountId = category.LedgerAccountId;
            line.LedgerAccountNumberSnapshot = category.AccountNumber;
            line.LedgerAccountNameSnapshot = category.AccountName;
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

    public async Task<InvoiceOperationResult> CreateCreditNoteAsync(Guid id, CancellationToken cancellationToken)
    {
        var original = await TenantScoped().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (original is null)
        {
            return InvoiceOperationResult.NotFound;
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
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

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
            DueDate = today.AddDays(settings?.PaymentTermDays ?? 30),
            Currency = original.Currency,
            PurchaseOrderNumber = original.PurchaseOrderNumber,
            LanguageCode = original.LanguageCode,
            Notes = $"Creditnota voor factuur {original.InvoiceNumber}.",
            // Snapshot copy FROM the credited document, never from live master data: the credit
            // note must mirror exactly what it credits.
            SellerName = original.SellerName,
            SellerVatNumber = original.SellerVatNumber,
            SellerIban = original.SellerIban,
            SellerAddressLine = original.SellerAddressLine,
            CustomerVatTreatment = original.CustomerVatTreatment,
            CustomerVatNumberSnapshot = original.CustomerVatNumberSnapshot,
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

        _dbContext.Add(creditNote);

        var legalEntity = original.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.FirstOrDefaultAsync(
                e => e.TenantId == tenantId && e.Id == entityId, cancellationToken)
            : null;
        if (legalEntity is not null)
        {
            // Same monthly sequence as invoices, distinguished by the credit-note prefix
            // (CreditNotePrefix, else InvoicePrefix + "CN"). A format without {PREFIX} gets it
            // prepended so the prefix can never silently disappear.
            var format = legalEntity.InvoiceNumberFormat.Contains("{PREFIX}", StringComparison.Ordinal)
                ? legalEntity.InvoiceNumberFormat
                : "{PREFIX}" + legalEntity.InvoiceNumberFormat;
            var numberingEntity = new LegalEntity
            {
                Id = legalEntity.Id,
                InvoiceNumberFormat = format,
                InvoiceSequencePadding = legalEntity.InvoiceSequencePadding,
                InvoicePrefix = legalEntity.CreditNotePrefix ?? $"{legalEntity.InvoicePrefix}CN",
            };
            await _numberService.ClaimAsync(numberingEntity, today.Year, today.Month,
                number => creditNote.InvoiceNumber = number, cancellationToken);
        }
        else
        {
            var writableSettings = await _dbContext.TenantSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, writableSettings,
                () => creditNote.InvoiceNumber = $"CN{GenerateInvoiceNumber(writableSettings)}",
                cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, creditNote.Id.ToString(), "CreditNoteCreated",
            new { CreditedInvoiceNumber = original.InvoiceNumber },
            new { creditNote.InvoiceNumber, LineCount = creditNote.Lines.Count }, cancellationToken);
        await _auditService.RecordAsync(EntityType, original.Id.ToString(), "Credited",
            null, new { CreditNoteNumber = creditNote.InvoiceNumber, CreditNoteId = creditNote.Id }, cancellationToken);

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
        await FreezeLedgerSnapshotsAsync(invoice, InvoiceLineMirror.MirroredIds(invoice), cancellationToken);
        var missingAfter = invoice.Lines.Count(l => !l.IsDeleted && l.LedgerAccountNumberSnapshot is null);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "LedgerSnapshotsCompleted",
            new { MissingSnapshots = missingBefore }, new { MissingSnapshots = missingAfter }, cancellationToken);

        return InvoiceOperationResult.Success(await MapDetailAsync(invoice, cancellationToken));
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
                Math.Round(l.Quantity * l.UnitPrice, 2),
                l.SalesCategoryId, categoryName, accountNumber, accountName, warning,
                l.UnitCode,
                // Frozen after Send; live-derived while Draft so the preview shows what WILL freeze.
                l.VatCategoryCode ?? Partners.Services.VatTreatmentCatalog.ResolveVatCategory(mappedTreatment, l.VatRatePercent).Code,
                fiscalTreatment, fiscalSource, fiscalLegalText, salesCode,
                // Draft: what Send will freeze (same rule as Send and the draft PDF); frozen: as stored.
                frozen ? l.Description : InvoiceLineDescriptions.CustomerFacing(l.Description, live, invoice.LanguageCode));
        }).ToList();

        var subtotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        var vat = InvoiceTotals.VatTotal(liveLines, invoice.CustomerVatTreatment);

        var legalEntityName = invoice.LegalEntityId is { } entityId
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
        var creditNotes = invoice.Kind == InvoiceKind.Invoice
            ? await _dbContext.Invoices.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.CreditedInvoiceId == invoice.Id)
                .OrderBy(i => i.InvoiceDate).ThenBy(i => i.InvoiceNumber)
                .Select(i => new InvoiceReferenceDto(i.Id, i.InvoiceNumber, i.Status))
                .ToListAsync(cancellationToken)
            : [];

        return new InvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DueDate,
            invoice.CustomerId, customer?.Name ?? string.Empty, customer?.VatNumber,
            invoice.Status, invoice.Currency, invoice.Notes,
            lines, subtotal, vat, subtotal + vat, Transitions[invoice.Status],
            invoice.LegalEntityId, legalEntityName,
            invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth, invoice.NumberIsManual,
            invoice.PurchaseOrderNumber,
            invoice.Kind, invoice.CreditedInvoiceId, creditedInvoiceNumber, invoice.PaymentReference,
            invoice.CustomerVatTreatment, invoice.LanguageCode,
            Partners.Services.VatTreatmentCatalog.Resolve(mappedTreatment).InvoiceLegalText,
            creditNotes);
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
