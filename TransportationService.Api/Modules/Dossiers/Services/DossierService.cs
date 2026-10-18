using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierService
{
    Task<IReadOnlyList<DossierListItemDto>> ListAsync(string? search, string? status, Guid? customerId, CancellationToken cancellationToken);

    Task<DossierDetailDto> CreateAsync(SaveDossierRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Server-side dossier search: filters + sort + pagination in one tenant-scoped query.</summary>
    Task<Common.Models.PagedResult<DossierListItemDto>> SearchAsync(DossierSearchQuery query, CancellationToken cancellationToken);

    Task<DossierDetailDto?> UpdateAsync(Guid id, SaveDossierRequest request, CancellationToken cancellationToken);

    /// <summary>Audited change of the issuing entity (old→new); inherited silently at create.</summary>
    Task<DossierDetailDto?> ChangeLegalEntityAsync(Guid id, ChangeDossierEntityRequest request, CancellationToken cancellationToken);

    /// <summary>Impact of an entity change on the dossier's linked orders, before confirming.</summary>
    Task<DossierLegalEntityChangeImpactDto?> PreviewLegalEntityChangeAsync(Guid id, Guid legalEntityId, CancellationToken cancellationToken);



    Task<DossierDetailDto?> LinkOrderAsync(Guid id, LinkDossierOrderRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> UnlinkOrderAsync(Guid id, Guid transportOrderId, CancellationToken cancellationToken);

    Task<DossierDetailDto?> AddRelationAsync(Guid id, AddDossierRelationRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> RemoveRelationAsync(Guid id, Guid relationId, CancellationToken cancellationToken);
}

public partial class DossierService : IDossierService
{
    private const string EntityType = "TransportDossier";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly Modules.Orders.Services.ITransportOrderService? _orderService;
    private readonly IDossierReadinessService _readinessService;
    private readonly Modules.Identity.Services.IPermissionAuthorizationService? _permissionService;
    private readonly Modules.Identity.Services.ICurrentUserContext? _currentUser;

    public DossierService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        TimeProvider timeProvider,
        IDossierReadinessService? readinessService = null,
        Modules.Identity.Services.IPermissionAuthorizationService? permissionService = null,
        Modules.Identity.Services.ICurrentUserContext? currentUser = null,
        Modules.Orders.Services.ITransportOrderService? orderService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _orderService = orderService;
        _readinessService = readinessService ?? new DossierReadinessService(dbContext, tenantContext);
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DossierListItemDto>> ListAsync(
        string? search, string? status, Guid? customerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.TransportDossiers.AsNoTracking().Where(d => d.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<DossierStatus>(status, out var parsed))
            {
                throw new DomainValidationException("status", "Onbekende dossierstatus.");
            }

            query = query.Where(d => d.Status == parsed);
        }

        if (customerId is { } cid)
        {
            query = query.Where(d => d.CustomerId == cid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Number, title, the customer's own reference, and the customer's name/number — all
            // in the same SQL statement (correlated EXISTS), never a client-side pass.
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(d =>
                d.DossierNumber.ToLower().Contains(term)
                || d.Title.ToLower().Contains(term)
                || (d.CustomerReference != null && d.CustomerReference.ToLower().Contains(term))
                || _dbContext.Customers.Any(c => c.Id == d.CustomerId
                    && (c.Name.ToLower().Contains(term) || c.CustomerNumber.ToLower().Contains(term))));
        }

        return await ProjectListAsync(query.OrderByDescending(d => d.CreatedAt).Take(500), cancellationToken);
    }

    /// <summary>
    /// The list projection shared by <see cref="ListAsync"/> (compat) and <see cref="SearchAsync"/>:
    /// ONE statement with correlated scalar subqueries per row — no per-row round trips.
    /// </summary>
    private async Task<List<DossierListItemDto>> ProjectListAsync(IQueryable<TransportDossier> ordered, CancellationToken cancellationToken)
    {
        var rows = await ordered
            .Select(d => new
            {
                d.Id, d.DossierNumber, d.Title, d.Status, d.CustomerId, d.ResponsibleUserId, d.CreatedAt,
                d.CustomerReference, d.DossierDate, d.ClosedAt, d.ConfirmationSource, d.Version,
                ActivityCount = _dbContext.DossierActivities.Count(a => a.DossierId == d.Id),
                CustomerName = _dbContext.Customers
                    .Where(c => c.Id == d.CustomerId).Select(c => (string?)c.Name).FirstOrDefault(),
                CustomerNumber = _dbContext.Customers
                    .Where(c => c.Id == d.CustomerId).Select(c => (string?)c.CustomerNumber).FirstOrDefault(),
                ResponsibleName = _dbContext.Users
                    .Where(u => u.Id == d.ResponsibleUserId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefault(),
                OrderCount = _dbContext.DossierOrders.Count(l => l.DossierId == d.Id),
                // OrderPricingState.IsPricedExpression — the single definition, translated in place.
                PricedOrderCount = _dbContext.DossierOrders
                    .Where(l => l.DossierId == d.Id)
                    .Join(_dbContext.TransportOrders, l => l.TransportOrderId, o => o.Id, (l, o) => o)
                    .Where(OrderPricingState.IsPricedExpression)
                    .Count(),
                OpenIncidentCount = _dbContext.Incidents.Count(i =>
                    i.DossierId == d.Id && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress)),
            })
            .ToListAsync(cancellationToken);

        // M11: the commercial counts come from the ONE shared computation (DossierPriceCounts) —
        // the same the list filter and the dossier detail use. One extra statement per page.
        var counts = await DossierPriceCounts.LoadAsync(_dbContext, _tenantContext.TenantId, rows.Select(r => r.Id).ToList(), cancellationToken);

        return rows
            .Select(r =>
            {
                var c = counts.GetValueOrDefault(r.Id) ?? DossierPriceCounts.Empty;
                return new DossierListItemDto(
                    r.Id, r.DossierNumber, r.Title, r.Status.ToString(), r.CustomerId,
                    r.CustomerName, r.ResponsibleName, r.OrderCount, r.OpenIncidentCount, r.CreatedAt,
                    CustomerReference: r.CustomerReference,
                    CustomerNumber: r.CustomerNumber,
                    // Null (not € 0,00) when nothing is priced — an override or agreement at 0 still yields 0 here.
                    AgreedPriceTotal: c.Total,
                    PricedOrderCount: r.PricedOrderCount,
                    BillableActivityCount: c.Relevant,
                    PricedActivityCount: c.WithPrice,
                    ZeroPricedActivityCount: c.ZeroUnconfirmed,
                    DossierDate: r.DossierDate,
                    ConfirmedAt: r.Status == DossierStatus.Closed ? r.ClosedAt : null,
                    ConfirmationSource: r.ConfirmationSource?.ToString(),
                    ActivityCount: r.ActivityCount,
                    Version: r.Version,
                    PriceStatus: c.Status,
                    SettledActivityCount: c.Settled,
                    UnpricedActivityCount: c.Unpriced,
                    FreeActivityCount: c.Free,
                    PartiallyPricedActivityCount: c.Partial);
            })
            .ToList();
    }

    /// <summary>
    /// Fast create (spec Part I): the ONLY required input is the customer. Date defaults to
    /// today, the title to "klant — datum", the issuing entity is inherited silently
    /// (customer default → tenant default → none), and a quick-start activity type becomes
    /// the first activity. Goods, route, price, contacts, times: all deliberately absent —
    /// completion happens on the dossier page, guided by readiness.
    /// </summary>
    public async Task<DossierDetailDto> CreateAsync(SaveDossierRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        RequireTextLengths(request);

        if (request.CustomerId is not { } customerId)
        {
            throw new DomainValidationException("customerId", "Kies een klant.");
        }

        var customer = await _dbContext.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == customerId, cancellationToken);
        if (customer is null)
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        // Same intake rule as orders: blocked/inactive customers get no NEW work.
        if (customer.IsBlocked)
        {
            throw new DomainValidationException(
                "customerId", "Deze klant is geblokkeerd; er kunnen geen nieuwe dossiers aangemaakt worden.");
        }

        if (!customer.IsActive)
        {
            throw new DomainValidationException(
                "customerId", "Deze klant is inactief; er kunnen geen nieuwe dossiers aangemaakt worden.");
        }

        if (request.ResponsibleUserId is { } userId
            && !await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive, cancellationToken))
        {
            throw new DomainValidationException("responsibleUserId", "De gekozen verantwoordelijke bestaat niet of is inactief.");
        }

        ActivityType? templateType = null;
        if (request.ActivityTypeId is { } typeId)
        {
            templateType = await _dbContext.ActivityTypes
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == typeId && t.IsActive, cancellationToken);
            if (templateType is null)
            {
                throw new DomainValidationException("activityTypeId", "Het gekozen activiteitstype bestaat niet of is inactief.");
            }
        }

        var dossierDate = request.DossierDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var legalEntityId = await ResolveInheritedLegalEntityAsync(customer.DefaultLegalEntityId, cancellationToken);

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var title = Trim(request.Title) ?? $"{customer.Name} — {dossierDate:dd-MM-yyyy}";
        var dossier = new TransportDossier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Length > 200 ? title[..200] : title,
            Description = Trim(request.Description),
            CustomerId = customerId,
            CustomerReference = Trim(request.CustomerReference),
            DossierDate = dossierDate,
            LegalEntityId = legalEntityId,
            // M15: no explicit choice → the creating internal user owns it ("Mijn open").
            ResponsibleUserId = request.ResponsibleUserId
                ?? await DossierOwnership.InternalCreatorAsync(_dbContext, tenantId, _currentUser?.CurrentUserId, cancellationToken),
            Notes = Trim(request.Notes),
        };
        _dbContext.Add(dossier);
        if (templateType is not null)
        {
            _dbContext.Add(new DossierActivity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossier.Id,
                ActivityTypeId = templateType.Id, Sequence = 1,
            });
        }

        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => dossier.DossierNumber = GenerateDossierNumber(settings),
            cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "Created", null,
            new
            {
                dossier.DossierNumber, dossier.Title, dossier.CustomerId, dossier.DossierDate,
                dossier.LegalEntityId, Template = templateType?.Code,
            }, cancellationToken);

        return (await GetAsync(dossier.Id, cancellationToken))!;
    }

    /// <summary>Customer default when still valid/active, else the tenant default entity, else none.</summary>
    private async Task<Guid?> ResolveInheritedLegalEntityAsync(Guid? customerDefault, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        if (customerDefault is { } candidate
            && await _dbContext.LegalEntities.AnyAsync(
                e => e.TenantId == tenantId && e.Id == candidate && e.IsActive, cancellationToken))
        {
            return candidate;
        }

        var tenantDefault = await _dbContext.LegalEntities.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.IsActive && e.IsDefault)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return tenantDefault;
    }

    public async Task<DossierDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await _dbContext.TransportDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        var customerName = dossier.CustomerId is { } customerId
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == customerId).Select(c => (string?)c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var responsibleName = dossier.ResponsibleUserId is { } userId
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefaultAsync(cancellationToken)
            : null;
        var confirmedByName = dossier.ConfirmedByUserId is { } confirmedBy
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == confirmedBy).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefaultAsync(cancellationToken)
            : null;

        // Linked orders (anonymous projection: record ctors do not translate in joins).
        var orderRows = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.DossierId == id)
            .Join(_dbContext.TransportOrders.AsNoTracking(), l => l.TransportOrderId, o => o.Id,
                (l, o) => new
                {
                    LinkId = l.Id, o.Id, o.OrderNumber, o.OrderDate, o.Status, o.GoodsDescription, o.AgreedPrice,
                    o.PriceIsManual, o.PricingSource, o.OneOffFixedAmount, o.UpdatedAt,
                })
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orders = orderRows
            .Select(x => new DossierOrderDto(x.LinkId, x.Id, x.OrderNumber, x.OrderDate, x.Status.ToString(), x.GoodsDescription, x.AgreedPrice,
                IsPriced: OrderPricingState.IsPriced(x.PriceIsManual, x.PricingSource, x.OneOffFixedAmount, x.AgreedPrice)))
            .ToList();

        // Relations in both directions, labelled from this dossier's point of view.
        var relationRows = await _dbContext.DossierRelations.AsNoTracking()
            .Where(r => r.TenantId == tenantId && (r.SourceDossierId == id || r.TargetDossierId == id))
            .ToListAsync(cancellationToken);
        var otherIds = relationRows
            .Select(r => r.SourceDossierId == id ? r.TargetDossierId : r.SourceDossierId)
            .Distinct()
            .ToList();
        var others = await _dbContext.TransportDossiers.AsNoTracking()
            .Where(d => otherIds.Contains(d.Id))
            .Select(d => new { d.Id, d.DossierNumber, d.Title })
            .ToDictionaryAsync(d => d.Id, cancellationToken);
        var relations = relationRows
            .Select(r =>
            {
                var isOutgoing = r.SourceDossierId == id;
                var otherId = isOutgoing ? r.TargetDossierId : r.SourceDossierId;
                var other = others.GetValueOrDefault(otherId);
                return new DossierRelationDto(
                    r.Id, r.RelationType.ToString(), r.Notes, isOutgoing,
                    otherId, other?.DossierNumber ?? "?", other?.Title ?? "Onbekend dossier");
            })
            .ToList();

        var incidents = (await _dbContext.Incidents.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.DossierId == id)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new { i.Id, i.Title, i.IncidentType, i.Status, i.Severity, i.DueDate })
                .ToListAsync(cancellationToken))
            .Select(i => new DossierIncidentDto(
                i.Id, i.Title, i.IncidentType.ToString(), i.Status.ToString(), i.Severity.ToString(), i.DueDate))
            .ToList();

        var legalEntityName = dossier.LegalEntityId is { } entityId
            ? await _dbContext.LegalEntities.AsNoTracking()
                .Where(e => e.Id == entityId && e.TenantId == tenantId)
                .Select(e => (string?)(e.TradingName ?? e.LegalName)).FirstOrDefaultAsync(cancellationToken)
            : null;

        var activityRows = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DossierId == id)
            .OrderBy(a => a.Sequence)
            .Join(_dbContext.ActivityTypes.AsNoTracking(), a => a.ActivityTypeId, t => t.Id,
                (a, t) => new
                {
                    a.Id, a.ActivityTypeId, t.Code, t.Name, t.Icon, t.HasStops, t.SupportsGoods, t.AllowsDuration, t.IsBillable,
                    t.SupportsOnSiteWork, t.PlanningRelevant, t.RequiresCrane,
                    a.Sequence, a.Label, a.LinkedTransportOrderId, a.LinkedActivityId,
                    a.PlannedDate, a.DurationHours, a.Notes, a.UpdatedAt,
                    a.ExecutionStatus, a.StartTime, a.SiteAddress, a.WorkDescription, a.DriverId, a.VehicleId,
                    a.StartedAt, a.CompletedAt, a.CompletedByUserId, a.CancelledAt, a.CancellationReason,
                })
            .ToListAsync(cancellationToken);

        // T-H7: names of the operator/vehicle/completer of standalone activities — set-based.
        var activityDriverIds = activityRows.Where(a => a.DriverId != null).Select(a => a.DriverId!.Value).Distinct().ToList();
        var activityDriverNames = activityDriverIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && activityDriverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id, (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var activityVehicleIds = activityRows.Where(a => a.VehicleId != null).Select(a => a.VehicleId!.Value).Distinct().ToList();
        var activityVehicles = activityVehicleIds.Count == 0
            ? new Dictionary<Guid, (string Number, string Plate)>()
            : (await _dbContext.Vehicles.AsNoTracking()
                    .Where(v => v.TenantId == tenantId && activityVehicleIds.Contains(v.Id))
                    .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
                    .ToListAsync(cancellationToken))
                .ToDictionary(v => v.Id, v => (v.InternalNumber, v.LicensePlate));
        var completerIds = activityRows.Where(a => a.CompletedByUserId != null).Select(a => a.CompletedByUserId!.Value).Distinct().ToList();
        var completerNames = completerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => completerIds.Contains(u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var linkedOrders = orderRows.ToDictionary(o => o.Id, o => o);

        // Step 13: price carriers per unit — the order's snapshot status for transport activities,
        // the activity's own record for standalone ones.
        var orderIds = orderRows.Select(o => o.Id).ToList();
        // ACC-M12: the live invoice each component is on (order-backed through its order, standalone
        // through the activity line); cancelled documents do not count as "invoiced".
        var componentActivityIds = activityRows.Select(a => a.Id).ToList();
        var dossierInvoiceLines = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (l.TransportOrderId != null || l.DossierActivityId != null))
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.TenantId == tenantId),
                l => l.InvoiceId, i => i.Id, (l, i) => new { l.TransportOrderId, l.DossierActivityId, i.Id, i.InvoiceNumber, i.Status, i.Kind })
            .Where(x => (x.TransportOrderId != null && orderIds.Contains(x.TransportOrderId.Value))
                        || (x.DossierActivityId != null && componentActivityIds.Contains(x.DossierActivityId.Value)))
            .ToListAsync(cancellationToken);
        var liveInvoiceByOrder = dossierInvoiceLines
            .Where(x => x.TransportOrderId != null && x.Kind == Modules.Invoicing.Entities.InvoiceKind.Invoice && x.Status != Modules.Invoicing.Entities.InvoiceStatus.Cancelled)
            .GroupBy(x => x.TransportOrderId!.Value)
            .ToDictionary(g => g.Key, g => new ComponentInvoiceRef(g.First().Id, g.First().InvoiceNumber, g.First().Status));
        var liveInvoiceByActivity = dossierInvoiceLines
            .Where(x => x.DossierActivityId != null && x.Kind == Modules.Invoicing.Entities.InvoiceKind.Invoice && x.Status != Modules.Invoicing.Entities.InvoiceStatus.Cancelled)
            .GroupBy(x => x.DossierActivityId!.Value)
            .ToDictionary(g => g.Key, g => new ComponentInvoiceRef(g.First().Id, g.First().InvoiceNumber, g.First().Status));
        ComponentInvoiceRef? ComponentInvoice(Guid activityId, Guid? linkedOrderId) =>
            linkedOrderId is { } oid && liveInvoiceByOrder.TryGetValue(oid, out var byOrder) ? byOrder
            : liveInvoiceByActivity.TryGetValue(activityId, out var byActivity) ? byActivity : null;
        var dossierInvoices = dossierInvoiceLines
            .Select(x => new { x.Id, x.InvoiceNumber, x.Status, x.Kind })
            .Distinct()
            .Select(x => new Modules.Orders.Dtos.InvoiceLinkDto(x.Id, x.InvoiceNumber, ComponentInvoiceRef.DisplayNumberOf(x.InvoiceNumber, x.Id), x.Status.ToString(), x.Kind.ToString()))
            .OrderBy(x => x.Status == "Cancelled" ? 1 : 0).ThenBy(x => x.DisplayNumber)
            .ToList();
        var snapshotByOrder = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrderPricingSnapshots.AsNoTracking()
                .Where(s => s.TenantId == tenantId && orderIds.Contains(s.TransportOrderId))
                .Select(s => new { s.TransportOrderId, s.Status, s.CoverageStatus, s.IsStale })
                .ToDictionaryAsync(s => s.TransportOrderId, cancellationToken);
        var standaloneIds = activityRows.Where(a => !a.HasStops).Select(a => a.Id).ToList();
        var pricingByActivity = standaloneIds.Count == 0
            ? new Dictionary<Guid, DossierActivityPricing>()
            : await _dbContext.DossierActivityPricings.AsNoTracking()
                .Where(p => p.TenantId == tenantId && standaloneIds.Contains(p.DossierActivityId))
                .ToDictionaryAsync(p => p.DossierActivityId, cancellationToken);
        // D5: the sales lines of the standalone price records — one query for the whole dossier.
        var pricingIds = pricingByActivity.Values.Select(p => p.Id).ToList();
        var priceLinesByPricing = pricingIds.Count == 0
            ? new Dictionary<Guid, List<DossierActivityPriceLineDto>>()
            : (await _dbContext.DossierActivityPriceLines.AsNoTracking()
                    .Where(l => l.TenantId == tenantId && pricingIds.Contains(l.DossierActivityPricingId))
                    .OrderBy(l => l.Sequence)
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.DossierActivityPricingId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(l => new DossierActivityPriceLineDto(
                        l.Id, l.Sequence, l.Label, l.Quantity, l.Unit, l.UnitPrice, l.Amount, l.SalesCategoryId)).ToList());

        // D7: note counts + the newest note per activity — set-based for the whole dossier.
        var notes = await DossierNoteService.SummarizeAsync(_dbContext, tenantId, id, cancellationToken);

        // D6: ONE query for every document of the dossier (both levels, each once) and one for the
        // issued transport documents of its activities' orders — none per activity.
        var documentRows = await Modules.Orders.Services.DossierDocumentQuery.ForDossier(_dbContext, tenantId, id)
            .Select(d => new { d.TransportOrderId, d.DossierActivityId, d.DocumentType })
            .ToListAsync(cancellationToken);
        var documentCountByOrder = documentRows
            .Where(d => d.TransportOrderId is not null)
            .GroupBy(d => d.TransportOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        // Documents of a STANDALONE activity (no order) hang on the activity itself.
        var documentCountByActivity = documentRows
            .Where(d => d.DossierActivityId is not null)
            .GroupBy(d => d.DossierActivityId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        var activityOrderIds = activityRows.Where(a => a.LinkedTransportOrderId is not null)
            .Select(a => a.LinkedTransportOrderId!.Value).Distinct().ToList();
        var issuedByOrder = activityOrderIds.Count == 0
            ? new Dictionary<Guid, List<DossierActivityIssuedDocumentDto>>()
            : (await _dbContext.IssuedTransportDocuments.AsNoTracking()
                    // M9: the ACTIVE documents only — a replaced CMR is history, shown in the Documenten tab.
                    .Where(i => i.TenantId == tenantId && activityOrderIds.Contains(i.TransportOrderId) && i.ReplacedByDocumentId == null)
                    .OrderBy(i => i.IssuedAt).ThenBy(i => i.DocumentNumber)
                    .Select(i => new { i.TransportOrderId, i.Id, i.Kind, i.DocumentNumber })
                    .ToListAsync(cancellationToken))
                .GroupBy(i => i.TransportOrderId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(i => new DossierActivityIssuedDocumentDto(i.Id, i.Kind.ToString(), i.DocumentNumber)).ToList());

        // D1: effective assignment per activity = the trip of its order. Set-based for the whole
        // dossier (a fixed number of queries, whatever the number of activities).
        var assignmentByOrder = await BuildActivityAssignmentsAsync(
            activityRows.Where(a => a.LinkedTransportOrderId is not null)
                .Select(a => a.LinkedTransportOrderId!.Value).Distinct().ToList(),
            cancellationToken);

        var activities = activityRows
            .Select(a =>
            {
                var linked = a.LinkedTransportOrderId is { } oid ? linkedOrders.GetValueOrDefault(oid) : null;
                string pricingSource = "None";
                decimal? agreedPrice = null;
                var isPriced = false;
                var isZero = false;
                string? pricingStatus = null;
                Guid? pricingVersion = null;
                string? priceStatus;
                var freeConfirmed = false;
                IReadOnlyList<DossierActivityPriceLineDto> priceLines = [];
                if (a.HasStops)
                {
                    var snapshot = linked is null ? null : snapshotByOrder.GetValueOrDefault(linked.Id);
                    if (linked is not null)
                    {
                        pricingSource = "Order";
                        agreedPrice = OrderPricingState.EffectiveAgreedPrice(linked.PricingSource, linked.OneOffFixedAmount, linked.AgreedPrice);
                        isPriced = OrderPricingState.IsPriced(linked.PriceIsManual, linked.PricingSource, linked.OneOffFixedAmount, linked.AgreedPrice);
                        pricingStatus = snapshot?.Status.ToString();
                    }

                    // An intentional € 0 on the order reads as Free (the one-off command at 0 IS the
                    // explicit decision); it is NOT a zero that still needs a decision, so `isZero`
                    // — the "needs attention" counter — stays false for order-backed units.
                    var intentionalZero = linked is not null
                        && OrderPricingState.IsIntentionalZero(linked.PriceIsManual, linked.PricingSource, linked.OneOffFixedAmount, linked.AgreedPrice);
                    priceStatus = ActivityPriceStatus.ForOrder(
                        a.IsBillable, linked is not null, isPriced, intentionalZero, snapshot?.CoverageStatus, snapshot?.IsStale ?? false);
                }
                else
                {
                    var pricing = pricingByActivity.GetValueOrDefault(a.Id);
                    if (pricing is not null)
                    {
                        pricingSource = pricing.PricingSource.ToString();
                        agreedPrice = pricing.AgreedPrice;
                        isPriced = ActivityPricingState.IsPriced(pricing);
                        // D5: a € 0 that was confirmed free is no longer a "check this" zero.
                        isZero = ActivityPricingState.IsUnconfirmedZero(pricing);
                        pricingStatus = pricing.Status.ToString();
                        pricingVersion = pricing.Version;
                        freeConfirmed = pricing.FreeConfirmed;
                        priceLines = priceLinesByPricing.GetValueOrDefault(pricing.Id) ?? [];
                    }

                    priceStatus = ActivityPriceStatus.ForStandalone(a.IsBillable, pricing);
                }

                var activityNotes = notes.ByActivity.GetValueOrDefault(a.Id);
                var standalone = !a.HasStops;
                var vehicle = a.VehicleId is { } vid && activityVehicles.TryGetValue(vid, out var v) ? v : ((string Number, string Plate)?)null;
                // M11: a cancelled order / cancelled standalone activity is no commercial component any more.
                var cancelled = standalone
                    ? a.ExecutionStatus == DossierActivityExecutionStatus.Cancelled
                    : linked?.Status == TransportOrderStatus.Cancelled;

                var dto = new DossierActivityDto(
                    a.Id, a.ActivityTypeId, a.Code, a.Name, a.Icon, a.HasStops, a.SupportsGoods, a.AllowsDuration,
                    a.Sequence, a.Label, a.LinkedTransportOrderId, linked?.OrderNumber, linked?.Status.ToString(),
                    a.LinkedActivityId, a.PlannedDate, a.DurationHours, a.Notes,
                    IsBillable: a.IsBillable, PricingSource: pricingSource, AgreedPrice: agreedPrice, IsPriced: isPriced,
                    PricingStatus: pricingStatus, PricingVersion: pricingVersion,
                    SupportsOnSiteWork: a.SupportsOnSiteWork,
                    Assignment: a.LinkedTransportOrderId is { } assignedOrderId
                        ? assignmentByOrder.GetValueOrDefault(assignedOrderId)
                        : null,
                    PriceLines: priceLines,
                    FreeConfirmed: freeConfirmed,
                    PriceStatus: priceStatus,
                    NoteCount: activityNotes?.Count ?? 0,
                    LatestNotePreview: activityNotes?.LatestPreview,
                    LatestNoteAt: activityNotes?.LatestAt,
                    // D6: the OWN documents of the activity's order, or of the standalone activity
                    // itself — dossier-level documents are counted on the dossier, never per activity.
                    IssuedDocuments: a.LinkedTransportOrderId is { } issuedOrderId
                        ? issuedByOrder.GetValueOrDefault(issuedOrderId) ?? []
                        : [],
                    DocumentCount: a.LinkedTransportOrderId is { } documentOrderId
                        ? documentCountByOrder.GetValueOrDefault(documentOrderId)
                        : documentCountByActivity.GetValueOrDefault(a.Id),
                    PlanningRelevant: a.PlanningRelevant,
                    IsExecutable: standalone && a.PlanningRelevant,
                    RequiresCrane: a.RequiresCrane,
                    ExecutionStatus: standalone ? a.ExecutionStatus.ToString() : null,
                    StartTime: standalone ? a.StartTime : null,
                    PlannedEnd: standalone ? StandaloneActivitySchedule.PlannedEnd(a.PlannedDate, a.StartTime, a.DurationHours) : null,
                    SiteAddress: standalone ? a.SiteAddress : null,
                    WorkDescription: standalone ? a.WorkDescription : null,
                    DriverId: standalone ? a.DriverId : null,
                    DriverName: standalone && a.DriverId is { } did ? activityDriverNames.GetValueOrDefault(did) : null,
                    VehicleId: standalone ? a.VehicleId : null,
                    VehicleNumber: standalone ? vehicle?.Number : null,
                    VehiclePlate: standalone ? vehicle?.Plate : null,
                    StartedAt: standalone ? a.StartedAt : null,
                    CompletedAt: standalone ? a.CompletedAt : null,
                    CompletedByName: standalone && a.CompletedByUserId is { } cid ? completerNames.GetValueOrDefault(cid) : null,
                    CancelledAt: standalone ? a.CancelledAt : null,
                    CancellationReason: standalone ? a.CancellationReason : null,
                    CountsForPricing: a.IsBillable && !cancelled,
                    InvoiceId: ComponentInvoice(a.Id, a.LinkedTransportOrderId)?.Id,
                    InvoiceNumber: ComponentInvoice(a.Id, a.LinkedTransportOrderId)?.InvoiceNumber,
                    InvoiceDisplayNumber: ComponentInvoice(a.Id, a.LinkedTransportOrderId)?.DisplayNumber,
                    InvoiceStatus: ComponentInvoice(a.Id, a.LinkedTransportOrderId)?.Status.ToString());
                return (Dto: dto, IsZero: isZero);
            })
            .ToList();

        // M11: the dossier's commercial completeness from the ONE shared computation — identical to
        // the list column and the list filter (cancelled orders/activities and non-billable types excluded).
        var priceCounts = (await DossierPriceCounts.LoadAsync(_dbContext, tenantId, [id], cancellationToken))
            .GetValueOrDefault(id) ?? DossierPriceCounts.Empty;
        var financials = await BuildFinancialsAsync(id, orderIds, orders.Count(o => o.IsPriced), priceCounts, cancellationToken);

        var readiness = await _readinessService.EvaluateAsync(id, cancellationToken);

        // Redesign 2026-09-11: the Overzicht summarises documents and "last changed" without
        // extra client fetches — one query over the linked orders' documents, and the maximum
        // UpdatedAt over rows that are already in memory.
        // D6: documents of the DOSSIER — the same definition as the dossier document list, so a
        // document is counted exactly once whatever level it hangs on.
        var documentTypes = documentRows.Select(d => d.DocumentType).ToList();
        // M10 (acceptance 2026-10-16): the Overzicht said "0 documenten" next to issued CMRs and
        // PODs. Those are separate records (never TransportOrderDocuments), so they are counted
        // separately — ACTIVE issued transport documents and CURRENT proofs of delivery of the
        // dossier's linked orders — and never folded into the upload count twice.
        var issuedDocumentCount = orderIds.Count == 0
            ? 0
            : await _dbContext.IssuedTransportDocuments.AsNoTracking()
                .CountAsync(i => i.TenantId == tenantId && orderIds.Contains(i.TransportOrderId) && i.ReplacedByDocumentId == null, cancellationToken);
        var podCount = orderIds.Count == 0
            ? 0
            : await _dbContext.ProofsOfDelivery.AsNoTracking()
                .CountAsync(p => p.TenantId == tenantId && orderIds.Contains(p.TransportOrderId) && p.IsCurrent, cancellationToken);
        var lastChangedAt = new[] { dossier.UpdatedAt }
            .Concat(activityRows.Select(a => a.UpdatedAt))
            .Concat(orderRows.Select(o => o.UpdatedAt))
            .Max();

        return new DossierDetailDto(
            dossier.Id, dossier.DossierNumber, dossier.Title, dossier.Description, dossier.Status.ToString(),
            dossier.CustomerId, customerName, dossier.ResponsibleUserId, responsibleName,
            dossier.ClosedAt, dossier.Notes, dossier.CreatedAt,
            orders, relations, incidents, financials,
            dossier.CustomerReference, dossier.DossierDate, dossier.LegalEntityId, legalEntityName,
            dossier.Version, activities.Select(a => a.Dto).ToList(), readiness,
            DocumentCount: documentTypes.Count,
            DocumentTypes: documentTypes.Select(d => d.ToString()).Distinct().ToList(),
            LastChangedAt: lastChangedAt,
            NoteCount: notes.DossierLevelCount,
            ConfirmedAt: dossier.Status == DossierStatus.Closed ? dossier.ClosedAt : null,
            ConfirmedByUserId: dossier.ConfirmedByUserId,
            ConfirmedByName: confirmedByName,
            ConfirmationSource: dossier.ConfirmationSource?.ToString(),
            ConfirmationReason: dossier.ConfirmationReason,
            CancelledAt: dossier.CancelledAt,
            CancellationReason: dossier.CancellationReason,
            IssuedDocumentCount: issuedDocumentCount,
            PodCount: podCount,
            Invoices: dossierInvoices);
    }

    /// <summary>
    /// D1: the effective assignment of each order = its most relevant NON-CANCELLED trip (InProgress,
    /// then Planned, then Draft, then Completed; the latest trip date within a status). Driver,
    /// vehicle and trailer are read from the trip — the only place they live. Five set-based
    /// queries at most for the whole dossier, none per activity.
    /// </summary>
    private async Task<Dictionary<Guid, DossierActivityAssignmentDto>> BuildActivityAssignmentsAsync(
        IReadOnlyList<Guid> orderIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, DossierActivityAssignmentDto>();
        if (orderIds.Count == 0)
        {
            return result;
        }

        var tenantId = _tenantContext.TenantId;
        var links = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && orderIds.Contains(to.TransportOrderId))
            .Join(_dbContext.Trips.AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.Status != Modules.Planning.Entities.TripStatus.Cancelled),
                to => to.TripId, t => t.Id,
                (to, t) => new
                {
                    to.TransportOrderId, TripId = t.Id, t.TripNumber, t.TripDate, t.Status,
                    t.DriverId, t.VehicleId, t.TrailerId, t.VehicleSelectionSource,
                    // MD-M6: a completed trip shows the identity it was executed with.
                    t.ExecutedDriverName, t.ExecutedVehicleNumber, t.ExecutedVehiclePlate, t.ExecutedTrailerNumber, t.ExecutedTrailerPlate,
                })
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
        {
            return result;
        }

        static int Rank(Modules.Planning.Entities.TripStatus status) => status switch
        {
            Modules.Planning.Entities.TripStatus.InProgress => 0,
            Modules.Planning.Entities.TripStatus.Planned => 1,
            Modules.Planning.Entities.TripStatus.Draft => 2,
            _ => 3,
        };

        var chosen = links
            .GroupBy(l => l.TransportOrderId)
            .ToDictionary(
                g => g.Key,
                g => (Trip: g.OrderBy(l => Rank(l.Status)).ThenByDescending(l => l.TripDate).ThenBy(l => l.TripNumber).First(),
                    TripCount: g.Select(l => l.TripId).Distinct().Count()));

        var tripIds = chosen.Values.Select(c => c.Trip.TripId).Distinct().ToList();
        var orderCountByTrip = await _dbContext.TripOrders.AsNoTracking()
            .Where(to => to.TenantId == tenantId && tripIds.Contains(to.TripId))
            .GroupBy(to => to.TripId)
            .Select(g => new { TripId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TripId, x => x.Count, cancellationToken);

        var driverIds = chosen.Values.Where(c => c.Trip.DriverId is not null).Select(c => c.Trip.DriverId!.Value).Distinct().ToList();
        var vehicleIds = chosen.Values.Where(c => c.Trip.VehicleId is not null).Select(c => c.Trip.VehicleId!.Value).Distinct().ToList();
        var trailerIds = chosen.Values.Where(c => c.Trip.TrailerId is not null).Select(c => c.Trip.TrailerId!.Value).Distinct().ToList();

        var drivers = driverIds.Count == 0
            ? []
            : await _dbContext.Drivers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && driverIds.Contains(d.Id))
                .Join(_dbContext.Employees.AsNoTracking().Where(e => e.TenantId == tenantId), d => d.EmployeeId, e => e.Id,
                    (d, e) => new { d.Id, Name = e.FirstName + " " + e.LastName })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var vehicles = vehicleIds.Count == 0
            ? []
            : await _dbContext.Vehicles.AsNoTracking().IgnoreQueryFilters()
                .Where(v => v.TenantId == tenantId && vehicleIds.Contains(v.Id))
                .Select(v => new { v.Id, v.InternalNumber, v.LicensePlate })
                .ToDictionaryAsync(v => v.Id, cancellationToken);
        var trailers = trailerIds.Count == 0
            ? []
            : await _dbContext.Trailers.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.TenantId == tenantId && trailerIds.Contains(t.Id))
                .Select(t => new { t.Id, t.InternalNumber, t.LicensePlate })
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        foreach (var (orderId, (trip, tripCount)) in chosen)
        {
            var vehicle = trip.VehicleId is { } vid ? vehicles.GetValueOrDefault(vid) : null;
            var trailer = trip.TrailerId is { } tid ? trailers.GetValueOrDefault(tid) : null;
            result[orderId] = new DossierActivityAssignmentDto(
                trip.TripId, trip.TripNumber, trip.TripDate, trip.Status.ToString(),
                trip.DriverId, trip.ExecutedDriverName ?? (trip.DriverId is { } did ? drivers.GetValueOrDefault(did) : null),
                trip.VehicleId, trip.ExecutedVehicleNumber ?? vehicle?.InternalNumber, trip.ExecutedVehiclePlate ?? vehicle?.LicensePlate,
                trip.VehicleSelectionSource?.ToString(),
                trip.TrailerId, trip.ExecutedTrailerNumber ?? trailer?.InternalNumber, trip.ExecutedTrailerPlate ?? trailer?.LicensePlate,
                tripCount,
                Math.Max(0, orderCountByTrip.GetValueOrDefault(trip.TripId, 1) - 1));
        }

        return result;
    }

    public async Task<DossierDetailDto?> UpdateAsync(Guid id, SaveDossierRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        RequireOpen(dossier);
        await RequireVersionAsync(dossier, request.Version, cancellationToken);
        await ValidateAsync(request, tenantId, cancellationToken);

        // Null title keeps the current one (header edits are partial by design).
        if (Trim(request.Title) is { } newTitle)
        {
            dossier.Title = newTitle.Length > 200 ? newTitle[..200] : newTitle;
        }

        dossier.Description = Trim(request.Description);
        if (request.CustomerId is { } newCustomerId && newCustomerId != dossier.CustomerId)
        {
            // Audit fix (sprint 6): a customer change re-evaluates pricing, entity policy,
            // draft invoices and every linked order. That lives in the dedicated customer-change
            // flow; the header edit may only SET a customer on a dossier that has none and no
            // orders yet — anything else would silently bypass those rules.
            var hasOrders = await _dbContext.DossierOrders
                .AnyAsync(l => l.TenantId == tenantId && l.DossierId == dossier.Id, cancellationToken);
            if (dossier.CustomerId is not null || hasOrders)
            {
                throw new DomainValidationException("customerId",
                    "Gebruik 'Klant wijzigen' om dit dossier naar een andere klant te verplaatsen; "
                    + "prijzen, facturatie-entiteit en gekoppelde orders worden dan mee herbeoordeeld.");
            }

            dossier.CustomerId = newCustomerId;
        }

        dossier.CustomerReference = Trim(request.CustomerReference);
        if (request.DossierDate is { } newDate)
        {
            dossier.DossierDate = newDate;
        }

        dossier.ResponsibleUserId = request.ResponsibleUserId;
        // D7: the legacy free-text column is history now (notes live in DossierNote). A client that
        // no longer sends it (null) must not wipe it; an explicit empty string still clears.
        if (request.Notes is not null)
        {
            dossier.Notes = Trim(request.Notes);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "Updated", null,
            new { dossier.Title, dossier.CustomerId, dossier.CustomerReference, dossier.DossierDate, dossier.ResponsibleUserId }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DossierDetailDto?> ChangeLegalEntityAsync(
        Guid id, ChangeDossierEntityRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        RequireOpen(dossier);
        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        var entityValid = await _dbContext.LegalEntities.AnyAsync(
            e => e.TenantId == tenantId && e.Id == request.LegalEntityId && e.IsActive, cancellationToken);
        if (!entityValid)
        {
            throw new DomainValidationException("legalEntityId", "De gekozen facturerende entiteit bestaat niet of is niet actief.");
        }

        var previous = dossier.LegalEntityId;
        if (previous == request.LegalEntityId)
        {
            return await GetAsync(id, cancellationToken);
        }

        // Wave 2 (spec Part O): the target must be in the customer's allowed set, and moving
        // AWAY from the customer default is a separate audited right with a mandatory reason —
        // dossiers.manage alone no longer suffices for cross-entity moves. A customer-less
        // dossier has no policy or default to compare against.
        if (dossier.CustomerId is { } policyCustomerId
            && await Modules.Partners.Services.CustomerEntityPolicy.ValidateAsync(
                _dbContext, tenantId, policyCustomerId, request.LegalEntityId, cancellationToken) is { } policyError)
        {
            throw new DomainValidationException("legalEntityId", policyError);
        }

        var customerDefault = dossier.CustomerId is { } defaultCustomerId
            ? await _dbContext.Customers
                .Where(c => c.TenantId == tenantId && c.Id == defaultCustomerId)
                .Select(c => c.DefaultLegalEntityId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (customerDefault is null || request.LegalEntityId != customerDefault)
        {
            // Fail-closed: no wired authorization service means NO override rights.
            var userId = _currentUser?.CurrentUserId;
            var allowed = _permissionService is not null
                && userId is { } uid
                && await _permissionService.UserHasPermissionAsync(
                    uid, Modules.Identity.PermissionCodes.DossiersOverrideEntity, cancellationToken);
            if (!allowed)
            {
                throw new DomainValidationException("legalEntityId",
                    "Je hebt geen rechten om dit dossier naar een andere entiteit dan de klantstandaard te verplaatsen.");
            }

            if (reason is null)
            {
                throw new DomainValidationException("reason",
                    "Een reden is verplicht bij een afwijkende facturerende entiteit.");
            }
        }

        // Rule H (audit fix): the dossier is the commercial authority for its linked orders, so
        // the orders that shared its entity move along in ONE unit of work through the same
        // per-order guards (sent invoice → refused, concept lines → released). Nothing ever
        // leaves a dossier and its orders on different invoicing entities silently.
        var impact = await BuildLegalEntityImpactAsync(dossier, request.LegalEntityId, customerDefault, cancellationToken);
        if (impact.BlockedReason is { } blockedByOrder)
        {
            throw new DomainValidationException("legalEntityId", blockedByOrder);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        foreach (var linked in impact.Orders)
        {
            var refused = await _orderService!.ChangeLegalEntityWithinDossierAsync(
                linked.OrderId, request.LegalEntityId, reason, cancellationToken);
            if (refused is not null)
            {
                throw new DomainValidationException("legalEntityId", $"Order {linked.OrderNumber}: {refused}");
            }
        }

        dossier.LegalEntityId = request.LegalEntityId;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "LegalEntityChanged",
            new { LegalEntityId = previous },
            new
            {
                dossier.LegalEntityId, Reason = reason,
                OrdersMoved = impact.Orders.Select(o => o.OrderNumber).ToList(),
                impact.DraftInvoiceLinesReleased,
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DossierLegalEntityChangeImpactDto?> PreviewLegalEntityChangeAsync(
        Guid id, Guid legalEntityId, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        var customerDefault = dossier.CustomerId is { } customerId
            ? await _dbContext.Customers
                .Where(c => c.TenantId == _tenantContext.TenantId && c.Id == customerId)
                .Select(c => c.DefaultLegalEntityId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        return await BuildLegalEntityImpactAsync(dossier, legalEntityId, customerDefault, cancellationToken);
    }

    private async Task<DossierLegalEntityChangeImpactDto> BuildLegalEntityImpactAsync(
        TransportDossier dossier, Guid legalEntityId, Guid? customerDefault, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        string? blocked = null;

        // Only orders that FOLLOW the dossier's entity move with it; an order deliberately put on
        // another entity keeps that choice.
        var linkedOrders = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DossierId == dossier.Id)
            .Join(_dbContext.TransportOrders.AsNoTracking().Where(o => o.TenantId == tenantId),
                l => l.TransportOrderId, o => o.Id, (l, o) => new { o.Id, o.OrderNumber, o.LegalEntityId })
            .Where(o => o.LegalEntityId == dossier.LegalEntityId && o.LegalEntityId != legalEntityId)
            .OrderBy(o => o.OrderNumber)
            .ToListAsync(cancellationToken);

        var orders = new List<DossierLegalEntityChangeOrderDto>();
        if (linkedOrders.Count > 0 && _orderService is null)
        {
            blocked = "Gekoppelde orders kunnen niet mee verplaatst worden (orderservice niet beschikbaar).";
        }

        foreach (var linked in linkedOrders)
        {
            var orderImpact = _orderService is null
                ? null
                : await _orderService.PreviewLegalEntityChangeAsync(linked.Id, legalEntityId, cancellationToken);
            var orderBlocked = orderImpact?.BlockedReason;
            if (orderBlocked is not null && blocked is null)
            {
                blocked = $"Order {linked.OrderNumber}: {orderBlocked}";
            }

            orders.Add(new DossierLegalEntityChangeOrderDto(
                linked.Id, linked.OrderNumber, orderBlocked, orderImpact?.DraftInvoiceLinesReleased ?? 0));
        }

        return new DossierLegalEntityChangeImpactDto(
            dossier.Id, dossier.LegalEntityId, legalEntityId,
            customerDefault is null || legalEntityId != customerDefault,
            blocked, orders, orders.Sum(o => o.DraftInvoiceLinesReleased));
    }

    private async Task RequireVersionAsync(TransportDossier dossier, Guid? version, CancellationToken cancellationToken)
    {
        if (version is { } expected && expected != dossier.Version)
        {
            throw new DossierVersionConflictException((await GetAsync(dossier.Id, cancellationToken))!);
        }
    }

    public async Task<DossierDetailDto?> LinkOrderAsync(Guid id, LinkDossierOrderRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        RequireOpen(dossier);

        var orderExists = await _dbContext.TransportOrders
            .AnyAsync(o => o.TenantId == tenantId && o.Id == request.TransportOrderId, cancellationToken);
        if (!orderExists)
        {
            throw new DomainValidationException("transportOrderId", "De gekozen transportopdracht bestaat niet.");
        }

        var alreadyLinked = await _dbContext.DossierOrders
            .AnyAsync(l => l.DossierId == id && l.TransportOrderId == request.TransportOrderId, cancellationToken);
        if (alreadyLinked)
        {
            throw new DomainValidationException("transportOrderId", "Deze opdracht is al aan het dossier gekoppeld.");
        }

        // D6: an order document's DossierId is the OWNING dossier of its order. A new link only
        // changes the owner when the order had none (wrapper / oldest link keep winning) — then
        // this dossier becomes it, and the order's documents follow in the same save.
        var currentOwner = await OwningDossierResolver.ResolveAsync(_dbContext, tenantId, request.TransportOrderId, cancellationToken);
        if (currentOwner is null)
        {
            await Modules.Orders.Services.OrderDocumentDossierSync.StageAsync(
                _dbContext, tenantId, request.TransportOrderId, id, cancellationToken);
        }

        _dbContext.Add(new DossierOrder
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DossierId = id,
            TransportOrderId = request.TransportOrderId,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, id.ToString(), "OrderLinked", null,
            new { request.TransportOrderId }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DossierDetailDto?> UnlinkOrderAsync(Guid id, Guid transportOrderId, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        RequireOpen(dossier);

        var link = await _dbContext.DossierOrders
            .FirstOrDefaultAsync(l => l.DossierId == id && l.TransportOrderId == transportOrderId, cancellationToken);
        if (link is null)
        {
            throw new DomainValidationException("Deze opdracht is niet aan het dossier gekoppeld.");
        }

        // D6: the order's documents follow its owning dossier AS IT WILL BE after this removal (the
        // wrapper, else the next-oldest link, else none). Only the link column changes — documents
        // of the dossier as a whole stay where they are, and no file is touched.
        var nextOwner = await OwningDossierResolver.ResolveAsync(
            _dbContext, dossier.TenantId, transportOrderId, cancellationToken, excludingLinkId: link.Id);
        await Modules.Orders.Services.OrderDocumentDossierSync.StageAsync(
            _dbContext, dossier.TenantId, transportOrderId, nextOwner?.Id, cancellationToken);

        _dbContext.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, id.ToString(), "OrderUnlinked", null,
            new { TransportOrderId = transportOrderId }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DossierDetailDto?> AddRelationAsync(Guid id, AddDossierRelationRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        if (request.TargetDossierId == id)
        {
            throw new DomainValidationException("targetDossierId", "Een dossier kan niet aan zichzelf gekoppeld worden.");
        }

        if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<DossierRelationType>(request.RelationType, out var relationType))
        {
            throw new DomainValidationException("relationType", "Onbekend relatietype.");
        }

        var targetExists = await _dbContext.TransportDossiers
            .AnyAsync(d => d.TenantId == tenantId && d.Id == request.TargetDossierId, cancellationToken);
        if (!targetExists)
        {
            throw new DomainValidationException("targetDossierId", "Het gekozen dossier bestaat niet.");
        }

        // Duplicates are refused in both directions: the pair+type is one logical link.
        var duplicate = await _dbContext.DossierRelations.AnyAsync(r =>
            r.RelationType == relationType
            && ((r.SourceDossierId == id && r.TargetDossierId == request.TargetDossierId)
                || (r.SourceDossierId == request.TargetDossierId && r.TargetDossierId == id)),
            cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("targetDossierId", "Deze dossiers zijn al met dit relatietype gekoppeld.");
        }

        _dbContext.Add(new DossierRelation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDossierId = id,
            TargetDossierId = request.TargetDossierId,
            RelationType = relationType,
            Notes = Trim(request.Notes),
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, id.ToString(), "RelationAdded", null,
            new { request.TargetDossierId, RelationType = relationType.ToString() }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DossierDetailDto?> RemoveRelationAsync(Guid id, Guid relationId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        var relation = await _dbContext.DossierRelations
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == relationId
                                      && (r.SourceDossierId == id || r.TargetDossierId == id), cancellationToken);
        if (relation is null)
        {
            throw new DomainValidationException("Deze dossierrelatie bestaat niet.");
        }

        _dbContext.Remove(relation);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, id.ToString(), "RelationRemoved", null,
            new { relation.TargetDossierId, RelationType = relation.RelationType.ToString() }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    /// <param name="counts">The shared commercial counts (<see cref="DossierPriceCounts"/>).</param>
    private async Task<DossierFinancialSummaryDto> BuildFinancialsAsync(
        Guid dossierId, IReadOnlyList<Guid> orderIds, int pricedOrderCount, DossierPriceCounts counts,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        // The dossier total is a SUM over the PRICED units only (an intentional € 0 contributes 0);
        // the frontend shows "Nog geen prijs" while PricedActivityCount is 0. ZeroPricedActivityCount
        // = the € 0 units that still await their "free" confirmation (the ⚠ of list and overview).
        var agreed = counts.Total ?? 0m;

        // Invoiced revenue: order-backed lines of the dossier's orders + (closure sprint
        // 2026-09-23) the lines that bill its standalone activities. Cancelled documents do not
        // count; a draft counts as "invoiced" exactly as it does for orders.
        var dossierActivityIds = _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DossierId == dossierId)
            .Select(a => a.Id);
        var liveInvoiceIds = _dbContext.Invoices.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status != Modules.Invoicing.Entities.InvoiceStatus.Cancelled)
            .Select(i => i.Id);
        var lines = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == tenantId && liveInvoiceIds.Contains(l.InvoiceId)
                        && ((l.TransportOrderId != null && orderIds.Contains(l.TransportOrderId.Value))
                            || (l.DossierActivityId != null && dossierActivityIds.Contains(l.DossierActivityId.Value))))
            .Select(l => new { l.Quantity, l.UnitPrice })
            .ToListAsync(cancellationToken);
        var invoiced = Math.Round(lines.Sum(l => l.Quantity * l.UnitPrice), 2);

        var incidentCosts = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DossierId == dossierId && i.Status != IncidentStatus.Cancelled)
            .Select(i => new { i.EstimatedCost, i.ActualCost })
            .ToListAsync(cancellationToken);

        return new DossierFinancialSummaryDto(
            agreed,
            invoiced,
            incidentCosts.Sum(i => i.EstimatedCost ?? 0),
            incidentCosts.Sum(i => i.ActualCost ?? 0),
            PricedOrderCount: pricedOrderCount,
            BillableActivityCount: counts.Relevant,
            PricedActivityCount: counts.WithPrice,
            ZeroPricedActivityCount: counts.ZeroUnconfirmed,
            UnpricedActivityCount: counts.Unpriced,
            SettledActivityCount: counts.Settled,
            FreeActivityCount: counts.Free,
            PartiallyPricedActivityCount: counts.Partial,
            PriceStatus: counts.Status);
    }

    private async Task ValidateAsync(SaveDossierRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        RequireTextLengths(request);
        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.TenantId == tenantId && c.Id == customerId, cancellationToken))
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        if (request.ResponsibleUserId is { } userId
            && !await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive, cancellationToken))
        {
            throw new DomainValidationException("responsibleUserId", "De gekozen verantwoordelijke bestaat niet of is inactief.");
        }
    }

    private async Task<TransportDossier?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await _dbContext.TransportDossiers
            .FirstOrDefaultAsync(d => d.TenantId == _tenantContext.TenantId && d.Id == id, cancellationToken);

    private static void RequireOpen(TransportDossier dossier)
    {
        DossierStateRules.RequireEditable(dossier);
    }

    private static string GenerateDossierNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"DOS-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        }

        var number = $"{settings.DossierNumberPrefix}{settings.DossierNumberNextValue:0000}";
        settings.DossierNumberNextValue++;
        return number;
    }

    // Column lengths of transport_dossiers (TransportDossierConfiguration). Exceeding them used to
    // surface as a PostgreSQL "value too long" 500 instead of a field error (B07). The title is not
    // listed: a too-long title is truncated on purpose (generated "klant — datum" fallbacks).
    private const int CustomerReferenceMaxLength = 100;
    private const int DescriptionMaxLength = 2000;
    private const int NotesMaxLength = 4000;

    private static void RequireTextLengths(SaveDossierRequest request)
    {
        if (Trim(request.CustomerReference) is { Length: > CustomerReferenceMaxLength })
        {
            throw new DomainValidationException("customerReference",
                $"De klantreferentie mag maximaal {CustomerReferenceMaxLength} tekens lang zijn.");
        }

        if (Trim(request.Description) is { Length: > DescriptionMaxLength })
        {
            throw new DomainValidationException("description",
                $"De omschrijving mag maximaal {DescriptionMaxLength} tekens lang zijn.");
        }

        if (Trim(request.Notes) is { Length: > NotesMaxLength })
        {
            throw new DomainValidationException("notes",
                $"De notities mogen maximaal {NotesMaxLength} tekens lang zijn.");
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>ACC-M12: one component's live invoice (id, number or draft reference, status).</summary>
    private sealed record ComponentInvoiceRef(Guid Id, string? InvoiceNumber, Modules.Invoicing.Entities.InvoiceStatus Status)
    {
        public string DisplayNumber => DisplayNumberOf(InvoiceNumber, Id);
        public static string DisplayNumberOf(string? number, Guid id) => number ?? "CONCEPT-" + id.ToString("N")[..8].ToUpperInvariant();
    }
}
