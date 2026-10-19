using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Dtos;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Exceptions.Entities;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Incidents.Services;

/// <summary>A safe summary of an open problem on a vehicle, for the fleet (EX-03).</summary>
public record VehicleSafetyIssueDto(
    Guid Id, string Kind, string TypeLabel, string Title, string Severity, string Status, DateTime OccurredAt,
    string? TripNumber, string? OrderNumber, string? Route, string? DriverName,
    Guid? DamageReportId, bool SafetyIssue);

public interface IProblemFollowUpService
{
    /// <summary>Afwijking → Incident: idempotent; the incident carries every relation of the afwijking.</summary>
    Task<IncidentDetailDto?> EscalateExceptionAsync(Guid exceptionId, CancellationToken cancellationToken);

    /// <summary>EX-03: the fleet damage report for a vehicle-related incident (idempotent, linked back).</summary>
    Task<IncidentDetailDto?> CreateDamageReportForIncidentAsync(Guid incidentId, CreateLinkedDamageReportRequest request, CancellationToken cancellationToken);

    /// <summary>EX-03: the same for an afwijking (vehicle/trailer from its trip). Returns the damage report id.</summary>
    Task<Guid?> CreateDamageReportForExceptionAsync(Guid exceptionId, CreateLinkedDamageReportRequest request, CancellationToken cancellationToken);

    /// <summary>EX-07: who can be made responsible — internal staff that handles problems, safe fields only.</summary>
    Task<IReadOnlyList<AssignableUserDto>> AssignableUsersAsync(CancellationToken cancellationToken);

    /// <summary>Domain history (when / who / what) of an incident or afwijking; financial fields redacted
    /// unless the caller may see them. Null when the record does not exist.</summary>
    Task<AuditLogPagedResult?> HistoryAsync(string entityType, Guid id, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>EX-03: open problems involving a vehicle, for the fleet — no customer, cost or HR data.</summary>
    Task<IReadOnlyList<VehicleSafetyIssueDto>> VehicleSafetyIssuesAsync(Guid vehicleId, CancellationToken cancellationToken);
}

public class ProblemFollowUpService : IProblemFollowUpService
{
    /// <summary>Permissions that make someone a problem handler (and therefore assignable).</summary>
    private static readonly string[] HandlerPermissions =
        [PermissionCodes.IncidentsManage, PermissionCodes.ExceptionsResolve];

    /// <summary>Audit value keys only roles with financial insight may read in a problem history.</summary>
    private static readonly string[] FinancialKeys =
    [
        nameof(Incident.ChargeAmount), nameof(Incident.ChargeDescription), nameof(Incident.EstimatedCost),
        nameof(Incident.ActualCost), nameof(Incident.FinancialImpact), "Amount", "Note",
    ];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IIncidentService _incidents;
    private readonly IDamageReportService _damage;
    private readonly IAuditService _audit;
    private readonly IAuditHistoryReader _history;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionAuthorizationService _permissions;

    public ProblemFollowUpService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IIncidentService incidents,
        IDamageReportService damage, IAuditService audit, IAuditHistoryReader history,
        ICurrentUserContext currentUser, IPermissionAuthorizationService permissions)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _incidents = incidents;
        _damage = damage;
        _audit = audit;
        _history = history;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    private Guid TenantId => _tenantContext.TenantId;

