using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Invoicing.Dtos;
using TransportationService.Api.Modules.Invoicing.Entities;
using TransportationService.Api.Modules.Orders.Entities;
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

    public InvoiceService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
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
                o.Id, o.OrderNumber, o.OrderDate, o.GoodsDescription,
                orderStops.FirstOrDefault(s => s.StopType == StopType.Loading)?.City,
                orderStops.LastOrDefault(s => s.StopType == StopType.Unloading)?.City,
                o.AgreedPrice);
        }).ToList();
    }

    public async Task<InvoiceOperationResult> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (!await _dbContext.Customers.AnyAsync(
                c => c.Id == request.CustomerId && c.TenantId == tenantId, cancellationToken))
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
        var vatRate = settings?.DefaultVatRatePercent ?? 21m;
        var invoiceDate = request.InvoiceDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = request.CustomerId,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(settings?.PaymentTermDays ?? 30),
            Currency = settings?.DefaultCurrency ?? "EUR",
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

        _dbContext.Add(invoice);
        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => invoice.InvoiceNumber = GenerateInvoiceNumber(settings),
            cancellationToken);

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

        invoice.InvoiceDate = request.InvoiceDate;
        invoice.DueDate = request.DueDate < request.InvoiceDate ? request.InvoiceDate : request.DueDate;
        invoice.Notes = Trim(request.Notes);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

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

        return new InvoiceDetailDto(
            invoice.Id, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DueDate,
            invoice.CustomerId, customer?.Name ?? string.Empty, customer?.VatNumber,
            invoice.Status, invoice.Currency, invoice.Notes,
            lines, subtotal, vat, subtotal + vat, Transitions[invoice.Status]);
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

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
