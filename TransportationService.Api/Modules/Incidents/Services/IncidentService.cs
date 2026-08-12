using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Incidents.Services;

public interface IIncidentService
{
    Task<IReadOnlyList<IncidentListItemDto>> ListAsync(
        string? search, string? status, string? severity, Guid? dossierId, Guid? customerId,
        CancellationToken cancellationToken);

    Task<IncidentDetailDto> CreateAsync(SaveIncidentRequest request, CancellationToken cancellationToken);

    Task<IncidentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IncidentDetailDto?> UpdateAsync(Guid id, SaveIncidentRequest request, CancellationToken cancellationToken);

    Task<IncidentDetailDto?> ChangeStatusAsync(Guid id, ChangeIncidentStatusRequest request, CancellationToken cancellationToken);

    // Wave 6: charge decision + linked redelivery + unified problem list.
    Task<IncidentDetailDto?> ProposeChargeAsync(Guid id, ProposeIncidentChargeRequest request, CancellationToken cancellationToken);
    Task<IncidentDetailDto?> DecideChargeAsync(Guid id, DecideIncidentChargeRequest request, CancellationToken cancellationToken);
    Task<IncidentDetailDto?> CreateRedeliveryAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProblemListItemDto>> ListProblemsAsync(CancellationToken cancellationToken);

    /// <summary>Incidents stamped with this driver (driver-app "my incidents" list).</summary>
    Task<IReadOnlyList<IncidentListItemDto>> ListForDriverAsync(Guid driverId, CancellationToken cancellationToken);
}

public class IncidentService : IIncidentService
{
    private const string EntityType = "Incident";

