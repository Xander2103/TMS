using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
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
    IReadOnlyList<CustomerDayDocumentRowDto> Rows,
    /// <summary>D2: own work orders (werkbonnen) for on-site work.</summary>
    int OwnWorkOrders = 0);

/// <summary>The resolved document decision for one order, as shown in the UI.</summary>
public record OrderDocumentStrategyDto(
    string? Kind, bool UsesCustomerDocument, bool NoneRequired, bool Undecided,
    string Source, string Reason, string? OrderPreference, string CustomerStrategy);

public interface ITransportDocumentService
{
    /// <summary>Null when the order does not exist in the tenant.</summary>
    Task<(byte[] Content, string FileName)?> RenderAsync(Guid orderId, string kind, CancellationToken cancellationToken);

    /// <summary>
    /// MD-B1: the document model of an order AS IT STANDS NOW, for <paramref name="kind"/> (Cmr |
    /// DeliveryNote | WorkOrder) — what an issue freezes. Issued documents never call this to
    /// render: they render their frozen snapshot. Null = unknown order.
    /// </summary>
    Task<TransportDocumentSnapshot?> BuildCurrentSnapshotAsync(Guid orderId, string kind, CancellationToken cancellationToken);

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

        var prefix = snapshot.Kind switch
        {
            "Cmr" => "cmr",
            DocumentStrategyResolver.KindWorkOrder => "werkbon",
            _ => "leveringsbon",
        };
        return (TransportDocumentRenderer.Render(snapshot), $"{prefix}-{snapshot.OrderNumber}.pdf");
    }

    public Task<TransportDocumentSnapshot?> BuildCurrentSnapshotAsync(
        Guid orderId, string kind, CancellationToken cancellationToken) =>
        BuildSnapshotAsync(orderId, NormalizeKind(kind), cancellationToken);

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

            // D2: on-site work gets a work order and nothing else — it never rides along in a
            // delivery-note/CMR run, and a work-order run prints only on-site work.
            if ((decision.Kind == DocumentStrategyResolver.KindWorkOrder) != (normalized == DocumentStrategyResolver.KindWorkOrder))
            {
                continue;
            }

            if (await IssuedOrCurrentSnapshotAsync(tripOrder.TransportOrderId, normalized, cancellationToken) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        var prefix = BatchPrefix(normalized);
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
                // D2: on-site work has no unloading stop; its site is where the work is delivered.
                order.Stops.Where(s => !s.IsDeleted && s.StopType is StopType.Unloading or StopType.Site)
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
            rows,
            OwnWorkOrders: rows.Count(r => r.Kind == DocumentStrategyResolver.KindWorkOrder && !r.UsesCustomerDocument && !r.NoneRequired && !r.Undecided));
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

            if (await IssuedOrCurrentSnapshotAsync(order.Id, normalized, cancellationToken) is { } snapshot)
            {
                snapshots.Add(snapshot);
            }
        }

        var prefix = BatchPrefix(normalized);
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

        // C-03: the requested window is a UTC instant, but "the deliveries of a date" is a
        // TENANT-LOCAL calendar day. Truncating the raw instant moves every delivery before
        // 01:00/02:00 local onto the previous day and out of that day's document run.
        var zone = await TenantTimeZone.ForTenantAsync(_dbContext, tenantId, cancellationToken);

        return orders
            .Where(o =>
            {
                if (tripDateByOrder.TryGetValue(o.Id, out var tripDate))
                {
                    return tripDate == date;
                }

                // D2: an on-site order's "delivery" is its site stop (planned start when the
                // customer requested no window).
                var requested = o.Stops.Where(s => !s.IsDeleted && s.StopType is StopType.Unloading or StopType.Site)
                    .OrderBy(s => s.Sequence)
                    .Select(s => s.RequestedFrom ?? s.RequestedTo ?? (s.StopType == StopType.Site ? s.PlannedFrom : null))
                    .LastOrDefault(d => d is not null);
                return requested is { } r ? TenantTimeZone.ToLocalDate(r, zone) == date : o.OrderDate == date;
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
            order.DocumentPreference, customerStrategy, crossBorder, order.AdrRequired, activityTypeId, rules,
            onSiteWork: CraneJobRules.IsOnSiteWork(order.CraneJobKind));
    }

    /// <summary>
    /// MD-B1: a batch print run reprints an order's ACTIVE issued document of the kind exactly as
    /// issued (frozen snapshot, with its number); only an order without one gets a fresh,
    /// number-less document from the current data.
    /// </summary>
    private async Task<TransportDocumentSnapshot?> IssuedOrCurrentSnapshotAsync(
        Guid orderId, string normalizedKind, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var issuedKind = normalizedKind switch
        {
            "Cmr" => IssuedTransportDocumentKind.Cmr,
            DocumentStrategyResolver.KindWorkOrder => IssuedTransportDocumentKind.WorkOrder,
            _ => IssuedTransportDocumentKind.DeliveryNote,
        };
        var issued = await _dbContext.IssuedTransportDocuments.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.TransportOrderId == orderId && d.Kind == issuedKind
                        && d.ReplacedByDocumentId == null && d.SnapshotJson != null)
            .OrderByDescending(d => d.IssuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (issued is not null && FrozenTransportDocumentJson.TryDeserialize(issued.SnapshotJson, out var frozen))
        {
            return frozen.Document with
            {
                Reference = new TransportDocumentReference(issued.DocumentNumber, frozen.DossierNumber, issued.ExternalNumber),
            };
        }

        return await BuildSnapshotAsync(orderId, normalizedKind, cancellationToken);
    }

    private static string NormalizeKind(string kind) =>
        string.Equals(kind, "cmr", StringComparison.OrdinalIgnoreCase) ? "Cmr"
        : string.Equals(kind, "work-order", StringComparison.OrdinalIgnoreCase)
          || string.Equals(kind, DocumentStrategyResolver.KindWorkOrder, StringComparison.OrdinalIgnoreCase)
            ? DocumentStrategyResolver.KindWorkOrder
            : "DeliveryNote";

    private static string BatchPrefix(string normalizedKind) => normalizedKind switch
    {
        "Cmr" => "cmr",
        DocumentStrategyResolver.KindWorkOrder => "werkbonnen",
        _ => "leveringsbonnen",
    };

    /// <summary>The document model exactly as it is printed (also the seam the document tests assert on).</summary>
    public async Task<TransportDocumentSnapshot?> BuildSnapshotAsync(
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
        // D2: on-site work carries no goods, so the legacy "1 × Goederen" fallback line would be
        // invented data there — its document lists goods only when real goods lines exist.
        var onSiteWork = CraneJobRules.IsOnSiteWork(order.CraneJobKind);
        // A goods unit is a unit-type CODE ("EUROPALLET"): the document prints the tenant's unit
        // name ("Europallet"), like the screens do; a legacy free-text unit stays as typed.
        var unitCodes = cargo.Select(c => c.QuantityUnitCode).Append(order.QuantityUnitCode)
            .OfType<string>().Distinct().ToList();
        var unitNames = unitCodes.Count == 0
            ? new Dictionary<string, string>()
            : await _dbContext.UnitTypes.AsNoTracking()
                .Where(u => u.TenantId == tenantId && unitCodes.Contains(u.Code))
                .ToDictionaryAsync(u => u.Code, u => u.Name, cancellationToken);
        string? UnitText(string? code, string? legacy) =>
            code is not null ? (unitNames.TryGetValue(code, out var name) ? name : code) : legacy;
        List<TransportDocumentLine> lines = cargo.Count > 0
            ? cargo.Select(c => new TransportDocumentLine(
                    string.IsNullOrWhiteSpace(c.Description) ? "Goederen" : c.Description!,
                    c.ExpectedQuantity, UnitText(c.QuantityUnitCode, c.QuantityUnit), c.TotalWeightKg))
                .ToList()
            : onSiteWork || kind == DocumentStrategyResolver.KindWorkOrder
                ? []
                : [new TransportDocumentLine(
                    order.GoodsDescription ?? "Goederen", order.Quantity ?? 1m,
                    UnitText(order.QuantityUnitCode, order.QuantityUnit), order.WeightKg)];

        var workOrder = kind == DocumentStrategyResolver.KindWorkOrder
            ? await BuildWorkOrderAsync(order, cancellationToken)
            : null;

        var seller = new TransportDocumentParty(
            entity?.LegalName ?? "—",
            Address(entity?.Street, entity?.HouseNumber, entity?.PostalCode, entity?.City),
            entity?.VatNumber,
            Source: TransportDocumentPartySource.LegalEntity);
        var orderingParty = new TransportDocumentParty(
            customer?.Name ?? "—",
            Address(customer?.Street, customer?.HouseNumber, customer?.PostalCode, customer?.City),
            customer?.VatNumber,
            Source: TransportDocumentPartySource.OrderingCustomer);
        var consignment = await BuildConsignmentAsync(order, customer, seller, orderingParty, cancellationToken);

        return new TransportDocumentSnapshot(
            kind, order.OrderNumber, order.OrderDate,
            seller,
            orderingParty,
            order.Stops.Where(s => !s.IsDeleted).OrderBy(s => s.Sequence)
                .Select(s => new TransportDocumentStop(
                    s.StopType switch
                    {
                        StopType.Loading => "Laden",
                        StopType.Site => "Werf",
                        _ => "Lossen",
                    },
                    s.LocationName ?? s.City,
                    Address(s.Address, null, s.PostalCode, s.City),
                    s.Reference))
                .ToList(),
            lines,
            order.WeightKg ?? (cargo.Count > 0 ? cargo.Sum(c => c.TotalWeightKg ?? 0m) : null),
            order.CustomerReference,
            order.Notes,
            workOrder,
            Consignment: consignment);
    }

    private static string? Address(string? street, string? number, string? postal, string? city) =>
        string.Join(" ", new[] { street, number, postal, city }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } joined
            ? joined
            : null;

    /// <summary>
    /// The CMR parties of an order (T-H3, acceptance 2026-10-16). The order has no explicit
    /// shipper/consignee fields, so the parties come from its stops:
    /// <list type="bullet">
    /// <item>Sender (box 1) = the party of the FIRST loading stop; consignee (box 2) = the party of
    /// the LAST unloading stop — see <see cref="ResolveStopParty"/> for how a stop becomes a party.</item>
    /// <item>Carrier (box 16) = the issuing legal entity of the order (else the tenant default) —
    /// the transport company is the carrier, never the sender.</item>
    /// <item>Place of taking over (box 4) = loading stop address + its local date (confirmed, else
    /// planned, else requested window start, else the trip date); place of delivery (box 3) =
    /// unloading stop address.</item>
    /// <item>Registration (box 25) = vehicle + trailer of the order's most relevant non-cancelled trip
    /// (InProgress, Planned, Draft, Completed); none = null → a blank line on the document.</item>
    /// </list>
    /// </summary>
    private async Task<TransportDocumentConsignment> BuildConsignmentAsync(
        TransportOrder order, Customer? customer, TransportDocumentParty carrier, TransportDocumentParty orderingParty,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var stops = order.Stops.Where(s => !s.IsDeleted).OrderBy(s => s.Sequence).ToList();
        var loading = stops.FirstOrDefault(s => s.StopType == StopType.Loading);
        var unloading = stops.LastOrDefault(s => s.StopType == StopType.Unloading);

        // Owners of the master locations on the two stops — one query each, only when linked.
        var locationIds = new[] { loading?.LocationId, unloading?.LocationId }.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var locationOwners = locationIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == tenantId && locationIds.Contains(l.Id))
                .Select(l => new { l.Id, l.CustomerId })
                .ToDictionaryAsync(l => l.Id, l => l.CustomerId, cancellationToken);
        var ownerIds = locationOwners.Values.Where(id => id is not null && id != customer?.Id).Select(id => id!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0
            ? new Dictionary<Guid, Customer>()
            : await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && ownerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);
        if (customer is not null)
        {
            owners[customer.Id] = customer;
        }

        TransportDocumentParty PartyOf(TransportOrderStop? stop)
        {
            Guid? ownerId = stop?.LocationId is { } locationId ? locationOwners.GetValueOrDefault(locationId) : null;
            return ResolveStopParty(stop, ownerId is { } id ? owners.GetValueOrDefault(id) : null, customer, orderingParty);
        }

        // The trip the order rides on (same ranking as the dossier's effective assignment).
        var trips = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && to.TransportOrderId == order.Id && !to.IsDeleted)
            .Join(_dbContext.Trips.AsNoTracking().Where(t => t.TenantId == tenantId && t.Status != Modules.Planning.Entities.TripStatus.Cancelled),
                to => to.TripId, t => t.Id,
                (to, t) => new { t.Status, t.TripDate, t.TripNumber, t.VehicleId, t.TrailerId, t.DriverId })
            .ToListAsync(cancellationToken);
        var trip = trips
            .OrderBy(t => t.Status switch
            {
                Modules.Planning.Entities.TripStatus.InProgress => 0,
                Modules.Planning.Entities.TripStatus.Planned => 1,
                Modules.Planning.Entities.TripStatus.Draft => 2,
                _ => 3,
            })
            .ThenByDescending(t => t.TripDate).ThenBy(t => t.TripNumber)
            .FirstOrDefault();

        string? vehicleRegistration = null;
        string? trailerRegistration = null;
        string? driverName = null;
        if (trip?.VehicleId is { } vehicleId)
        {
            vehicleRegistration = await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == tenantId && v.Id == vehicleId)
                .Select(v => v.LicensePlate)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (trip?.TrailerId is { } trailerId)
        {
            trailerRegistration = await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.Id == trailerId)
                .Select(t => t.LicensePlate)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (trip?.DriverId is { } driverId)
        {
            driverName = await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.Id == driverId)
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id, (d, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        DateOnly? takingOverDate = null;
        if (loading is not null && (loading.ConfirmedFrom ?? loading.PlannedFrom ?? loading.RequestedFrom) is { } loadingAt)
        {
            var zone = await TenantTimeZone.ForTenantAsync(_dbContext, tenantId, cancellationToken);
            takingOverDate = TenantTimeZone.ToLocalDate(loadingAt, zone);
        }

        takingOverDate ??= trip?.TripDate;

        static TransportDocumentPlace? Place(TransportOrderStop? stop, DateOnly? date) => stop is null
            ? null
            : new TransportDocumentPlace(stop.LocationName, Address(stop.Address, null, stop.PostalCode, stop.City), stop.CountryCode, date);

        return new TransportDocumentConsignment(
            PartyOf(loading),
            PartyOf(unloading),
            carrier,
            Place(loading, takingOverDate),
            Place(unloading, null),
            Blank(vehicleRegistration),
            Blank(trailerRegistration),
            Blank(driverName?.Trim()));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// How a stop becomes a CMR party, in this order:
    /// <list type="number">
    /// <item>The stop references a master location that BELONGS to a customer
    /// (<c>Location.CustomerId</c>): that customer is the party — its name and VAT, the location name
    /// as detail line (when it adds something) and the STOP address, where the goods physically are.
    /// When the owner is the ordering customer, the ordering customer is therefore the party.</item>
    /// <item>The stop carries a name of its own (free address or an unowned location): that name and
    /// the stop address — unless the address IS the ordering customer's registered address, which
    /// makes the ordering customer the party.</item>
    /// <item>No stop, or a stop without a name: the ordering customer (name, registered address, VAT).</item>
    /// </list>
    /// </summary>
    public static TransportDocumentParty ResolveStopParty(
        TransportOrderStop? stop, Customer? locationOwner, Customer? orderingCustomer, TransportDocumentParty orderingParty)
    {
        if (stop is null)
        {
            return orderingParty;
        }

        var stopAddress = Address(stop.Address, null, stop.PostalCode, stop.City);
        var stopName = Blank(stop.LocationName);
        if (locationOwner is not null)
        {
            var detail = stopName is not null && !string.Equals(stopName, locationOwner.Name, StringComparison.OrdinalIgnoreCase)
                ? stopName
                : null;
            return new TransportDocumentParty(
                locationOwner.Name, stopAddress ?? Address(locationOwner.Street, locationOwner.HouseNumber, locationOwner.PostalCode, locationOwner.City),
                locationOwner.VatNumber, detail, TransportDocumentPartySource.LocationOwner);
        }

        if (stopName is null || (orderingCustomer is not null && SameAddress(stop, orderingCustomer)))
        {
            return orderingParty with { Detail = stopName is not null && !string.Equals(stopName, orderingParty.Name, StringComparison.OrdinalIgnoreCase) ? stopName : null };
        }

        return new TransportDocumentParty(stopName, stopAddress, null, null, TransportDocumentPartySource.StopLocation);
    }

    /// <summary>Street + house number and postal code equal, ignoring case, spaces and punctuation.</summary>
    private static bool SameAddress(TransportOrderStop stop, Customer customer)
    {
        static string Key(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var stopStreet = Key(stop.Address);
        return stopStreet.Length > 0
               && stopStreet == Key($"{customer.Street}{customer.HouseNumber}")
               && Key(stop.PostalCode) == Key(customer.PostalCode);
    }

    /// <summary>
    /// D2: the work-order block — work description, planned start/end of the (first) site stop in
    /// tenant wall-clock time, the activity's planned duration and the lift data as entered.
    /// Nothing is derived that was not stored: an empty field stays empty on the document.
    /// </summary>
    private async Task<TransportDocumentWorkOrder> BuildWorkOrderAsync(TransportOrder order, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var site = order.Stops.Where(s => !s.IsDeleted && s.StopType == StopType.Site)
            .OrderBy(s => s.Sequence)
            .FirstOrDefault();
        var durationHours = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == order.Id)
            .OrderBy(a => a.Sequence)
            .Select(a => a.DurationHours)
            .FirstOrDefaultAsync(cancellationToken);

        DateTime? plannedStart = null;
        DateTime? plannedEnd = null;
        if (site?.PlannedFrom is not null || site?.PlannedTo is not null)
        {
            // Planned times are UTC instants; a printed work order shows the tenant's wall clock.
            var zone = await TenantTimeZone.ForTenantAsync(_dbContext, tenantId, cancellationToken);
            plannedStart = site.PlannedFrom is { } from ? TenantTimeZone.ToWallClock(from, zone) : null;
            plannedEnd = site.PlannedTo is { } to ? TenantTimeZone.ToWallClock(to, zone) : null;
        }

        return new TransportDocumentWorkOrder(
            order.WorkDescription, plannedStart, plannedEnd, durationHours,
            order.LiftLoadWeightKg, order.LiftLoadDimensions, order.LiftRadiusMeters, order.LiftHeightMeters,
            order.LiftConditions, order.LiftEquipment);
    }
}
