using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierService
{
    Task<IReadOnlyList<DossierListItemDto>> ListAsync(string? search, string? status, Guid? customerId, CancellationToken cancellationToken);

    Task<DossierDetailDto> CreateAsync(SaveDossierRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<DossierDetailDto?> UpdateAsync(Guid id, SaveDossierRequest request, CancellationToken cancellationToken);

    /// <summary>Audited change of the issuing entity (old→new); inherited silently at create.</summary>
    Task<DossierDetailDto?> ChangeLegalEntityAsync(Guid id, ChangeDossierEntityRequest request, CancellationToken cancellationToken);

    /// <summary>Impact of an entity change on the dossier's linked orders, before confirming.</summary>
    Task<DossierLegalEntityChangeImpactDto?> PreviewLegalEntityChangeAsync(Guid id, Guid legalEntityId, CancellationToken cancellationToken);

    Task<DossierDetailDto?> CloseAsync(Guid id, CancellationToken cancellationToken);

    Task<DossierDetailDto?> ReopenAsync(Guid id, CancellationToken cancellationToken);

    Task<DossierDetailDto?> LinkOrderAsync(Guid id, LinkDossierOrderRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> UnlinkOrderAsync(Guid id, Guid transportOrderId, CancellationToken cancellationToken);

    Task<DossierDetailDto?> AddRelationAsync(Guid id, AddDossierRelationRequest request, CancellationToken cancellationToken);

    Task<DossierDetailDto?> RemoveRelationAsync(Guid id, Guid relationId, CancellationToken cancellationToken);
}

public class DossierService : IDossierService
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
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(d =>
                d.DossierNumber.ToLower().Contains(term) || d.Title.ToLower().Contains(term));
        }

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(500)
            .Select(d => new
            {
                d.Id, d.DossierNumber, d.Title, d.Status, d.CustomerId, d.ResponsibleUserId, d.CreatedAt,
                CustomerName = _dbContext.Customers
                    .Where(c => c.Id == d.CustomerId).Select(c => (string?)c.Name).FirstOrDefault(),
                ResponsibleName = _dbContext.Users
                    .Where(u => u.Id == d.ResponsibleUserId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefault(),
                OrderCount = _dbContext.DossierOrders.Count(l => l.DossierId == d.Id),
                OpenIncidentCount = _dbContext.Incidents.Count(i =>
                    i.DossierId == d.Id && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress)),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DossierListItemDto(
                r.Id, r.DossierNumber, r.Title, r.Status.ToString(), r.CustomerId,
                r.CustomerName, r.ResponsibleName, r.OrderCount, r.OpenIncidentCount, r.CreatedAt))
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
            ResponsibleUserId = request.ResponsibleUserId,
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

        // Linked orders (anonymous projection: record ctors do not translate in joins).
        var orderRows = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.DossierId == id)
            .Join(_dbContext.TransportOrders.AsNoTracking(), l => l.TransportOrderId, o => o.Id,
                (l, o) => new { LinkId = l.Id, o.Id, o.OrderNumber, o.OrderDate, o.Status, o.GoodsDescription, o.AgreedPrice })
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var orders = orderRows
            .Select(x => new DossierOrderDto(x.LinkId, x.Id, x.OrderNumber, x.OrderDate, x.Status.ToString(), x.GoodsDescription, x.AgreedPrice))
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

        var financials = await BuildFinancialsAsync(id, orderRows.Select(o => o.Id).ToList(), cancellationToken);

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
                    a.Id, a.ActivityTypeId, t.Code, t.Name, t.Icon, t.HasStops, t.SupportsGoods, t.AllowsDuration,
                    a.Sequence, a.Label, a.LinkedTransportOrderId, a.LinkedActivityId,
                    a.PlannedDate, a.DurationHours, a.Notes,
                })
            .ToListAsync(cancellationToken);
        var linkedOrders = orderRows.ToDictionary(o => o.Id, o => new { o.OrderNumber, o.Status });
        var activities = activityRows
            .Select(a =>
            {
                var linked = a.LinkedTransportOrderId is { } oid ? linkedOrders.GetValueOrDefault(oid) : null;
                return new DossierActivityDto(
                    a.Id, a.ActivityTypeId, a.Code, a.Name, a.Icon, a.HasStops, a.SupportsGoods, a.AllowsDuration,
                    a.Sequence, a.Label, a.LinkedTransportOrderId, linked?.OrderNumber, linked?.Status.ToString(),
                    a.LinkedActivityId, a.PlannedDate, a.DurationHours, a.Notes);
            })
            .ToList();

        var readiness = await _readinessService.EvaluateAsync(id, cancellationToken);

        return new DossierDetailDto(
            dossier.Id, dossier.DossierNumber, dossier.Title, dossier.Description, dossier.Status.ToString(),
            dossier.CustomerId, customerName, dossier.ResponsibleUserId, responsibleName,
            dossier.ClosedAt, dossier.Notes, dossier.CreatedAt,
            orders, relations, incidents, financials,
            dossier.CustomerReference, dossier.DossierDate, dossier.LegalEntityId, legalEntityName,
            dossier.Version, activities, readiness);
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
        dossier.Notes = Trim(request.Notes);
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

    public async Task<DossierDetailDto?> CloseAsync(Guid id, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Dit dossier is al gesloten.");
        }

        var openIncidents = await _dbContext.Incidents
            .CountAsync(i => i.TenantId == dossier.TenantId && i.DossierId == id
                             && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress),
                cancellationToken);
        if (openIncidents > 0)
        {
            throw new DomainValidationException(
                $"Dit dossier heeft nog {openIncidents} open incident(en). Handel die eerst af.");
        }

        dossier.Status = DossierStatus.Closed;
        dossier.ClosedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "Closed", null,
            new { dossier.DossierNumber }, cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DossierDetailDto?> ReopenAsync(Guid id, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(id, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        if (dossier.Status == DossierStatus.Open)
        {
            throw new DomainValidationException("Dit dossier is al open.");
        }

        dossier.Status = DossierStatus.Open;
        dossier.ClosedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "Reopened", null,
            new { dossier.DossierNumber }, cancellationToken);

        return await GetAsync(id, cancellationToken);
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

    private async Task<DossierFinancialSummaryDto> BuildFinancialsAsync(
        Guid dossierId, IReadOnlyList<Guid> orderIds, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        decimal agreed = 0, invoiced = 0;
        if (orderIds.Count > 0)
        {
            agreed = await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => orderIds.Contains(o.Id))
                .SumAsync(o => o.AgreedPrice ?? 0, cancellationToken);

            var lines = await _dbContext.InvoiceLines.AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.TransportOrderId != null && orderIds.Contains(l.TransportOrderId.Value))
                .Select(l => new { l.Quantity, l.UnitPrice })
                .ToListAsync(cancellationToken);
            invoiced = Math.Round(lines.Sum(l => l.Quantity * l.UnitPrice), 2);
        }

        var incidentCosts = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DossierId == dossierId && i.Status != IncidentStatus.Cancelled)
            .Select(i => new { i.EstimatedCost, i.ActualCost })
            .ToListAsync(cancellationToken);

        return new DossierFinancialSummaryDto(
            agreed,
            invoiced,
            incidentCosts.Sum(i => i.EstimatedCost ?? 0),
            incidentCosts.Sum(i => i.ActualCost ?? 0));
    }

    private async Task ValidateAsync(SaveDossierRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
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
        if (dossier.Status == DossierStatus.Closed)
        {
            throw new DomainValidationException("Een gesloten dossier kan niet worden bewerkt. Heropen het dossier eerst.");
        }
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

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
