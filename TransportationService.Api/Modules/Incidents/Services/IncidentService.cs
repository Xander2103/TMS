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

    public IncidentService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        INotificationService notificationService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<IncidentListItemDto>> ListAsync(
        string? search, string? status, string? severity, Guid? dossierId, Guid? customerId,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var query = _dbContext.Incidents.AsNoTracking().Where(i => i.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var parsedStatus))
            {
                throw new DomainValidationException("status", "Onbekende incidentstatus.");
            }

            query = query.Where(i => i.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            if (!Enum.TryParse<IncidentSeverity>(severity, ignoreCase: true, out var parsedSeverity))
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

        if (!Enum.TryParse<IncidentStatus>(request.Status, ignoreCase: true, out var target))
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

        if (!Enum.TryParse<IncidentType>(request.IncidentType, ignoreCase: true, out var incidentType))
        {
            throw new DomainValidationException("incidentType", "Onbekend incidenttype.");
        }

        if (incidentType == IncidentType.Other && string.IsNullOrWhiteSpace(request.CustomTypeName))
        {
            throw new DomainValidationException("customTypeName", "Geef het type een naam bij 'Overig'.");
        }

        if (!Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity))
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
                : []);
    }

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