    /// <summary>Allowed status transitions; resolved/cancelled incidents can be reactivated.</summary>
    private static readonly IReadOnlyDictionary<IncidentStatus, IncidentStatus[]> Transitions =
        new Dictionary<IncidentStatus, IncidentStatus[]>
        {
            [IncidentStatus.New] = [IncidentStatus.InProgress, IncidentStatus.Resolved, IncidentStatus.Cancelled],
            [IncidentStatus.InProgress] = [IncidentStatus.Resolved, IncidentStatus.Cancelled],
            [IncidentStatus.Resolved] = [IncidentStatus.InProgress],
            [IncidentStatus.Cancelled] = [IncidentStatus.New],
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;
    private readonly Modules.Identity.Services.IPermissionAuthorizationService? _permissionService;
    private readonly Modules.Identity.Services.ICurrentUserContext? _currentUser;

    public IncidentService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        INotificationService notificationService,
        TimeProvider timeProvider,
        Modules.Identity.Services.IPermissionAuthorizationService? permissionService = null,
        Modules.Identity.Services.ICurrentUserContext? currentUser = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<IncidentListItemDto>> ListAsync(
        string? search, string? status, string? severity, Guid? dossierId, Guid? customerId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.Incidents.AsNoTracking().Where(i => i.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<IncidentStatus>(status, out var parsedStatus))
            {
                throw new DomainValidationException("status", "Onbekende incidentstatus.");
            }

            query = query.Where(i => i.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<IncidentSeverity>(severity, out var parsedSeverity))
            {
                throw new DomainValidationException("severity", "Onbekende ernst.");
            }

            query = query.Where(i => i.Severity == parsedSeverity);
        }

        if (dossierId is { } did)
        {
            query = query.Where(i => i.DossierId == did);
        }

        if (customerId is { } cid)
        {
            query = query.Where(i => i.CustomerId == cid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(i => i.Title.ToLower().Contains(term));
        }

        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(500)
            .Select(i => new
            {
                i.Id, i.Title, i.IncidentType, i.CustomTypeName, i.Status, i.Severity, i.DueDate, i.CreatedAt,
                CustomerName = _dbContext.Customers
                    .Where(c => c.Id == i.CustomerId).Select(c => (string?)c.Name).FirstOrDefault(),
                ResponsibleName = _dbContext.Users
                    .Where(u => u.Id == i.ResponsibleUserId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefault(),
                DossierNumber = _dbContext.TransportDossiers
                    .Where(d => d.Id == i.DossierId).Select(d => (string?)d.DossierNumber).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var today = Today;
        return rows
            .Select(r => new IncidentListItemDto(
                r.Id, r.Title, r.IncidentType.ToString(), r.CustomTypeName,
                r.Status.ToString(), r.Severity.ToString(),
                r.CustomerName, r.ResponsibleName, r.DossierNumber, r.DueDate,
                IsOverdue: r.DueDate < today
                           && (r.Status == IncidentStatus.New || r.Status == IncidentStatus.InProgress),
                r.CreatedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<IncidentListItemDto>> ListForDriverAsync(
        Guid driverId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var rows = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DriverId == driverId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .Select(i => new
            {
                i.Id, i.Title, i.IncidentType, i.CustomTypeName, i.Status, i.Severity, i.DueDate, i.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var today = Today;
        return rows
            .Select(r => new IncidentListItemDto(
                r.Id, r.Title, r.IncidentType.ToString(), r.CustomTypeName,
                r.Status.ToString(), r.Severity.ToString(),
                null, null, null, r.DueDate,
                IsOverdue: r.DueDate < today
                           && (r.Status == IncidentStatus.New || r.Status == IncidentStatus.InProgress),
                r.CreatedAt))
            .ToList();
    }

    public async Task<IncidentDetailDto> CreateAsync(SaveIncidentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        // Offline replay (driver app): the same client key returns the stored incident.
        if (request.ClientRequestId is { } clientRequestId)
        {
            var replayed = await _dbContext.Incidents.AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.ClientRequestId == clientRequestId)
                .Select(i => (Guid?)i.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (replayed is { } existingId)
            {
                return (await GetAsync(existingId, cancellationToken))!;
            }
        }

        var (incidentType, severityValue) = await ValidateAsync(request, tenantId, cancellationToken);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientRequestId = request.ClientRequestId,
        };
        Apply(incident, request, incidentType, severityValue);
        _dbContext.Add(incident);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "Created", null,
            new { incident.Title, IncidentType = incidentType.ToString(), Severity = severityValue.ToString() },
            cancellationToken);
        await NotifyResponsibleAsync(incident, cancellationToken);

        return (await GetAsync(incident.Id, cancellationToken))!;
    }

    public async Task<IncidentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var incident = await _dbContext.Incidents.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, cancellationToken);
        return incident is null ? null : await MapDetailAsync(incident, cancellationToken);
    }

    public async Task<IncidentDetailDto?> UpdateAsync(Guid id, SaveIncidentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var incident = await _dbContext.Incidents
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        if (incident.Status == IncidentStatus.Cancelled)
        {
            throw new DomainValidationException("Een geannuleerd incident kan niet worden bewerkt. Heractiveer het eerst.");
        }

        var previousResponsible = incident.ResponsibleUserId;
        var (incidentType, severityValue) = await ValidateAsync(request, tenantId, cancellationToken);
        Apply(incident, request, incidentType, severityValue);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "Updated", null,
            new { incident.Title, Severity = severityValue.ToString() }, cancellationToken);
        if (incident.ResponsibleUserId != previousResponsible)
        {
            await NotifyResponsibleAsync(incident, cancellationToken);
        }

        return await MapDetailAsync(incident, cancellationToken);
    }

    public async Task<IncidentDetailDto?> ChangeStatusAsync(Guid id, ChangeIncidentStatusRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var incident = await _dbContext.Incidents
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        if (!TransportationService.Api.Common.EnumParsing.TryParseDefined<IncidentStatus>(request.Status, out var target))
        {
            throw new DomainValidationException("status", "Onbekende incidentstatus.");
        }

        if (!Transitions.TryGetValue(incident.Status, out var allowed) || !allowed.Contains(target))
        {
            throw new DomainValidationException("status",
                $"Een incident kan niet van {incident.Status} naar {target} gaan.");
        }

        var previousStatus = incident.Status;
        if (target == IncidentStatus.Resolved)
        {
            if (string.IsNullOrWhiteSpace(request.Resolution))
            {
                throw new DomainValidationException("resolution", "Een oplossing is verplicht bij het afhandelen.");
            }

            incident.Resolution = request.Resolution.Trim();
            incident.ResolvedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }
        else
        {
            // Reopening (or cancelling) leaves any earlier resolution text as history, but
            // the incident is no longer considered resolved.
            incident.ResolvedAt = null;
        }

        incident.Status = target;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "StatusChanged", null,
            new { From = previousStatus.ToString(), To = target.ToString() }, cancellationToken);

        return await MapDetailAsync(incident, cancellationToken);
    }

    private async Task<(IncidentType Type, IncidentSeverity Severity)> ValidateAsync(
        SaveIncidentRequest request, Guid tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new DomainValidationException("title", "Een titel is verplicht.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new DomainValidationException("description", "Een omschrijving is verplicht.");
        }

        // TryParseDefined: a numeric string would otherwise pass as an undefined enum value and be
        // persisted verbatim into the string-stored column.
        if (!EnumParsing.TryParseDefined<IncidentType>(request.IncidentType, out var incidentType))
        {
            throw new DomainValidationException("incidentType", "Onbekend incidenttype.");
        }

        if (incidentType == IncidentType.Other && string.IsNullOrWhiteSpace(request.CustomTypeName))
        {
            throw new DomainValidationException("customTypeName", "Geef het type een naam bij 'Overig'.");
        }

        if (!EnumParsing.TryParseDefined<IncidentSeverity>(request.Severity, out var severity))
        {
            throw new DomainValidationException("severity", "Onbekende ernst.");
        }

        if (request.EstimatedCost is < 0)
        {
            throw new DomainValidationException("estimatedCost", "De geschatte kost kan niet negatief zijn.");
        }

        if (request.ActualCost is < 0)
        {
            throw new DomainValidationException("actualCost", "De werkelijke kost kan niet negatief zijn.");
        }

        // Every provided link must resolve inside the tenant.
        if (request.ResponsibleUserId is { } userId
            && !await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive, cancellationToken))
        {
            throw new DomainValidationException("responsibleUserId", "De gekozen verantwoordelijke bestaat niet of is inactief.");
        }

        if (request.CustomerId is { } customerId
            && !await _dbContext.Customers.AnyAsync(c => c.TenantId == tenantId && c.Id == customerId, cancellationToken))
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        if (request.DriverId is { } driverId
            && !await _dbContext.Drivers.AnyAsync(d => d.TenantId == tenantId && d.Id == driverId, cancellationToken))
        {
            throw new DomainValidationException("driverId", "De gekozen chauffeur bestaat niet.");
        }

        if (request.VehicleId is { } vehicleId
            && !await _dbContext.Vehicles.AnyAsync(v => v.TenantId == tenantId && v.Id == vehicleId, cancellationToken))
        {
            throw new DomainValidationException("vehicleId", "Het gekozen voertuig bestaat niet.");
        }

        if (request.TrailerId is { } trailerId
            && !await _dbContext.Trailers.AnyAsync(t => t.TenantId == tenantId && t.Id == trailerId, cancellationToken))
        {
            throw new DomainValidationException("trailerId", "De gekozen oplegger bestaat niet.");
        }

        if (request.TransportOrderId is { } orderId
            && !await _dbContext.TransportOrders.AnyAsync(o => o.TenantId == tenantId && o.Id == orderId, cancellationToken))
        {
            throw new DomainValidationException("transportOrderId", "De gekozen transportopdracht bestaat niet.");
        }

        if (request.TripId is { } tripId
            && !await _dbContext.Trips.AnyAsync(t => t.TenantId == tenantId && t.Id == tripId, cancellationToken))
        {
            throw new DomainValidationException("tripId", "De gekozen rit bestaat niet.");
        }

        if (request.DossierId is { } dossierId
            && !await _dbContext.TransportDossiers.AnyAsync(d => d.TenantId == tenantId && d.Id == dossierId, cancellationToken))
        {
            throw new DomainValidationException("dossierId", "Het gekozen dossier bestaat niet.");
        }

        return (incidentType, severity);
    }

    // --- Wave 6 §4: the unified problem view -------------------------------------------------

    /// <summary>OPEN incidents + OPEN execution exceptions in one list — one place to work
    /// problems, each row linking to its own detail (incident page / trip).</summary>
    public async Task<IReadOnlyList<ProblemListItemDto>> ListProblemsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var incidents = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId
                        && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress))
            .Select(i => new
            {
                i.Id, i.Title, Severity = i.Severity.ToString(), Status = i.Status.ToString(),
                OccurredAt = i.CreatedAt, i.TransportOrderId, i.TripId, i.DossierId,
                i.ResponsibleParty, i.ChargeDecision,
            })
            .ToListAsync(cancellationToken);