    public async Task<IncidentDetailDto?> EscalateExceptionAsync(Guid exceptionId, CancellationToken cancellationToken)
    {
        var exception = await _dbContext.ExecutionExceptions
            .FirstOrDefaultAsync(e => e.TenantId == TenantId && e.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return null;
        }

        // Idempotent: the afwijking already became an incident.
        var existing = exception.EscalatedIncidentId
                       ?? await _dbContext.Incidents.AsNoTracking()
                           .Where(i => i.TenantId == TenantId && i.SourceExceptionId == exceptionId)
                           .Select(i => (Guid?)i.Id)
                           .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } incidentId)
        {
            return await _incidents.GetAsync(incidentId, cancellationToken);
        }

        if (exception.Status == ExecutionExceptionStatus.Rejected)
        {
            throw new DomainValidationException("Een afgewezen afwijking kan niet naar een incident worden geëscaleerd.");
        }

        var trip = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == TenantId && t.Id == exception.TripId)
            .Select(t => new { t.TripNumber, t.DriverId, t.VehicleId, t.TrailerId })
            .FirstAsync(cancellationToken);
        var label = DossierTimelineLabels.ExceptionType(exception.Type);
        var (type, customName) = IncidentTypeFor(exception.Type, label);
        var description = exception.Quantity is { } quantity
            ? $"{exception.Description}\n\nGemelde hoeveelheid: {quantity:0.##}."
            : exception.Description;

        var incident = await _incidents.CreateFromExceptionAsync(new SaveIncidentRequest(
            Title: $"{label} — {trip.TripNumber}",
            Description: description,
            IncidentType: type.ToString(),
            Severity: exception.Severity.ToString(),
            CustomTypeName: customName,
            ResponsibleUserId: exception.AssignedToUserId,
            TransportOrderId: exception.TransportOrderId,
            TripId: exception.TripId,
            DriverId: exception.DriverId ?? trip.DriverId,
            VehicleId: trip.VehicleId,
            TrailerId: trip.TrailerId,
            CustomerMessage: exception.CustomerMessage), exceptionId, cancellationToken);

        exception.EscalatedIncidentId = incident.Id;
        var previousStatus = exception.Status;
        if (exception.Status == ExecutionExceptionStatus.Open)
        {
            exception.Status = ExecutionExceptionStatus.Investigating;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync("ExecutionException", exception.Id.ToString(), "EscalatedToIncident",
            new { Status = previousStatus.ToString() },
            new { IncidentId = incident.Id, incident.Title, Status = exception.Status.ToString() }, cancellationToken);
        await _audit.RecordAsync("Incident", incident.Id.ToString(), "EscalatedFromException", null,
            new { ExceptionId = exception.Id, Type = label }, cancellationToken);
        return await _incidents.GetAsync(incident.Id, cancellationToken);
    }

    /// <summary>The incident type an afwijking type escalates to; the Dutch label names an "Overig" type.</summary>
    private static (IncidentType Type, string? CustomName) IncidentTypeFor(ExecutionExceptionType type, string label) => type switch
    {
        ExecutionExceptionType.DamagedPackage => (IncidentType.Damage, null),
        ExecutionExceptionType.MissingPackage or ExecutionExceptionType.IncompleteQuantity or ExecutionExceptionType.PartialDelivery
            => (IncidentType.MissingGoods, null),
        ExecutionExceptionType.WrongPackage or ExecutionExceptionType.UnknownPackage or ExecutionExceptionType.WrongRoutePackage
            or ExecutionExceptionType.WrongStopPackage => (IncidentType.WrongDelivery, null),
        ExecutionExceptionType.VehicleIssue => (IncidentType.VehicleBreakdown, null),
        ExecutionExceptionType.Delay => (IncidentType.Delay, null),
        ExecutionExceptionType.UnreadableBarcode or ExecutionExceptionType.LabelMissing => (IncidentType.Administrative, null),
        _ => (IncidentType.Other, label),
    };

    public async Task<IncidentDetailDto?> CreateDamageReportForIncidentAsync(
        Guid incidentId, CreateLinkedDamageReportRequest request, CancellationToken cancellationToken)
    {
        var incident = await _dbContext.Incidents.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == TenantId && i.Id == incidentId, cancellationToken);
        if (incident is null)
        {
            return null;
        }

        if (incident.VehicleId is null && incident.TrailerId is null)
        {
            throw new DomainValidationException("vehicleId", "Koppel eerst het voertuig of de oplegger aan het incident.");
        }

        // Idempotent: a second click returns the incident with its existing report — and is not history.
        if (await _dbContext.DamageReports.AsNoTracking().AnyAsync(d => d.TenantId == TenantId && d.SourceIncidentId == incident.Id, cancellationToken))
        {
            return await _incidents.GetAsync(incident.Id, cancellationToken);
        }

        var severity = ParseSeverity(request.Severity);
        var tripId = await TripMatchingAsync(incident.TripId, incident.VehicleId, incident.TrailerId, cancellationToken);
        var result = await _damage.CreateLinkedAsync(
            incident.VehicleId, incident.VehicleId is null ? incident.TrailerId : null,
            new CreateDamageReportRequest(
                incident.DriverId,
                // The tenant-local day the problem was reported on, not the UTC date of the instant.
                Common.TenantTimeZone.ToLocalDate(incident.CreatedAt, await Common.TenantTimeZone.ForTenantAsync(_dbContext, TenantId, cancellationToken)),
                string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
                Describe(request.Description, $"{incident.Title}: {incident.Description}"),
                severity,
                InsuranceReference: null,
                Notes: $"Aangemaakt vanuit incident '{incident.Title}'.",
                TripId: tripId),
            sourceIncidentId: incident.Id, sourceExceptionId: null, cancellationToken);
        var damageId = RequireSuccess(result);
        await _audit.RecordAsync("Incident", incident.Id.ToString(), "DamageReportCreated", null,
            new { DamageReportId = damageId, Severity = severity.ToString() }, cancellationToken);
        return await _incidents.GetAsync(incident.Id, cancellationToken);
    }

    public async Task<Guid?> CreateDamageReportForExceptionAsync(
        Guid exceptionId, CreateLinkedDamageReportRequest request, CancellationToken cancellationToken)
    {
        var exception = await _dbContext.ExecutionExceptions.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == TenantId && e.Id == exceptionId, cancellationToken);
        if (exception is null)
        {
            return null;
        }

        if (await _dbContext.DamageReports.AsNoTracking()
                .Where(d => d.TenantId == TenantId && d.SourceExceptionId == exception.Id)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken) is { } existingId)
        {
            return existingId;
        }

        var trip = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == TenantId && t.Id == exception.TripId)
            .Select(t => new { t.Id, t.DriverId, t.VehicleId, t.TrailerId })
            .FirstAsync(cancellationToken);
        if (trip.VehicleId is null && trip.TrailerId is null)
        {
            throw new DomainValidationException("Aan de rit van deze afwijking is geen voertuig of oplegger gekoppeld.");
        }

        var severity = ParseSeverity(request.Severity);
        var label = DossierTimelineLabels.ExceptionType(exception.Type);
        var result = await _damage.CreateLinkedAsync(
            trip.VehicleId, trip.VehicleId is null ? trip.TrailerId : null,
            new CreateDamageReportRequest(
                exception.DriverId ?? trip.DriverId,
                Common.TenantTimeZone.ToLocalDate(exception.OccurredAt, await Common.TenantTimeZone.ForTenantAsync(_dbContext, TenantId, cancellationToken)),
                string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
                Describe(request.Description, $"{label}: {exception.Description}"),
                severity,
                InsuranceReference: null,
                Notes: $"Aangemaakt vanuit afwijking '{label}'.",
                TripId: trip.Id),
            sourceIncidentId: null, sourceExceptionId: exception.Id, cancellationToken);
        var damageId = RequireSuccess(result);
        await _audit.RecordAsync("ExecutionException", exception.Id.ToString(), "DamageReportCreated", null,
            new { DamageReportId = damageId, Severity = severity.ToString() }, cancellationToken);
        return damageId;
    }

    private static DamageSeverity ParseSeverity(string? value) =>
        EnumParsing.TryParseDefined<DamageSeverity>(value, out var severity)
            ? severity
            : throw new DomainValidationException("severity", "Kies de ernst van de schade.");

    private static string Describe(string? requested, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
        return text.Length <= 2000 ? text : text[..2000];
    }

    private static Guid RequireSuccess(DamageOperationResult result) => result.Outcome switch
    {
        DamageOperationOutcome.Success => result.Report!.Id,
        DamageOperationOutcome.OwnerNotFound => throw new DomainValidationException("Het voertuig of de oplegger bestaat niet meer."),
        DamageOperationOutcome.InvalidReference => throw new DomainValidationException("De chauffeur van dit probleem bestaat niet meer."),
        _ => throw new DomainValidationException(result.Error ?? "De schademelding kon niet worden aangemaakt."),
    };

    /// <summary>The trip goes along only when it really carried this vehicle/trailer (FL-M7 rule).</summary>
    private async Task<Guid?> TripMatchingAsync(Guid? tripId, Guid? vehicleId, Guid? trailerId, CancellationToken cancellationToken)
    {
        if (tripId is not { } id)
        {
            return null;
        }

        var trip = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == TenantId && t.Id == id)
            .Select(t => new { t.VehicleId, t.TrailerId })
            .FirstOrDefaultAsync(cancellationToken);
        return trip is not null && ((vehicleId is not null && trip.VehicleId == vehicleId) || (vehicleId is null && trailerId is not null && trip.TrailerId == trailerId))
            ? id
            : null;
    }

    public async Task<IReadOnlyList<AssignableUserDto>> AssignableUsersAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var handlerCodes = HandlerPermissions;
        var rows = await (
                from ur in _dbContext.UserRoles.AsNoTracking()
                join u in _dbContext.Users.AsNoTracking()
                        .Where(u => u.TenantId == tenantId && u.IsActive)
                        .Where(PortalPermissionScope.InternalIdentityOnly)
                    on ur.UserId equals u.Id
                join r in _dbContext.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.IsActive) on ur.RoleId equals r.Id
                select new { u.Id, u.FirstName, u.LastName, RoleId = r.Id, RoleName = r.Name, r.IsSystemRole })
            .ToListAsync(cancellationToken);

        var roleIds = rows.Select(r => r.RoleId).Distinct().ToList();
        var handlerRoleIds = (await (
                    from rp in _dbContext.RolePermissions.AsNoTracking()
                    join p in _dbContext.Permissions.AsNoTracking().Where(p => handlerCodes.Contains(p.Code)) on rp.PermissionId equals p.Id
                    where roleIds.Contains(rp.RoleId)
                    select rp.RoleId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return rows
            .Where(r => r.IsSystemRole || handlerRoleIds.Contains(r.RoleId))
            .GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.OrderByDescending(r => handlerRoleIds.Contains(r.RoleId)).First();
                return new AssignableUserDto(g.Key, $"{first.FirstName} {first.LastName}".Trim(), first.RoleName);
            })
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<AuditLogPagedResult?> HistoryAsync(
        string entityType, Guid id, int page, int pageSize, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var exists = entityType switch
        {
            "Incident" => await _dbContext.Incidents.AnyAsync(i => i.TenantId == tenantId && i.Id == id, cancellationToken),
            "ExecutionException" => await _dbContext.ExecutionExceptions.AnyAsync(e => e.TenantId == tenantId && e.Id == id, cancellationToken),
            _ => false,
        };
        if (!exists)
        {
            return null;
        }

        var key = id.ToString();
        var rows = _dbContext.AuditLogs.Where(a => a.EntityType == entityType && a.EntityId == key);
        var mayReadFinancials = _currentUser.CurrentUserId is { } userId
            && (await _permissions.UserHasPermissionAsync(userId, PermissionCodes.ProblemsApproveCharge, cancellationToken)
                || await _permissions.UserHasPermissionAsync(userId, PermissionCodes.InvoicesView, cancellationToken)
                || await _permissions.UserHasPermissionAsync(userId, PermissionCodes.AuditLogsView, cancellationToken));
        return await _history.ReadAsync(rows, page, pageSize, mayReadFinancials ? null : FinancialKeys, cancellationToken);
    }

    public async Task<IReadOnlyList<VehicleSafetyIssueDto>> VehicleSafetyIssuesAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var openIncidentStatuses = ProblemScope.OpenIncidentStatuses;
        var openExceptionStatuses = ProblemScope.OpenExceptionStatuses;
        var incidents = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.VehicleId == vehicleId && openIncidentStatuses.Contains(i.Status))
            .ToListAsync(cancellationToken);
        var tripIds = await _dbContext.Trips.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.VehicleId == vehicleId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var exceptions = await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && tripIds.Contains(e.TripId) && openExceptionStatuses.Contains(e.Status)
                        && e.Type == ExecutionExceptionType.VehicleIssue)
            .ToListAsync(cancellationToken);
        var damageBySource = await _dbContext.DamageReports.AsNoTracking()
            .Where(d => d.TenantId == tenantId && (d.SourceIncidentId != null || d.SourceExceptionId != null))
            .Select(d => new { d.Id, d.SourceIncidentId, d.SourceExceptionId })
            .ToListAsync(cancellationToken);

        var result = new List<VehicleSafetyIssueDto>();
        // Vehicle problems only: a breakdown/accident, a serious safety issue, or anything that already
        // produced a damage report — cargo damage on a trip is not the fleet's business.
        foreach (var incident in incidents.Where(i => VehicleSafetyRules.IsVehicleType(i.IncidentType)
                                                      || VehicleSafetyRules.IsSafetyIssue(i)
                                                      || damageBySource.Any(d => d.SourceIncidentId == i.Id)))
        {
            var (trip, order, route, driver) = await ContextAsync(incident.TripId, incident.TransportOrderId, incident.DriverId, cancellationToken);
            result.Add(new VehicleSafetyIssueDto(
                incident.Id, "Incident", DossierTimelineLabels.IncidentType(incident.IncidentType, incident.CustomTypeName),
                incident.Title, incident.Severity.ToString(), incident.Status.ToString(), incident.CreatedAt,
                trip, order, route, driver,
                damageBySource.FirstOrDefault(d => d.SourceIncidentId == incident.Id)?.Id,
                VehicleSafetyRules.IsSafetyIssue(incident)));
        }

        foreach (var exception in exceptions)
        {
            var (trip, order, route, driver) = await ContextAsync(exception.TripId, exception.TransportOrderId, exception.DriverId, cancellationToken);
            var label = DossierTimelineLabels.ExceptionType(exception.Type);
            result.Add(new VehicleSafetyIssueDto(
                exception.Id, "Exception", label, label, exception.Severity.ToString(), exception.Status.ToString(), exception.OccurredAt,
                trip, order, route, driver,
                damageBySource.FirstOrDefault(d => d.SourceExceptionId == exception.Id)?.Id,
                VehicleSafetyRules.IsSafetyIssue(exception)));
        }

        return result.OrderByDescending(r => r.OccurredAt).ToList();
    }

    private async Task<(string? Trip, string? Order, string? Route, string? Driver)> ContextAsync(
        Guid? tripId, Guid? orderId, Guid? driverId, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var trip = tripId is { } t
            ? await _dbContext.Trips.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == t)
                .Select(x => new { x.TripNumber, x.DriverId }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var order = orderId is { } o
            ? await _dbContext.TransportOrders.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == o)
                .Select(x => new
                {
                    x.OrderNumber,
                    From = x.Stops.Where(s => s.StopType == Orders.Entities.StopType.Loading).OrderBy(s => s.Sequence).Select(s => s.City).FirstOrDefault(),
                    To = x.Stops.Where(s => s.StopType == Orders.Entities.StopType.Unloading).OrderByDescending(s => s.Sequence).Select(s => s.City).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var driver = (driverId ?? trip?.DriverId) is { } d
            ? await _dbContext.Drivers.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == d)
                .Join(_dbContext.Employees.AsNoTracking(), x => x.EmployeeId, e => e.Id, (x, e) => e.FirstName + " " + e.LastName)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var route = order is { From: not null, To: not null } ? $"{order.From} → {order.To}" : null;
        return (trip?.TripNumber, order?.OrderNumber, route, driver);
    }
}
