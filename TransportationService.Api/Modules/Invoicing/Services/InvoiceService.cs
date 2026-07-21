using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Invoicing.Services;

public class InvoiceService : IInvoiceService
{
    private const string EntityType = "Invoice";

    private static readonly IReadOnlyDictionary<InvoiceStatus, InvoiceStatus[]> Transitions =
        new Dictionary<InvoiceStatus, InvoiceStatus[]>
        {
            [InvoiceStatus.Draft] = [InvoiceStatus.Sent, InvoiceStatus.Cancelled],
            [InvoiceStatus.Sent] = [InvoiceStatus.Paid, InvoiceStatus.Cancelled],
            [InvoiceStatus.Paid] = [],
            [InvoiceStatus.Cancelled] = [],
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IInvoiceNumberService _numberService;
    private readonly Partners.Services.ICustomerBillingConfigService _billingConfig;

    public InvoiceService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IInvoiceNumberService numberService,
        Partners.Services.ICustomerBillingConfigService billingConfig)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _numberService = numberService;
        _billingConfig = billingConfig;
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
            .Select(x => new { x.i.Id, x.i.InvoiceNumber, x.i.InvoiceDate, x.i.DueDate, x.i.CustomerId, x.CustomerName, x.i.Status, x.i.Currency })
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
            var subtotal = Math.Round(invoiceLines.Sum(l => l.Quantity * l.UnitPrice), 2);
            var vat = Math.Round(invoiceLines.Sum(l => l.Quantity * l.UnitPrice * l.VatRatePercent / 100m), 2);
            return new InvoiceListItemDto(
                r.Id, r.InvoiceNumber, r.InvoiceDate, r.DueDate, r.CustomerId, r.CustomerName,
                r.Status, r.Currency, subtotal, vat, subtotal + vat, invoiceLines.Count);
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
            .Select(o => new { o.Id, o.OrderNumber, o.OrderDate, o.GoodsDescription, o.AgreedPrice })
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
                o.AgreedPrice);
        }).ToList();
    }

    public async Task<InvoiceOperationResult> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var customerVat = await _dbContext.Customers
            .Where(c => c.Id == request.CustomerId && c.TenantId == tenantId)
            .Select(c => new { c.VatTreatment, c.DefaultVatRatePercent, c.DefaultLegalEntityId, c.VatNumber })
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

        // Orders: completed, of this customer, not yet on a live invoice.
        List<UninvoicedOrderDto> orderDtos = [];
        if (request.OrderIds.Count > 0)
        {
            var candidates = await ListUninvoicedOrdersAsync(request.CustomerId, cancellationToken);
            var byId = candidates.ToDictionary(o => o.Id);
            foreach (var orderId in request.OrderIds)
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
        var vatRate = customerVat.DefaultVatRatePercent
            ?? (customerVat.VatTreatment == VatTreatment.DomesticVat
                ? settings?.DefaultVatRatePercent ?? 21m
                : 0m);
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
        };

        var sequence = 1;
        foreach (var order in orderDtos)
        {
            var route = order.FirstLoadingCity is not null || order.LastUnloadingCity is not null
                ? $" ({order.FirstLoadingCity ?? "?"} → {order.LastUnloadingCity ?? "?"})"
                : string.Empty;
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TransportOrderId = order.Id,
                Sequence = sequence++,
                Description = $"{order.OrderNumber} — {order.GoodsDescription}{route}",
                Quantity = 1m,
                UnitPrice = order.AgreedPrice ?? 0m,
                VatRatePercent = vatRate,
            });
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
                VatRatePercent = manual.VatRatePercent ?? vatRate,
            });
        }

        // Claim the invoice number and flip the orders to Invoiced in the same save.
        var orders = orderDtos.Count == 0
            ? []
            : await _dbContext.TransportOrders
                .Where(o => o.TenantId == tenantId && request.OrderIds.Contains(o.Id))
                .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.Status = TransportOrderStatus.Invoiced;
        }

        // Diesel surcharge: customer config (order overrides respected) → separate lines.
        var surchargeConfig = await _dbContext.CustomerDieselSurcharges
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.CustomerId == request.CustomerId, cancellationToken);
        if (orderDtos.Count > 0 && DieselSurchargeCalculator.Applies(surchargeConfig, invoiceDate))
        {
            var overridesByOrder = orders.ToDictionary(
                o => o.Id, o => o.DieselSurchargeOverride ? o.DieselSurchargePercentOverride : null);
            var bases = orderDtos
                .Select(o => new DieselSurchargeCalculator.OrderBase(
                    o.Id, o.OrderNumber, o.AgreedPrice ?? 0m, overridesByOrder.GetValueOrDefault(o.Id)))
                .ToList();
            foreach (var surchargeLine in DieselSurchargeCalculator.BuildLines(surchargeConfig!, bases, invoiceDate))
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
                    VatRatePercent = vatRate,
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

        // While Draft, snapshots track current master data; Sent freezes them for good.
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

        if (!Transitions[invoice.Status].Contains(target))
        {
            return InvoiceOperationResult.InvalidState($"Een factuur met status '{invoice.Status}' kan niet naar '{target}'.");
        }

        if (target == InvoiceStatus.Sent)
        {
            // Fail-safe: sending requires a valid, active, same-tenant issuing entity.
            var entityValid = invoice.LegalEntityId is { } entityId
                && await _dbContext.LegalEntities.AnyAsync(
                    e => e.TenantId == _tenantContext.TenantId && e.Id == entityId && e.IsActive, cancellationToken);
            if (!entityValid)
            {
                return InvoiceOperationResult.InvalidState(
                    "Deze factuur heeft geen geldige facturerende entiteit en kan niet worden verzonden.");
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

        if (target == InvoiceStatus.Cancelled)
        {
            await ReleaseOrdersAsync(invoice, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, invoice.Id.ToString(), "StatusChanged", before,
            new { invoice.Status }, cancellationToken);

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

        var lines = liveLines.Select(l => new InvoiceLineDto(
            l.Id, l.Sequence, l.TransportOrderId,
            l.TransportOrderId is { } oid ? orderNumbers.GetValueOrDefault(oid) : null,
            l.Description, l.Quantity, l.UnitPrice, l.VatRatePercent,
            Math.Round(l.Quantity * l.UnitPrice, 2))).ToList();

        var subtotal = Math.Round(lines.Sum(l => l.LineTotal), 2);
        var vat = Math.Round(liveLines.Sum(l => l.Quantity * l.UnitPrice * l.VatRatePercent / 100m), 2);

        var legalEntityName = invoice.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Id == entityId)
                .Select(e => e.TradingName ?? e.LegalName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new InvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DueDate,
            invoice.CustomerId, customer?.Name ?? string.Empty, customer?.VatNumber,
            invoice.Status, invoice.Currency, invoice.Notes,
            lines, subtotal, vat, subtotal + vat, Transitions[invoice.Status],
            invoice.LegalEntityId, legalEntityName,
            invoice.InvoicePeriodYear, invoice.InvoicePeriodMonth, invoice.NumberIsManual,
            invoice.PurchaseOrderNumber);
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
}