        var exceptions = await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId
                        && (e.Status == Modules.Exceptions.Entities.ExecutionExceptionStatus.Open
                            || e.Status == Modules.Exceptions.Entities.ExecutionExceptionStatus.Investigating
                            || e.Status == Modules.Exceptions.Entities.ExecutionExceptionStatus.CustomerActionRequired))
            .Select(e => new
            {
                e.Id, Title = e.Type.ToString() + ": " + e.Description,
                Severity = e.Severity.ToString(), Status = e.Status.ToString(),
                e.OccurredAt, e.TransportOrderId, TripId = (Guid?)e.TripId,
            })
            .ToListAsync(cancellationToken);

        var orderIds = incidents.Select(i => i.TransportOrderId)
            .Concat(exceptions.Select(e => e.TransportOrderId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var orderNumbers = orderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, cancellationToken);
        var tripIds = incidents.Select(i => i.TripId).Concat(exceptions.Select(e => e.TripId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var tripNumbers = tripIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenantId && tripIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.TripNumber, cancellationToken);
        var dossierIds = incidents.Where(i => i.DossierId != null).Select(i => i.DossierId!.Value).Distinct().ToList();
        var dossierNumbers = dossierIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.TransportDossiers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && dossierIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.DossierNumber, cancellationToken);

        return incidents
            .Select(i => new ProblemListItemDto(
                i.Id, "Incident", i.Title, i.Severity, i.Status, i.OccurredAt,
                i.TransportOrderId is { } oid ? orderNumbers.GetValueOrDefault(oid) : null,
                i.TripId is { } tid ? tripNumbers.GetValueOrDefault(tid) : null, i.TripId,
                i.DossierId is { } did ? dossierNumbers.GetValueOrDefault(did) : null, i.DossierId,
                i.ResponsibleParty, i.ChargeDecision))
            .Concat(exceptions.Select(e => new ProblemListItemDto(
                e.Id, "Exception", e.Title, e.Severity, e.Status, e.OccurredAt,
                e.TransportOrderId is { } oid ? orderNumbers.GetValueOrDefault(oid) : null,
                e.TripId is { } tid ? tripNumbers.GetValueOrDefault(tid) : null, e.TripId,
                null, null)))
            .OrderByDescending(p => p.OccurredAt)
            .ToList();
    }

    // --- Wave 6 §2: approval-gated charge decision -------------------------------------------

    public async Task<IncidentDetailDto?> ProposeChargeAsync(
        Guid id, ProposeIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var incident = await _dbContext.Incidents.FirstOrDefaultAsync(
            i => i.TenantId == _tenantContext.TenantId && i.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Description))
        {
            throw new DomainValidationException("amount", "Geef een positief bedrag en een omschrijving op.");
        }

        if (incident.ChargeDecision == "Approved")
        {
            throw new DomainValidationException("Deze doorrekening is al goedgekeurd en kan niet meer worden gewijzigd.");
        }

        if (incident.ResponsibleParty != "Customer")
        {
            throw new DomainValidationException(
                "Alleen problemen met verantwoordelijkheid 'Klant' kunnen worden doorgerekend; interne kosten blijven intern.");
        }

        var before = new { incident.ChargeDecision, incident.ChargeAmount };
        incident.ChargeDecision = "Proposed";
        incident.ChargeAmount = decimal.Round(request.Amount, 2);
        incident.ChargeDescription = request.Description.Trim();
        incident.ChargeDecidedByUserId = null;
        incident.ChargeDecidedAt = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "ChargeProposed", before,
            new { incident.ChargeDecision, incident.ChargeAmount, incident.ChargeDescription }, cancellationToken);
        return await MapDetailAsync(incident, cancellationToken);
    }

    public async Task<IncidentDetailDto?> DecideChargeAsync(
        Guid id, DecideIncidentChargeRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var incident = await _dbContext.Incidents.FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        // Fail-closed (L7 house rule): no wired authorization = no approval rights, ever.
        var userId = _currentUser?.CurrentUserId;
        var allowed = _permissionService is not null && userId is { } uid
            && await _permissionService.UserHasPermissionAsync(
                uid, Modules.Identity.PermissionCodes.ProblemsApproveCharge, cancellationToken);
        if (!allowed)
        {
            throw new DomainValidationException("Je hebt geen rechten om doorrekeningen goed of af te keuren.");
        }

        if (incident.ChargeDecision != "Proposed")
        {
            throw new DomainValidationException("Er staat geen voorgestelde doorrekening open op dit incident.");
        }

        var before = new { incident.ChargeDecision, incident.ChargeAmount };
        incident.ChargeDecision = request.Approve ? "Approved" : "Rejected";
        incident.ChargeDecidedByUserId = userId;
        incident.ChargeDecidedAt = _timeProvider.GetUtcNow().UtcDateTime;

        string? lineNote = null;
        if (request.Approve && incident.TransportOrderId is { } chargeOrderId)
        {
            // The sales line lands on the linked order via the EXISTING manual-line mechanics
            // — from there the normal invoice flow picks it up. A locked/invoiced price
            // refuses; the approval stays recorded and Wave 10 surfaces it as manual work.
            var snapshot = await _dbContext.TransportOrderPricingSnapshots.FirstOrDefaultAsync(
                s => s.TenantId == tenantId && s.TransportOrderId == chargeOrderId, cancellationToken);
            if (snapshot is { Status: Modules.Orders.Entities.OrderPricingStatus.Locked
                or Modules.Orders.Entities.OrderPricingStatus.Invoiced })
            {
                lineNote = "De prijs van de gekoppelde order is vergrendeld/gefactureerd — voeg de lijn handmatig toe bij facturatie.";
            }
            else
            {
                var order = await _dbContext.TransportOrders.FirstOrDefaultAsync(
                    o => o.TenantId == tenantId && o.Id == chargeOrderId, cancellationToken);
                if (order is not null)
                {
                    var maxSequence = await _dbContext.TransportOrderPricingLines
                        .Where(l => l.TenantId == tenantId && l.TransportOrderId == chargeOrderId)
                        .Select(l => (int?)l.Sequence)
                        .MaxAsync(cancellationToken) ?? 0;
                    _dbContext.TransportOrderPricingLines.Add(new Modules.Orders.Entities.TransportOrderPricingLine
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = chargeOrderId,
                        Sequence = maxSequence + 1,
                        Label = incident.ChargeDescription ?? $"Doorrekening incident",
                        Amount = incident.ChargeAmount ?? 0m,
                        Source = "Manueel", Kind = Modules.Orders.Entities.OrderPriceLineKind.Manual,
                        AdjustReason = $"Incident: {incident.Title}",
                        AdjustedByUserId = userId,
                        AdjustedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                        LineKey = $"manual:{Guid.NewGuid()}",
                    });
                    if (!order.PriceIsManual)
                    {
                        order.AgreedPrice = (order.AgreedPrice ?? 0m) + (incident.ChargeAmount ?? 0m);
                    }

                    if (snapshot is not null)
                    {
                        snapshot.LinesTotal = (snapshot.LinesTotal ?? 0m) + (incident.ChargeAmount ?? 0m);
                    }

                    await Modules.Orders.Services.InvoiceReadinessEvaluator.EvaluateAsync(
                        _dbContext, order, cancellationToken);
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(),
            request.Approve ? "ChargeApproved" : "ChargeRejected", before,
            new { incident.ChargeDecision, incident.ChargeAmount, Note = lineNote }, cancellationToken);
        return await MapDetailAsync(incident, cancellationToken);
    }

    // --- Wave 6 §3: linked redelivery --------------------------------------------------------

    public async Task<IncidentDetailDto?> CreateRedeliveryAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var incident = await _dbContext.Incidents.FirstOrDefaultAsync(
            i => i.TenantId == tenantId && i.Id == id, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        if (incident.LinkedRedeliveryOrderId is not null)
        {
            throw new DomainValidationException("Voor dit incident bestaat al een herleveringsorder.");
        }

        if (incident.TransportOrderId is not { } originalOrderId)
        {
            throw new DomainValidationException("Koppel eerst de originele order aan het incident.");
        }

        var original = await _dbContext.TransportOrders
            .Include(o => o.Stops)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == originalOrderId, cancellationToken);
        if (original is null)
        {
            throw new DomainValidationException("De gekoppelde order bestaat niet meer.");
        }

        var settings = await _dbContext.TenantSettings.FirstOrDefaultAsync(
            s => s.TenantId == tenantId, cancellationToken);
        var redelivery = new Modules.Orders.Entities.TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = original.CustomerId,
            OrderDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
            Status = Modules.Orders.Entities.TransportOrderStatus.Draft,
            CustomerReference = $"HERLEVERING {original.OrderNumber}",
            GoodsDescription = original.GoodsDescription,
            Quantity = original.Quantity, QuantityUnit = original.QuantityUnit,
            QuantityUnitCode = original.QuantityUnitCode,
            WeightKg = original.WeightKg, VolumeM3 = original.VolumeM3,
            PalletCount = original.PalletCount, DistanceKm = original.DistanceKm,
            LoadingMeters = original.LoadingMeters,
            AdrRequired = original.AdrRequired, CraneRequired = original.CraneRequired,
            LegalEntityId = original.LegalEntityId,
            Notes = $"Herlevering wegens incident: {incident.Title}",
            Stops = original.Stops.Where(s => !s.IsDeleted).OrderBy(s => s.Sequence)
                .Select(s => new Modules.Orders.Entities.TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Sequence = s.Sequence,
                    StopType = s.StopType, LocationId = s.LocationId, LocationName = s.LocationName,
                    Address = s.Address, PostalCode = s.PostalCode, City = s.City, CountryCode = s.CountryCode,
                    Reference = s.Reference, Instructions = s.Instructions,
                })
                .ToList(),
        };
        _dbContext.TransportOrders.Add(redelivery);

        // Into the SAME dossier as the original (its wrapper or first user link) — one file.
        var dossierId = await _dbContext.TransportDossiers
                .Where(d => d.TenantId == tenantId && d.OriginTransportOrderId == originalOrderId)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken)
            ?? await _dbContext.DossierOrders
                .Where(l => l.TenantId == tenantId && l.TransportOrderId == originalOrderId)
                .OrderBy(l => l.CreatedAt)
                .Select(l => (Guid?)l.DossierId)
                .FirstOrDefaultAsync(cancellationToken)
            ?? incident.DossierId;
        if (dossierId is { } targetDossierId)
        {
            _dbContext.DossierOrders.Add(new Modules.Dossiers.Entities.DossierOrder
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                DossierId = targetDossierId, TransportOrderId = redelivery.Id,
            });
        }

        // Original packages flip to RedeliveryPlanned where the lifecycle allows it.
        var packages = await _dbContext.Packages
            .Where(p => p.TenantId == tenantId && p.TransportOrderId == originalOrderId)
            .ToListAsync(cancellationToken);
        foreach (var package in packages.Where(p =>
                     Modules.Packages.Services.PackageLifecycleMachine.IsAllowed(
                         p.CurrentLifecycleStatus, Modules.Packages.Entities.PackageLifecycleStatus.RedeliveryPlanned)))
        {
            package.CurrentLifecycleStatus = Modules.Packages.Entities.PackageLifecycleStatus.RedeliveryPlanned;
        }

        incident.LinkedRedeliveryOrderId = redelivery.Id;
        await Common.Persistence.TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => redelivery.OrderNumber = settings is null
                ? $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
                : $"{settings.OrderNumberPrefix}{settings.OrderNumberNextValue++:0000}",
            cancellationToken);
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "RedeliveryCreated", null,
            new { RedeliveryOrderId = redelivery.Id, redelivery.OrderNumber }, cancellationToken);
        return await MapDetailAsync(incident, cancellationToken);
    }

    private static void Apply(Incident incident, SaveIncidentRequest request, IncidentType incidentType, IncidentSeverity severity)
    {
        incident.Title = request.Title.Trim();
        incident.Description = request.Description.Trim();
        incident.IncidentType = incidentType;
        incident.CustomTypeName = incidentType == IncidentType.Other ? Trim(request.CustomTypeName) : null;
        incident.Severity = severity;
        incident.Cause = Trim(request.Cause);
        incident.ResponsibleUserId = request.ResponsibleUserId;
        incident.CustomerImpact = Trim(request.CustomerImpact);
        incident.OperationalImpact = Trim(request.OperationalImpact);
        incident.FinancialImpact = Trim(request.FinancialImpact);
        incident.EstimatedCost = request.EstimatedCost;
        incident.ActualCost = request.ActualCost;
        incident.CustomerId = request.CustomerId;
        incident.DriverId = request.DriverId;
        incident.VehicleId = request.VehicleId;
        incident.TrailerId = request.TrailerId;
        incident.TransportOrderId = request.TransportOrderId;
        incident.TripId = request.TripId;
        incident.DossierId = request.DossierId;
        incident.DueDate = request.DueDate;
        // Wave 6 §1: responsibility attribution (validated set; unknown input falls back).
        incident.ResponsibleParty = request.ResponsibleParty is "Customer" or "Own" or "Driver" or "Supplier"
            ? request.ResponsibleParty
            : "Unknown";
        incident.ResponsibilityNotes = Trim(request.ResponsibilityNotes);
    }

    private async Task NotifyResponsibleAsync(Incident incident, CancellationToken cancellationToken)
    {
        if (incident.ResponsibleUserId is { } responsibleId)
        {
            await _notificationService.NotifyAsync(responsibleId, "incident_assigned",
                $"Incident toegewezen: {incident.Title}",
                "Je bent verantwoordelijk gesteld voor een incident.",
                $"/incidents/{incident.Id}", cancellationToken);
        }
    }

    private async Task<IncidentDetailDto> MapDetailAsync(Incident incident, CancellationToken cancellationToken)
    {
        var responsibleName = incident.ResponsibleUserId is { } userId
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefaultAsync(cancellationToken)
            : null;
        var customerName = incident.CustomerId is { } customerId
            ? await _dbContext.Customers.AsNoTracking()
                .Where(c => c.Id == customerId).Select(c => (string?)c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var driverName = incident.DriverId is { } driverId
            ? await _dbContext.Drivers.AsNoTracking().Where(d => d.Id == driverId)
                .Join(_dbContext.Employees.AsNoTracking(), d => d.EmployeeId, e => e.Id,
                    (d, e) => (string?)(e.FirstName + " " + e.LastName))
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var vehicleLabel = incident.VehicleId is { } vehicleId
            ? await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.Id == vehicleId).Select(v => (string?)v.LicensePlate).FirstOrDefaultAsync(cancellationToken)
            : null;
        var trailerLabel = incident.TrailerId is { } trailerId
            ? await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.Id == trailerId).Select(t => (string?)t.LicensePlate).FirstOrDefaultAsync(cancellationToken)
            : null;
        var orderNumber = incident.TransportOrderId is { } orderId
            ? await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.Id == orderId).Select(o => (string?)o.OrderNumber).FirstOrDefaultAsync(cancellationToken)
            : null;
        var tripNumber = incident.TripId is { } tripId
            ? await _dbContext.Trips.AsNoTracking()
                .Where(t => t.Id == tripId).Select(t => (string?)t.TripNumber).FirstOrDefaultAsync(cancellationToken)
            : null;
        var dossierNumber = incident.DossierId is { } dossierId
            ? await _dbContext.TransportDossiers.AsNoTracking()
                .Where(d => d.Id == dossierId).Select(d => (string?)d.DossierNumber).FirstOrDefaultAsync(cancellationToken)
            : null;

        return new IncidentDetailDto(
            incident.Id, incident.Title, incident.Description,
            incident.IncidentType.ToString(), incident.CustomTypeName,
            incident.Status.ToString(), incident.Severity.ToString(), incident.Cause,
            incident.ResponsibleUserId, responsibleName,
            incident.CustomerImpact, incident.OperationalImpact, incident.FinancialImpact,
            incident.EstimatedCost, incident.ActualCost,
            incident.CustomerId, customerName,
            incident.DriverId, driverName,
            incident.VehicleId, vehicleLabel,
            incident.TrailerId, trailerLabel,
            incident.TransportOrderId, orderNumber,
            incident.TripId, tripNumber,
            incident.DossierId, dossierNumber,
            incident.DueDate, incident.Resolution, incident.ResolvedAt, incident.CreatedAt,
            Transitions.TryGetValue(incident.Status, out var allowed)
                ? allowed.Select(s => s.ToString()).ToList()
                : [],
            incident.ResponsibleParty, incident.ResponsibilityNotes,
            incident.ChargeDecision, incident.ChargeAmount, incident.ChargeDescription,
            incident.LinkedRedeliveryOrderId,
            incident.LinkedRedeliveryOrderId is { } redeliveryId
                ? await _dbContext.TransportOrders.AsNoTracking()
                    .Where(o => o.Id == redeliveryId).Select(o => (string?)o.OrderNumber).FirstOrDefaultAsync(cancellationToken)
                : null);
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
