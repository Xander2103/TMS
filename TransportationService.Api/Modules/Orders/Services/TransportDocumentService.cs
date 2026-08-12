using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Orders.Services;

/// <summary>One order row in the customer/day document preview.</summary>
public record CustomerDayDocumentRowDto(
    Guid OrderId, string OrderNumber, string? UnloadingCity,
    string? Kind, string Source, string Reason,
    bool UsesCustomerDocument, bool NoneRequired, bool Undecided);

/// <summary>Follow-up wave P3: the end-of-day preview for one customer + delivery date.</summary>
public record CustomerDayDocumentsPreviewDto(
    DateOnly Date, int TotalOrders, int OwnDeliveryNotes, int OwnCmrs,
    int CustomerDocuments, int NoneRequired, int Undecided,
    IReadOnlyList<CustomerDayDocumentRowDto> Rows);

/// <summary>The resolved document decision for one order, as shown in the UI.</summary>
public record OrderDocumentStrategyDto(
    string? Kind, bool UsesCustomerDocument, bool NoneRequired, bool Undecided,
    string Source, string Reason, string? OrderPreference, string CustomerStrategy);

public interface ITransportDocumentService
{
    /// <summary>Null when the order does not exist in the tenant.</summary>
    Task<(byte[] Content, string FileName)?> RenderAsync(Guid orderId, string kind, CancellationToken cancellationToken);

    /// <summary>Merged batch for every order on the trip (route order); null = unknown trip.
    /// Orders whose document strategy says "customer document" or "no document" are excluded.</summary>
    Task<(byte[] Content, string FileName)?> RenderTripBatchAsync(Guid tripId, string kind, CancellationToken cancellationToken);

