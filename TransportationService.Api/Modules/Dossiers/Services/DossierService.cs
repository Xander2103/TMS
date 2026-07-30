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

    public DossierService(
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

    public async Task<DossierDetailDto> CreateAsync(SaveDossierRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        await ValidateAsync(request, tenantId, cancellationToken);

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        var dossier = new TransportDossier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = request.Title.Trim(),
            Description = Trim(request.Description),
            CustomerId = request.CustomerId,
            ResponsibleUserId = request.ResponsibleUserId,
            Notes = Trim(request.Notes),
        };
        _dbContext.Add(dossier);

        await TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => dossier.DossierNumber = GenerateDossierNumber(settings),
            cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "Created", null,
            new { dossier.DossierNumber, dossier.Title, dossier.CustomerId }, cancellationToken);

        return (await GetAsync(dossier.Id, cancellationToken))!;
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

        return new DossierDetailDto(
            dossier.Id, dossier.DossierNumber, dossier.Title, dossier.Description, dossier.Status.ToString(),
            dossier.CustomerId, customerName, dossier.ResponsibleUserId, responsibleName,
            dossier.ClosedAt, dossier.Notes, dossier.CreatedAt,
            orders, relations, incidents, financials);
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
        await ValidateAsync(request, tenantId, cancellationToken);

        dossier.Title = request.Title.Trim();
        dossier.Description = Trim(request.Description);
        dossier.CustomerId = request.CustomerId;
        dossier.ResponsibleUserId = request.ResponsibleUserId;
        dossier.Notes = Trim(request.Notes);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), "Updated", null,
            new { dossier.Title, dossier.CustomerId, dossier.ResponsibleUserId }, cancellationToken);

        return await GetAsync(id, cancellationToken);
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
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainValidationException("title", "Een titel is verplicht.");
        }

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