    /// <summary>The resolved document decision for one order; null = unknown order.</summary>
    Task<OrderDocumentStrategyDto?> GetStrategyAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>P3: preview of the customer's deliveries on a date and what each one needs.</summary>
    Task<CustomerDayDocumentsPreviewDto?> PreviewCustomerDayAsync(
        Guid customerId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>P3: merged batch of the customer's own documents of one kind for a date.
    /// Optional <paramref name="orderIds"/> narrows to a selection; strategy exclusions always apply.</summary>
    Task<(byte[] Content, string FileName)?> RenderCustomerDayBatchAsync(
        Guid customerId, DateOnly date, string kind, IReadOnlyList<Guid>? orderIds, CancellationToken cancellationToken);
}

/// <summary>
/// Wave 9: assembles delivery-note/CMR snapshots from frozen order data (seller = the order's
/// issuing entity, else the tenant default) and renders via the PDFsharp house renderer. Batch
/// = one merged PDF per trip, in route order — the "print everything for this trip" run.
/// Follow-up wave P1-P3: every batch consults the DocumentStrategyResolver (customer strategy,
/// order override, tenant rules), and the customer/day batch is the end-of-day print run.
/// </summary>
public class TransportDocumentService : ITransportDocumentService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public TransportDocumentService(TransportationDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    public async Task<(byte[] Content, string FileName)?> RenderAsync(
        Guid orderId, string kind, CancellationToken cancellationToken)
    {
        var snapshot = await BuildSnapshotAsync(orderId, NormalizeKind(kind), cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var prefix = snapshot.Kind == "Cmr" ? "cmr" : "leveringsbon";
        return (TransportDocumentRenderer.Render(snapshot), $"{prefix}-{snapshot.OrderNumber}.pdf");
    }

    public async Task<(byte[] Content, string FileName)?> RenderTripBatchAsync(
        Guid tripId, string kind, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var trip = await _dbContext.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var normalized = NormalizeKind(kind);
        var snapshots = new List<TransportDocumentSnapshot>();
        foreach (var tripOrder in trip.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Sequence))
        {
            // The batch only prints orders that need an OWN document; a customer-document or
            // no-document order is silently skipped (the preview endpoints explain why).
            var decision = await ResolveDecisionAsync(tripOrder.TransportOrderId, cancellationToken);
            if (decision is null || decision.UsesCustomerDocument || decision.NoneRequired)
            {
                continue;
            }

            if (await BuildSnapshotAsync(tripOrder.TransportOrderId, normalized, cancellationToken) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        var prefix = normalized == "Cmr" ? "cmr" : "leveringsbonnen";
        return (TransportDocumentRenderer.RenderBatch(snapshots), $"{prefix}-{trip.TripNumber}.pdf");
    }

    public async Task<OrderDocumentStrategyDto?> GetStrategyAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var customerStrategy = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == order.CustomerId)
            .Select(c => c.DocumentStrategy)
            .FirstOrDefaultAsync(cancellationToken) ?? "GenerateOwn";
        var decision = await ResolveAsync(order, customerStrategy, cancellationToken);
        return new OrderDocumentStrategyDto(
            decision.Kind, decision.UsesCustomerDocument, decision.NoneRequired, decision.Undecided,
            decision.Source, decision.Reason, order.DocumentPreference, customerStrategy);
    }

    public async Task<CustomerDayDocumentsPreviewDto?> PreviewCustomerDayAsync(
        Guid customerId, DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var customer = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var orders = await MatchDayOrdersAsync(customerId, date, cancellationToken);
        var rows = new List<CustomerDayDocumentRowDto>();
        foreach (var order in orders)
        {
            var decision = await ResolveAsync(order, customer.DocumentStrategy, cancellationToken);
            rows.Add(new CustomerDayDocumentRowDto(
                order.Id, order.OrderNumber,
                order.Stops.Where(s => !s.IsDeleted && s.StopType == StopType.Unloading)
                    .OrderBy(s => s.Sequence).Select(s => s.City).LastOrDefault(),
                decision.Kind, decision.Source, decision.Reason,
                decision.UsesCustomerDocument, decision.NoneRequired, decision.Undecided));
        }

        return new CustomerDayDocumentsPreviewDto(
            date, rows.Count,
            rows.Count(r => r.Kind == DocumentStrategyResolver.KindDeliveryNote && !r.UsesCustomerDocument && !r.NoneRequired && !r.Undecided),
            rows.Count(r => r.Kind == DocumentStrategyResolver.KindCmr && !r.UsesCustomerDocument && !r.NoneRequired && !r.Undecided),
            rows.Count(r => r.UsesCustomerDocument),
            rows.Count(r => r.NoneRequired),
            rows.Count(r => r.Undecided),
            rows);
    }

    public async Task<(byte[] Content, string FileName)?> RenderCustomerDayBatchAsync(
        Guid customerId, DateOnly date, string kind, IReadOnlyList<Guid>? orderIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var customer = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var normalized = NormalizeKind(kind);
        var orders = await MatchDayOrdersAsync(customerId, date, cancellationToken);
        var snapshots = new List<TransportDocumentSnapshot>();
        foreach (var order in orders)
        {
            if (orderIds is { Count: > 0 } && !orderIds.Contains(order.Id))
            {
                continue;
            }

            var decision = await ResolveAsync(order, customer.DocumentStrategy, cancellationToken);
            if (!decision.GeneratesOwnDocument || decision.Kind != normalized)
            {
                continue;
            }

            if (await BuildSnapshotAsync(order.Id, normalized, cancellationToken) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        var prefix = normalized == "Cmr" ? "cmr" : "leveringsbonnen";
        var safeName = customer.CustomerNumber ?? customer.Id.ToString("N")[..8];
        return (TransportDocumentRenderer.RenderBatch(snapshots), $"{prefix}-{safeName}-{date:yyyyMMdd}.pdf");
    }

    /// <summary>
    /// The customer's deliveries "of a date": orders on a non-cancelled trip dated that day,
    /// else (unplanned) orders whose last unloading stop is requested that day, else orders
    /// simply dated that day — the same ordering a dispatcher reasons in.
    /// </summary>
    private async Task<List<TransportOrder>> MatchDayOrdersAsync(
        Guid customerId, DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var orders = await _dbContext.TransportOrders.AsNoTracking()
            .Include(o => o.Stops)
            .Where(o => o.TenantId == tenantId && o.CustomerId == customerId
                        && o.Status != TransportOrderStatus.Cancelled && o.Status != TransportOrderStatus.Draft)
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToList();
        var tripDates = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && orderIds.Contains(to.TransportOrderId) && !to.IsDeleted)
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == tenantId
                    && t.Status != Modules.Planning.Entities.TripStatus.Cancelled),
                to => to.TripId, t => t.Id, (to, t) => new { to.TransportOrderId, t.TripDate })
            .ToListAsync(cancellationToken);
        var tripDateByOrder = tripDates
            .GroupBy(x => x.TransportOrderId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.TripDate));

        return orders
            .Where(o =>
            {
                if (tripDateByOrder.TryGetValue(o.Id, out var tripDate))
                {
                    return tripDate == date;
                }

                var requested = o.Stops.Where(s => !s.IsDeleted && s.StopType == StopType.Unloading)
                    .OrderBy(s => s.Sequence)
                    .Select(s => s.RequestedFrom ?? s.RequestedTo)
                    .LastOrDefault(d => d is not null);
                return requested is { } r ? DateOnly.FromDateTime(r) == date : o.OrderDate == date;
            })
            .OrderBy(o => o.OrderNumber)
            .ToList();
    }

    private async Task<DocumentStrategyResolver.Decision?> ResolveDecisionAsync(
        Guid orderId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var customerStrategy = await _dbContext.Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == order.CustomerId)
            .Select(c => c.DocumentStrategy)
            .FirstOrDefaultAsync(cancellationToken) ?? "GenerateOwn";
        return await ResolveAsync(order, customerStrategy, cancellationToken);
    }

    private async Task<DocumentStrategyResolver.Decision> ResolveAsync(
        TransportOrder order, string customerStrategy, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var countries = order.Stops.Where(s => !s.IsDeleted)
            .Select(s => s.CountryCode?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();
        var crossBorder = countries.Count > 1;

        var activityTypeId = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == order.Id)
            .OrderBy(a => a.Sequence)
            .Select(a => (Guid?)a.ActivityTypeId)
            .FirstOrDefaultAsync(cancellationToken);

        var rules = await _dbContext.TenantDocumentRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        return DocumentStrategyResolver.Resolve(
            order.DocumentPreference, customerStrategy, crossBorder, order.AdrRequired, activityTypeId, rules);
    }

    private static string NormalizeKind(string kind) =>
        string.Equals(kind, "cmr", StringComparison.OrdinalIgnoreCase) ? "Cmr" : "DeliveryNote";

    private async Task<TransportDocumentSnapshot?> BuildSnapshotAsync(
        Guid orderId, string kind, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var order = await _dbContext.TransportOrders.AsNoTracking()
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == order.CustomerId, cancellationToken);
        var entity = order.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == entityId, cancellationToken)
            : await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.IsActive)
                .OrderByDescending(e => e.IsDefault)
                .FirstOrDefaultAsync(cancellationToken);

        var cargo = await _dbContext.CargoItems.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.TransportOrderId == orderId && !c.IsDeleted)
            .OrderBy(c => c.Sequence)
            .ToListAsync(cancellationToken);
        var lines = cargo.Count > 0
            ? cargo.Select(c => new TransportDocumentLine(
                    string.IsNullOrWhiteSpace(c.Description) ? "Goederen" : c.Description!,
                    c.ExpectedQuantity, c.QuantityUnitCode ?? c.QuantityUnit, c.TotalWeightKg))
                .ToList()
            : [new TransportDocumentLine(
                order.GoodsDescription ?? "Goederen", order.Quantity ?? 1m,
                order.QuantityUnitCode ?? order.QuantityUnit, order.WeightKg)];

        static string? Address(string? street, string? number, string? postal, string? city) =>
            string.Join(" ", new[] { street, number, postal, city }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } joined
                ? joined
                : null;

        return new TransportDocumentSnapshot(
            kind, order.OrderNumber, order.OrderDate,
            new TransportDocumentParty(
                entity?.LegalName ?? "—",
                Address(entity?.Street, entity?.HouseNumber, entity?.PostalCode, entity?.City),
                entity?.VatNumber),
            new TransportDocumentParty(
                customer?.Name ?? "—",
                Address(customer?.Street, customer?.HouseNumber, customer?.PostalCode, customer?.City),
                customer?.VatNumber),
            order.Stops.Where(s => !s.IsDeleted).OrderBy(s => s.Sequence)
                .Select(s => new TransportDocumentStop(
                    s.StopType == StopType.Loading ? "Laden" : "Lossen",
                    s.LocationName ?? s.City,
                    Address(s.Address, null, s.PostalCode, s.City),
                    s.Reference))
                .ToList(),
            lines,
            order.WeightKg ?? (cargo.Count > 0 ? cargo.Sum(c => c.TotalWeightKg ?? 0m) : null),
            order.CustomerReference,
            order.Notes);
    }
}
