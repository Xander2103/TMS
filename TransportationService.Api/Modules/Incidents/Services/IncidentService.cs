using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Incidents.Services;

public interface IIncidentService
{
    /// <summary>
    /// One page of the incident register. PERF-H3: the list used to be cut at 500 rows without a
    /// word; it is now paged server-side (<see cref="PageRequest"/>, max 200 per page) and sorted
    /// server-side on the keys the register offers (<see cref="IncidentListPage"/>), so
    /// <c>TotalCount</c> is the real count and every row can be reached.
    /// </summary>
    Task<PagedResult<IncidentListItemDto>> ListAsync(
        string? search, string? status, string? severity, Guid? dossierId, Guid? customerId,
        CancellationToken cancellationToken, Guid? transportOrderId = null, Guid? tripId = null,
        IncidentListFilter? filter = null, IncidentListPage? page = null);

    Task<IncidentDetailDto> CreateAsync(SaveIncidentRequest request, CancellationToken cancellationToken);

    /// <summary>Afwijking → Incident escalation: the new incident remembers its source afwijking.</summary>
    Task<IncidentDetailDto> CreateFromExceptionAsync(SaveIncidentRequest request, Guid sourceExceptionId, CancellationToken cancellationToken);

    Task<IncidentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>EX-01: section-wise, never-destructive office update (see <see cref="UpdateIncidentRequest"/>).</summary>
    Task<IncidentDetailDto?> UpdateAsync(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken);

    Task<IncidentDetailDto?> ChangeStatusAsync(Guid id, ChangeIncidentStatusRequest request, CancellationToken cancellationToken);

    // Wave 6: charge decision + linked redelivery + unified problem list.
    Task<IncidentDetailDto?> ProposeChargeAsync(Guid id, ProposeIncidentChargeRequest request, CancellationToken cancellationToken);
    Task<IncidentDetailDto?> DecideChargeAsync(Guid id, DecideIncidentChargeRequest request, CancellationToken cancellationToken);
    Task<IncidentDetailDto?> CreateRedeliveryAsync(Guid id, CancellationToken cancellationToken, CreateRedeliveryRequest? request = null);
    Task<IReadOnlyList<ProblemListItemDto>> ListProblemsAsync(CancellationToken cancellationToken);

    /// <summary>Incidents stamped with this driver (driver-app "my incidents" list).</summary>
    Task<IReadOnlyList<IncidentListItemDto>> ListForDriverAsync(Guid driverId, CancellationToken cancellationToken);
}

/// <summary>Remediation filters for the incident register (operational, not a list redesign).</summary>
public sealed record IncidentListFilter(
    Guid? ResponsibleUserId = null, Guid? DriverId = null, Guid? VehicleId = null,
    DateOnly? From = null, DateOnly? To = null, bool OpenOnly = false);

/// <summary>
/// Paging and sort of the incident register. <paramref name="Sort"/> is one of
/// <see cref="SortKeys"/> (anything else = newest first); <paramref name="Dir"/> "asc"/"desc".
/// </summary>
public sealed record IncidentListPage(PageRequest Page, string? Sort = null, string? Dir = null)
{
    public const string DefaultSort = "created";
    public static readonly IReadOnlyCollection<string> SortKeys = ["created", "title", "severity", "customer", "due", "status"];
    public static readonly IReadOnlyCollection<string> DescendingByDefault = ["created", "severity"];

    public static IncidentListPage Of(int? page, int? pageSize, string? sort = null, string? dir = null) =>
        new(PageRequest.Of(page, pageSize), sort, dir);
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
    // Resolved lazily (redelivery "gratis"): the order service sits downstream of trip execution,
    // which already depends on this service through the failed-delivery automation.
    private readonly IServiceProvider? _services;

    public IncidentService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        INotificationService notificationService,
        TimeProvider timeProvider,
        Modules.Identity.Services.IPermissionAuthorizationService? permissionService = null,
        Modules.Identity.Services.ICurrentUserContext? currentUser = null,
        Modules.Messaging.Services.INotificationEventService? notificationEvents = null,
        IServiceProvider? services = null)
    {
        _notificationEvents = notificationEvents;
        _services = services;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
        _permissionService = permissionService;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<IncidentListItemDto>> ListAsync(
        string? search, string? status, string? severity, Guid? dossierId, Guid? customerId,
        CancellationToken cancellationToken, Guid? transportOrderId = null, Guid? tripId = null,
        IncidentListFilter? filter = null, IncidentListPage? page = null)
    {
        var tenantId = _tenantContext.TenantId;
        page ??= IncidentListPage.Of(null, null);
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
            // EX-04: the dossier's incidents include those linked only through its orders/trips.
            var scope = await ProblemScope.ForDossierAsync(_dbContext, tenantId, did, cancellationToken);
            var scoped = ProblemScope.Incidents(_dbContext, tenantId, scope).Select(i => i.Id);
            query = query.Where(i => scoped.Contains(i.Id));
        }

        if (filter is not null)
        {
            if (filter.OpenOnly)
            {
                var open = ProblemScope.OpenIncidentStatuses;
                query = query.Where(i => open.Contains(i.Status));
            }

            if (filter.ResponsibleUserId is { } responsible) query = query.Where(i => i.ResponsibleUserId == responsible);
            if (filter.DriverId is { } driver) query = query.Where(i => i.DriverId == driver);
            if (filter.VehicleId is { } vehicle) query = query.Where(i => i.VehicleId == vehicle);
            if (filter.From is { } from)
            {
                var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                query = query.Where(i => i.CreatedAt >= fromUtc);
            }

            if (filter.To is { } to)
            {
                var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                query = query.Where(i => i.CreatedAt < toUtc);
            }
        }

        if (customerId is { } cid)
        {
            query = query.Where(i => i.CustomerId == cid);
        }

        // M12: the order and trip pages show the incidents about them.
        if (transportOrderId is { } oid)
        {
            query = query.Where(i => i.TransportOrderId == oid);
        }

        if (tripId is { } tid)
        {
            query = query.Where(i => i.TripId == tid);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(i => i.Title.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var projected = query.Select(i => new
        {
            i.Id, i.Title, i.IncidentType, i.CustomTypeName, i.Status, i.Severity, i.DueDate, i.CreatedAt,
            CustomerName = _dbContext.Customers
                .Where(c => c.Id == i.CustomerId).Select(c => (string?)c.Name).FirstOrDefault(),
            ResponsibleName = _dbContext.Users
                .Where(u => u.Id == i.ResponsibleUserId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefault(),
            DossierNumber = _dbContext.TransportDossiers
                .Where(d => d.Id == i.DossierId).Select(d => (string?)d.DossierNumber).FirstOrDefault(),
            OrderNumber = _dbContext.TransportOrders
                .Where(o => o.Id == i.TransportOrderId).Select(o => (string?)o.OrderNumber).FirstOrDefault(),
            TripNumber = _dbContext.Trips
                .Where(t => t.Id == i.TripId).Select(t => (string?)t.TripNumber).FirstOrDefault(),
            DriverName = _dbContext.Drivers.Where(d => d.Id == i.DriverId)
                .Join(_dbContext.Employees, d => d.EmployeeId, e => e.Id, (d, e) => (string?)(e.FirstName + " " + e.LastName))
                .FirstOrDefault(),
            VehicleLabel = _dbContext.Vehicles
                .Where(v => v.Id == i.VehicleId).Select(v => (string?)v.LicensePlate).FirstOrDefault(),
        });

        // Server-side sort on the register's columns (the UI used to sort its ≤ 500 rows itself).
        // Every key ends on CreatedAt + Id so a page boundary is deterministic. Enums are stored as
        // text: Status sorts alphabetically (as the UI did), Severity by rank Low → Critical.
        var listSort = ListSort.Parse(page.Sort, page.Dir, IncidentListPage.DefaultSort, IncidentListPage.SortKeys, IncidentListPage.DescendingByDefault);
        var ordered = listSort.Key switch
        {
            "title" => projected.OrderBy(r => r.Title, listSort.Descending),
            "severity" => projected.OrderBy(
                r => r.Severity == IncidentSeverity.Low ? 0 : r.Severity == IncidentSeverity.Medium ? 1 : r.Severity == IncidentSeverity.High ? 2 : 3,
                listSort.Descending),
            // Rows without a customer / due date sort last in both directions.
            "customer" => projected.OrderBy(r => r.CustomerName == null).ThenBy(r => r.CustomerName, listSort.Descending),
            "due" => projected.OrderBy(r => r.DueDate == null).ThenBy(r => r.DueDate, listSort.Descending),
            "status" => projected.OrderBy(r => r.Status, listSort.Descending),
            _ => projected.OrderBy(r => r.CreatedAt, listSort.Descending),
        };

        var rows = await ordered
            .ThenByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(page.Page.Skip)
            .Take(page.Page.PageSize)
            .ToListAsync(cancellationToken);

        var today = Today;
        var items = rows
            .Select(r => new IncidentListItemDto(
                r.Id, r.Title, r.IncidentType.ToString(), r.CustomTypeName,
                r.Status.ToString(), r.Severity.ToString(),
                r.CustomerName, r.ResponsibleName, r.DossierNumber, r.DueDate,
                IsOverdue: r.DueDate < today && ProblemScope.IsOpen(r.Status),
                r.CreatedAt)
            {
                OrderNumber = r.OrderNumber, TripNumber = r.TripNumber, DriverName = r.DriverName, VehicleLabel = r.VehicleLabel,
            })
            .ToList();

        return new PagedResult<IncidentListItemDto>(items, totalCount, page.Page.Page, page.Page.PageSize);
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

    public async Task<IncidentDetailDto> CreateAsync(SaveIncidentRequest request, CancellationToken cancellationToken) =>
        await CreateCoreAsync(request, sourceExceptionId: null, cancellationToken);

    public async Task<IncidentDetailDto> CreateFromExceptionAsync(
        SaveIncidentRequest request, Guid sourceExceptionId, CancellationToken cancellationToken) =>
        await CreateCoreAsync(request, sourceExceptionId, cancellationToken);

    private async Task<IncidentDetailDto> CreateCoreAsync(
        SaveIncidentRequest request, Guid? sourceExceptionId, CancellationToken cancellationToken)
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

        var report = new IncidentReportSection(
            request.Title, request.Description, request.IncidentType, request.Severity,
            request.CustomTypeName, request.Cause, request.ResponsibleUserId, request.DueDate);
        var (incidentType, severityValue) = await ValidateReportAsync(report, tenantId, cancellationToken);
        ValidateCosts(request.EstimatedCost, request.ActualCost);
        var links = await ResolveLinksAsync(
            LinkState.Empty,
            new LinkState(request.CustomerId, request.DossierId, request.TransportOrderId, request.TripId,
                request.DriverId, request.VehicleId, request.TrailerId),
            cancellationToken);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientRequestId = request.ClientRequestId,
            SourceExceptionId = sourceExceptionId,
        };
        ApplyReport(incident, report, incidentType, severityValue);
        ApplyLinks(incident, links);
        ApplyImpact(incident, new IncidentImpactSection(
            request.CustomerImpact, request.OperationalImpact, request.FinancialImpact, request.EstimatedCost, request.ActualCost));
        ApplyResponsibility(incident, new IncidentResponsibilitySection(request.ResponsibleParty, request.ResponsibilityNotes));
        incident.CustomerMessage = Trim(request.CustomerMessage);
        _dbContext.Add(incident);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The relations travel into the audit row: the domain history names trip, driver, order, ….
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "Created", null,
            new
            {
                incident.Title, IncidentType = incidentType.ToString(), Severity = severityValue.ToString(),
                incident.TripId, incident.DriverId, incident.VehicleId, incident.TransportOrderId, incident.DossierId,
                incident.CustomerId, incident.SourceExceptionId,
            },
            cancellationToken);
        await NotifyResponsibleAsync(incident, cancellationToken);

        // P5: an incident born with customer responsibility triggers the charge policy too.
        if (incident.ResponsibleParty == "Customer" && incident.ChargeDecision == "None")
        {
            await ApplyChargePolicyAsync(incident, cancellationToken);
        }

        // P9: registered damage on a linked order raises the sensitive event — its customer
        // mail is held for dispatcher review by default (catalog DefaultRequiresReview).
        if (incidentType == IncidentType.Damage && incident.TransportOrderId is { } damagedOrderId
            && _notificationEvents is not null)
        {
            try
            {
                var damaged = await _dbContext.TransportOrders.AsNoTracking()
                    .Where(o => o.TenantId == tenantId && o.Id == damagedOrderId)
                    .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId),
                        o => o.CustomerId, c => c.Id,
                        (o, c) => new { o.OrderNumber, o.GoodsDescription, CustomerId = c.Id, CustomerName = c.Name })
                    .FirstOrDefaultAsync(cancellationToken);
                if (damaged is not null)
                {
                    // EX-02: every placeholder of the template is filled; the staff copy carries the
                    // internal report, the customer copy only customer-safe wording.
                    await _notificationEvents.PublishAsync(
                        Modules.Messaging.Entities.MessageKinds.OrderDamageRegistered,
                        new Modules.Messaging.Services.NotificationEventContext(
                            "Incident", incident.Id.ToString(),
                            new Dictionary<string, string>
                            {
                                ["orderNumber"] = damaged.OrderNumber,
                                ["customerName"] = damaged.CustomerName,
                                ["goodsDescription"] = string.IsNullOrWhiteSpace(damaged.GoodsDescription) ? "uw zending" : damaged.GoodsDescription,
                                ["reason"] = incident.Title,
                                ["details"] = incident.Description,
                            })
                        {
                            CustomerId = damaged.CustomerId,
                            LinkPath = $"/incidents/{incident.Id}",
                            CustomerTokens = new Dictionary<string, string>
                            {
                                ["reason"] = incident.CustomerMessage ?? Modules.Messaging.Services.CustomerSafeText.DamageRegistered,
                                ["details"] = incident.CustomerMessage ?? Modules.Messaging.Services.CustomerSafeText.DamageRegistered,
                            },
                        },
                        cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never let a notification failure break the committed incident.
            }
        }

        await ProblemSignals.AfterIncidentRegisteredAsync(
            _dbContext, tenantId, incident, _notificationService, _auditService, cancellationToken);

        return (await GetAsync(incident.Id, cancellationToken))!;
    }

    private readonly Modules.Messaging.Services.INotificationEventService? _notificationEvents;

    public async Task<IncidentDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var incident = await _dbContext.Incidents.AsNoTracking()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == id, cancellationToken);
        return incident is null ? null : await MapDetailAsync(incident, cancellationToken);
    }

    public async Task<IncidentDetailDto?> UpdateAsync(Guid id, UpdateIncidentRequest request, CancellationToken cancellationToken)
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

        var before = Snapshot(incident);
        var previousLinks = LinkState.Of(incident);

        // EX-01: every section is applied ONLY when the caller sent it. A card that edits the severity
        // never touches the relations; the relations card never touches the report.
        if (request.Report is { } report)
        {
            var (incidentType, severityValue) = await ValidateReportAsync(report, tenantId, cancellationToken);
            ApplyReport(incident, report, incidentType, severityValue);
        }

        if (request.Links is { } linkSection)
        {
            var links = await ResolveLinksAsync(
                previousLinks,
                new LinkState(linkSection.CustomerId, linkSection.DossierId, linkSection.TransportOrderId, linkSection.TripId,
                    linkSection.DriverId, linkSection.VehicleId, linkSection.TrailerId),
                cancellationToken);
            ApplyLinks(incident, links);
        }

        if (request.Impact is { } impact)
        {
            ValidateCosts(impact.EstimatedCost, impact.ActualCost);
            ApplyImpact(incident, impact);
        }

        if (request.Responsibility is { } responsibility)
        {
            ApplyResponsibility(incident, responsibility);
        }

        if (request.Communication is { } communication)
        {
            incident.CustomerMessage = Trim(communication.CustomerMessage);
        }

        var after = Snapshot(incident);
        var changedKeys = after.Keys.Where(k => !Equals(before[k], after[k])).ToList();
        if (changedKeys.Count == 0)
        {
            return await MapDetailAsync(incident, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Only the fields that actually changed: the history reads "Ernst: Gemiddeld → Kritiek".
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "Updated",
            changedKeys.ToDictionary(k => k, k => before[k]),
            changedKeys.ToDictionary(k => k, k => after[k]),
            cancellationToken);
        if (changedKeys.Contains(nameof(Incident.ResponsibleUserId)))
        {
            await NotifyResponsibleAsync(incident, cancellationToken);
        }

        // P5: the configurable charge policy fires exactly once, when responsibility lands on
        // the customer and no charge decision exists yet. Never widens WHO pays — only Customer.
        if (changedKeys.Contains(nameof(Incident.ResponsibleParty)) && (string?)before[nameof(Incident.ResponsibleParty)] != "Customer"
            && incident.ResponsibleParty == "Customer" && incident.ChargeDecision == "None")
        {
            await ApplyChargePolicyAsync(incident, cancellationToken);
        }

        // A severity raised to a vehicle safety issue, or a newly linked (already confirmed) dossier.
        await ProblemSignals.AfterIncidentChangedAsync(
            _dbContext, tenantId, incident, previousLinks.DossierId, previousLinks.OrderId, previousLinks.TripId,
            _notificationService, _auditService, cancellationToken);

        return await MapDetailAsync(incident, cancellationToken);
    }

    /// <summary>The persisted, office-relevant state of an incident (audit diff of an update).</summary>
    private static Dictionary<string, object?> Snapshot(Incident i) => new()
    {
        [nameof(Incident.Title)] = i.Title,
        [nameof(Incident.Description)] = i.Description,
        [nameof(Incident.IncidentType)] = i.IncidentType.ToString(),
        [nameof(Incident.CustomTypeName)] = i.CustomTypeName,
        [nameof(Incident.Severity)] = i.Severity.ToString(),
        [nameof(Incident.Cause)] = i.Cause,
        [nameof(Incident.ResponsibleUserId)] = i.ResponsibleUserId,
        [nameof(Incident.DueDate)] = i.DueDate,
        [nameof(Incident.CustomerId)] = i.CustomerId,
        [nameof(Incident.DossierId)] = i.DossierId,
        [nameof(Incident.TransportOrderId)] = i.TransportOrderId,
        [nameof(Incident.TripId)] = i.TripId,
        [nameof(Incident.DriverId)] = i.DriverId,
        [nameof(Incident.VehicleId)] = i.VehicleId,
        [nameof(Incident.TrailerId)] = i.TrailerId,
        [nameof(Incident.CustomerImpact)] = i.CustomerImpact,
        [nameof(Incident.OperationalImpact)] = i.OperationalImpact,
        [nameof(Incident.FinancialImpact)] = i.FinancialImpact,
        [nameof(Incident.EstimatedCost)] = i.EstimatedCost,
        [nameof(Incident.ActualCost)] = i.ActualCost,
        [nameof(Incident.ResponsibleParty)] = i.ResponsibleParty,
        [nameof(Incident.ResponsibilityNotes)] = i.ResponsibilityNotes,
        [nameof(Incident.CustomerMessage)] = i.CustomerMessage,
    };

    /// <summary>P5: resolves the most specific charge policy (customer+type &gt; customer &gt;
    /// type &gt; tenant default) and applies its mode. Auto without an amount degrades to Propose.</summary>
    private async Task ApplyChargePolicyAsync(Incident incident, CancellationToken cancellationToken)
    {
        var policy = await ResolveChargePolicyAsync(incident, cancellationToken);
        if (policy is null || policy.Mode == "Never")
        {
            return;
        }

        var amount = policy.DefaultAmount ?? incident.EstimatedCost;
        if (amount is not > 0m)
        {
            return; // nothing sensible to propose automatically; manual flow stays available
        }

        var description = policy.DefaultDescription
            ?? $"Doorrekening {incident.CustomTypeName ?? incident.IncidentType.ToString()}: {incident.Title}";
        var before = new { incident.ChargeDecision, incident.ChargeAmount };
        incident.ChargeDecision = "Proposed";
        incident.ChargeAmount = decimal.Round(amount.Value, 2);
        incident.ChargeDescription = description;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "ChargeProposedByPolicy", before,
            new { incident.ChargeDecision, incident.ChargeAmount, PolicyId = policy.Id, policy.Mode }, cancellationToken);

        if (policy.Mode == "Auto")
        {
            // The configured policy IS the standing approval; the line lands via the same
            // mechanics as a manual approval and stays reversible while the price is unlocked.
            await ApplyApprovedChargeAsync(incident, decidedByUserId: null, cancellationToken);
            await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "ChargeAutoApprovedByPolicy",
                null, new { PolicyId = policy.Id, incident.ChargeAmount }, cancellationToken);
        }
    }

    private async Task<IncidentChargePolicy?> ResolveChargePolicyAsync(
        Incident incident, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var typeName = incident.IncidentType.ToString();
        var candidates = await _dbContext.IncidentChargePolicies
            .Where(p => p.TenantId == tenantId
                        && (p.CustomerId == null || p.CustomerId == incident.CustomerId)
                        && (p.IncidentType == null || p.IncidentType == typeName))
            .ToListAsync(cancellationToken);
        return candidates
            .OrderByDescending(p => (p.CustomerId is not null ? 2 : 0) + (p.IncidentType is not null ? 1 : 0))
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
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
            incident.ResolvedByUserId = _currentUser?.CurrentUserId;
        }
        else
        {
            // Reopening (or cancelling) leaves any earlier resolution text as history, but
            // the incident is no longer considered resolved.
            incident.ResolvedAt = null;
            incident.ResolvedByUserId = null;
        }

        incident.Status = target;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // B24/B25: a before/after Status pair, so the history reads "Status gewijzigd van … naar …".
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "StatusChanged",
            new { Status = previousStatus.ToString() },
            new { Status = target.ToString(), Resolution = target == IncidentStatus.Resolved ? incident.Resolution : null },
            cancellationToken);

        return await MapDetailAsync(incident, cancellationToken);
    }

    private async Task<(IncidentType Type, IncidentSeverity Severity)> ValidateReportAsync(
        IncidentReportSection request, Guid tenantId, CancellationToken cancellationToken)
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

        if (request.ResponsibleUserId is { } userId
            && !await _dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == userId && u.IsActive, cancellationToken))
        {
            throw new DomainValidationException("responsibleUserId", "De gekozen verantwoordelijke bestaat niet of is inactief.");
        }

        return (incidentType, severity);
    }

    private static void ValidateCosts(decimal? estimatedCost, decimal? actualCost)
    {
        if (estimatedCost is < 0)
        {
            throw new DomainValidationException("estimatedCost", "De geschatte kost kan niet negatief zijn.");
        }

        if (actualCost is < 0)
        {
            throw new DomainValidationException("actualCost", "De werkelijke kost kan niet negatief zijn.");
        }
    }

    /// <summary>The relations of an incident, as one value (EX-01/EX-04).</summary>
    private sealed record LinkState(
        Guid? CustomerId, Guid? DossierId, Guid? OrderId, Guid? TripId, Guid? DriverId, Guid? VehicleId, Guid? TrailerId)
    {
        public static readonly LinkState Empty = new(null, null, null, null, null, null, null);

        public static LinkState Of(Incident i) =>
            new(i.CustomerId, i.DossierId, i.TransportOrderId, i.TripId, i.DriverId, i.VehicleId, i.TrailerId);
    }

    /// <summary>
    /// EX-01 + EX-04: validates and completes the requested relations against the persisted ones.
    /// <list type="bullet">
    /// <item>Trip, driver and trailer are EXECUTION relations: filled in when still empty, never changed
    /// or cleared by an office edit (an omitted/null value keeps what the driver app recorded).</item>
    /// <item>The server derives what the context already says — customer and dossier from the order,
    /// the trip when the order runs on exactly one trip, driver/vehicle/trailer from a newly linked trip —
    /// so no form has to fill every redundant key.</item>
    /// <item>Impossible combinations (an order of another customer/dossier, a trip that does not carry the
    /// order) are refused — but only when one of the involved relations changes, so legacy rows stay editable.</item>
    /// </list>
    /// </summary>
    private async Task<LinkState> ResolveLinksAsync(LinkState current, LinkState requested, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        var tripId = FillOnly(current.TripId, requested.TripId, "tripId", "De rit van dit incident ligt vast en kan niet worden gewijzigd.");
        var driverId = FillOnly(current.DriverId, requested.DriverId, "driverId", "De chauffeur van dit incident ligt vast en kan niet worden gewijzigd.");
        var trailerId = FillOnly(current.TrailerId, requested.TrailerId, "trailerId", "De oplegger van dit incident ligt vast en kan niet worden gewijzigd.");
        var customerId = requested.CustomerId;
        var dossierId = requested.DossierId;
        var orderId = requested.OrderId;
        var vehicleId = requested.VehicleId;

        // Every NEWLY provided link must resolve inside the tenant.
        if (customerId is { } c && c != current.CustomerId
            && !await _dbContext.Customers.AnyAsync(x => x.TenantId == tenantId && x.Id == c, cancellationToken))
        {
            throw new DomainValidationException("customerId", "De gekozen klant bestaat niet.");
        }

        if (driverId is { } d && d != current.DriverId
            && !await _dbContext.Drivers.AnyAsync(x => x.TenantId == tenantId && x.Id == d, cancellationToken))
        {
            throw new DomainValidationException("driverId", "De gekozen chauffeur bestaat niet.");
        }

        if (vehicleId is { } v && v != current.VehicleId
            && !await _dbContext.Vehicles.AnyAsync(x => x.TenantId == tenantId && x.Id == v, cancellationToken))
        {
            throw new DomainValidationException("vehicleId", "Het gekozen voertuig bestaat niet.");
        }

        if (trailerId is { } tr && tr != current.TrailerId
            && !await _dbContext.Trailers.AnyAsync(x => x.TenantId == tenantId && x.Id == tr, cancellationToken))
        {
            throw new DomainValidationException("trailerId", "De gekozen oplegger bestaat niet.");
        }

        var tripChanged = tripId != current.TripId;
        if (tripId is { } t && tripChanged
            && !await _dbContext.Trips.AnyAsync(x => x.TenantId == tenantId && x.Id == t, cancellationToken))
        {
            throw new DomainValidationException("tripId", "De gekozen rit bestaat niet.");
        }

        if (dossierId is { } ds && ds != current.DossierId
            && !await _dbContext.TransportDossiers.AnyAsync(x => x.TenantId == tenantId && x.Id == ds, cancellationToken))
        {
            throw new DomainValidationException("dossierId", "Het gekozen dossier bestaat niet.");
        }

        var orderChanged = orderId != current.OrderId;
        if (orderId is { } o)
        {
            var order = await _dbContext.TransportOrders.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == o)
                .Select(x => new { x.CustomerId })
                .FirstOrDefaultAsync(cancellationToken);
            if (order is null)
            {
                throw new DomainValidationException("transportOrderId", "De gekozen transportopdracht bestaat niet.");
            }

            // Customer follows the order.
            if (customerId is null)
            {
                customerId = order.CustomerId;
            }
            else if (customerId != order.CustomerId && (orderChanged || customerId != current.CustomerId))
            {
                throw new DomainValidationException("customerId", "De gekozen opdracht hoort bij een andere klant.");
            }

            // Dossier follows the order (EX-04: the server resolves the relation itself).
            var orderDossiers = await ProblemScope.DossiersOfOrderAsync(_dbContext, tenantId, o, cancellationToken);
            if (dossierId is null)
            {
                dossierId = orderDossiers.Count > 0 ? orderDossiers[0] : null;
            }
            else if (orderDossiers.Count > 0 && !orderDossiers.Contains(dossierId.Value)
                     && (orderChanged || dossierId != current.DossierId))
            {
                throw new DomainValidationException("dossierId", "De gekozen opdracht hoort niet bij dit dossier.");
            }

            if (tripId is { } linkedTrip)
            {
                if ((orderChanged || tripChanged)
                    && !await _dbContext.TripOrders.AnyAsync(x => x.TenantId == tenantId && x.TripId == linkedTrip && x.TransportOrderId == o, cancellationToken))
                {
                    throw new DomainValidationException("tripId", "Deze opdracht staat niet op de gekoppelde rit.");
                }
            }
            else
            {
                // The order ran on exactly one (non-cancelled) trip: that is the incident's trip.
                var trips = await _dbContext.TripOrders.AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.TransportOrderId == o)
                    .Join(_dbContext.Trips.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status != Modules.Planning.Entities.TripStatus.Cancelled),
                        x => x.TripId, x => x.Id, (x, trip) => trip.Id)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                if (trips.Count == 1)
                {
                    tripId = trips[0];
                }
            }
        }

        // A newly linked trip brings its driver, vehicle and trailer where the incident has none.
        if (tripId is { } newTrip && newTrip != current.TripId)
        {
            var trip = await _dbContext.Trips.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == newTrip)
                .Select(x => new { x.DriverId, x.VehicleId, x.TrailerId })
                .FirstAsync(cancellationToken);
            driverId ??= trip.DriverId;
            vehicleId ??= trip.VehicleId;
            trailerId ??= trip.TrailerId;
        }

        if (dossierId is { } dossier)
        {
            var dossierCustomer = await _dbContext.TransportDossiers.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == dossier)
                .Select(x => x.CustomerId)
                .FirstOrDefaultAsync(cancellationToken);
            if (customerId is null)
            {
                customerId = dossierCustomer;
            }
            else if (dossierCustomer is not null && dossierCustomer != customerId
                     && (dossier != current.DossierId || customerId != current.CustomerId))
            {
                throw new DomainValidationException("dossierId", "Het gekozen dossier hoort bij een andere klant.");
            }
        }

        return new LinkState(customerId, dossierId, orderId, tripId, driverId, vehicleId, trailerId);
    }

    private static Guid? FillOnly(Guid? current, Guid? requested, string field, string message)
    {
        if (current is null)
        {
            return requested;
        }

        if (requested is { } value && value != current)
        {
            throw new DomainValidationException(field, message);
        }

        return current;
    }

    // --- Wave 6 §4: the unified problem view -------------------------------------------------

    /// <summary>OPEN incidents + OPEN execution exceptions in one list — one place to work
    /// problems, each row linking to its own detail (incident page / trip).</summary>
    public async Task<IReadOnlyList<ProblemListItemDto>> ListProblemsAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var openIncidentStatuses = ProblemScope.OpenIncidentStatuses;
        var openExceptionStatuses = ProblemScope.OpenExceptionStatuses;

        var incidents = await _dbContext.Incidents.AsNoTracking()
            .Where(i => i.TenantId == tenantId && openIncidentStatuses.Contains(i.Status))
            .Select(i => new
            {
                i.Id, i.Title, i.IncidentType, i.CustomTypeName, Severity = i.Severity.ToString(), Status = i.Status.ToString(),
                OccurredAt = i.CreatedAt, i.TransportOrderId, i.TripId, i.DossierId, i.CustomerId,
                i.ResponsibleParty, i.ChargeDecision, ResponsibleId = i.ResponsibleUserId,
            })
            .ToListAsync(cancellationToken);

        var exceptions = await _dbContext.ExecutionExceptions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && openExceptionStatuses.Contains(e.Status))
            .Select(e => new
            {
                e.Id, e.Type, e.Description,
                Severity = e.Severity.ToString(), Status = e.Status.ToString(),
                e.OccurredAt, e.TransportOrderId, TripId = (Guid?)e.TripId, ResponsibleId = e.AssignedToUserId,
            })
            .ToListAsync(cancellationToken);

        var orderIds = incidents.Select(i => i.TransportOrderId)
            .Concat(exceptions.Select(e => e.TransportOrderId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var orders = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber, o.CustomerId })
                .ToListAsync(cancellationToken);
        var orderById = orders.ToDictionary(o => o.Id);
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
        var customerIds = incidents.Select(i => i.CustomerId)
            .Concat(orders.Select(o => (Guid?)o.CustomerId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var customerNames = customerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var userIds = incidents.Select(i => i.ResponsibleId).Concat(exceptions.Select(e => e.ResponsibleId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        var userNames = userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

        string? OrderNumber(Guid? id) => id is { } oid && orderById.TryGetValue(oid, out var o) ? o.OrderNumber : null;
        string? CustomerOfOrder(Guid? id) => id is { } oid && orderById.TryGetValue(oid, out var o) ? customerNames.GetValueOrDefault(o.CustomerId) : null;
        string? UserName(Guid? id) => id is { } uid ? userNames.GetValueOrDefault(uid) : null;

        // Afwijkingen have no own dossier key: resolve it like everywhere else (order → dossier).
        var exceptionDossier = new Dictionary<Guid, (Guid Id, string Number)>();
        foreach (var orderId in exceptions.Where(e => e.TransportOrderId != null).Select(e => e.TransportOrderId!.Value).Distinct())
        {
            var ids = await ProblemScope.DossiersOfOrderAsync(_dbContext, tenantId, orderId, cancellationToken);
            if (ids.Count > 0)
            {
                var number = await _dbContext.TransportDossiers.AsNoTracking()
                    .Where(d => d.TenantId == tenantId && d.Id == ids[0]).Select(d => d.DossierNumber).FirstOrDefaultAsync(cancellationToken);
                if (number is not null) exceptionDossier[orderId] = (ids[0], number);
            }
        }

        return incidents
            .Select(i => new ProblemListItemDto(
                i.Id, "Incident", i.Title, i.Severity, i.Status, i.OccurredAt,
                OrderNumber(i.TransportOrderId),
                i.TripId is { } tid ? tripNumbers.GetValueOrDefault(tid) : null, i.TripId,
                i.DossierId is { } did ? dossierNumbers.GetValueOrDefault(did) : null, i.DossierId,
                i.ResponsibleParty, i.ChargeDecision)
            {
                TypeCode = i.IncidentType.ToString(),
                TypeLabel = Modules.Dossiers.Services.DossierTimelineLabels.IncidentType(i.IncidentType, i.CustomTypeName),
                ResponsibleName = UserName(i.ResponsibleId),
                CustomerName = i.CustomerId is { } cid ? customerNames.GetValueOrDefault(cid) : CustomerOfOrder(i.TransportOrderId),
            })
            .Concat(exceptions.Select(e =>
            {
                var label = Modules.Dossiers.Services.DossierTimelineLabels.ExceptionType(e.Type);
                var dossier = e.TransportOrderId is { } oid && exceptionDossier.TryGetValue(oid, out var d) ? d : ((Guid Id, string Number)?)null;
                return new ProblemListItemDto(
                    e.Id, "Exception", $"{label}: {e.Description}", e.Severity, e.Status, e.OccurredAt,
                    OrderNumber(e.TransportOrderId),
                    e.TripId is { } tid ? tripNumbers.GetValueOrDefault(tid) : null, e.TripId,
                    dossier?.Number, dossier?.Id)
                {
                    TypeCode = e.Type.ToString(),
                    TypeLabel = label,
                    ResponsibleName = UserName(e.ResponsibleId),
                    CustomerName = CustomerOfOrder(e.TransportOrderId),
                };
            }))
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

        // P5: a configured Never-policy switches customer-fault charging off entirely.
        if (await ResolveChargePolicyAsync(incident, cancellationToken) is { Mode: "Never" })
        {
            throw new DomainValidationException(
                "Doorrekenen is per beleid uitgeschakeld voor deze klant/dit incidenttype.");
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
        if (request.Approve)
        {
            lineNote = await ApplyApprovedChargeAsync(incident, userId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(),
            request.Approve ? "ChargeApproved" : "ChargeRejected", before,
            new { incident.ChargeDecision, incident.ChargeAmount, Note = lineNote }, cancellationToken);
        return await MapDetailAsync(incident, cancellationToken);
    }

    /// <summary>
    /// Shared by manual approval AND the P5 Auto policy: marks the charge Approved and lands
    /// the sales line on the linked order via the EXISTING manual-line mechanics — from there
    /// the normal invoice flow picks it up. A locked/invoiced price refuses; the approval
    /// stays recorded and the invoice-control workspace surfaces it as manual work. The line
    /// stays reversible (editable/removable) until the price snapshot is locked at invoicing.
    /// Caller saves.
    /// </summary>
    private async Task<string?> ApplyApprovedChargeAsync(
        Incident incident, Guid? decidedByUserId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        incident.ChargeDecision = "Approved";
        incident.ChargeDecidedByUserId = decidedByUserId;
        incident.ChargeDecidedAt = _timeProvider.GetUtcNow().UtcDateTime;

        if (incident.TransportOrderId is not { } chargeOrderId)
        {
            return null;
        }

        var snapshot = await _dbContext.TransportOrderPricingSnapshots.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.TransportOrderId == chargeOrderId, cancellationToken);
        if (snapshot is { Status: Modules.Orders.Entities.OrderPricingStatus.Locked
            or Modules.Orders.Entities.OrderPricingStatus.Invoiced })
        {
            return "De prijs van de gekoppelde order is vergrendeld/gefactureerd — voeg de lijn handmatig toe bij facturatie.";
        }

        var order = await _dbContext.TransportOrders.FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.Id == chargeOrderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

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
            AdjustedByUserId = decidedByUserId,
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
        return null;
    }

    // --- Wave 6 §3: linked redelivery --------------------------------------------------------

    /// <summary>Afwijking types whose quantity is goods that did NOT reach the consignee.</summary>
    private static readonly Modules.Exceptions.Entities.ExecutionExceptionType[] NotDeliveredTypes =
    [
        Modules.Exceptions.Entities.ExecutionExceptionType.MissingPackage,
        Modules.Exceptions.Entities.ExecutionExceptionType.IncompleteQuantity,
        Modules.Exceptions.Entities.ExecutionExceptionType.PartialDelivery,
        Modules.Exceptions.Entities.ExecutionExceptionType.RejectedDelivery,
        Modules.Exceptions.Entities.ExecutionExceptionType.DamagedPackage,
    ];

    /// <summary>
    /// The redelivery of what was NOT delivered: the not-delivered quantity (explicit, else the source
    /// afwijking's quantity, else the order's quantity-bearing afwijkingen, else everything), the relevant
    /// stops with their time requirements and access details (requested windows moved to the new date;
    /// confirmed windows are agreements of the original and are not copied), backlinks in both
    /// directions and a history entry on both orders. It starts UNPRICED (review) — never a silent copy
    /// of the original billing — or explicitly free through the existing confirmed-free mechanism.
    /// </summary>
    public async Task<IncidentDetailDto?> CreateRedeliveryAsync(
        Guid id, CancellationToken cancellationToken, CreateRedeliveryRequest? request = null)
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

        // D2: a redelivery re-runs a loading → unloading route. On-site work has neither (site
        // stops only), and copying it as an ordinary order would leave site stops on a transport.
        if (original.CraneJobKind == Modules.Orders.Entities.CraneJobKind.OnSiteLifting)
        {
            throw new DomainValidationException(
                "Voor kraanwerk ter plaatse kan geen herleveringsorder worden aangemaakt; er is niets geleverd.");
        }

        if (request?.Free == true && string.IsNullOrWhiteSpace(request.FreeReason))
        {
            throw new DomainValidationException("freeReason", "Geef een reden op waarom de herlevering gratis is.");
        }

        var (quantity, quantityBasis) = await NotDeliveredQuantityAsync(incident, original, request?.Quantity, cancellationToken);

        var settings = await _dbContext.TenantSettings.FirstOrDefaultAsync(
            s => s.TenantId == tenantId, cancellationToken);

        // P4: the redelivery targets the next WORKING day (skipping weekends + tenant holidays) after
        // the TENANT's today — just after local midnight the UTC date is still yesterday.
        var today = await Common.TenantTimeZone.TodayAsync(_dbContext, tenantId, _timeProvider, cancellationToken);
        var holidays = (await _dbContext.TenantHolidays.AsNoTracking()
                .Where(h => h.TenantId == tenantId && h.Date > today && h.Date <= today.AddDays(60))
                .Select(h => h.Date)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var redeliveryDate = BusinessDayCalculator.NextWorkingDay(today, holidays);
        var dayShift = redeliveryDate.DayNumber - original.OrderDate.DayNumber;

        // Relevant stops: a failed delivery stop re-delivers THAT drop (with the loading leg); otherwise the route.
        var stops = original.Stops.Where(s => !s.IsDeleted).OrderBy(s => s.Sequence).ToList();
        if (incident.SourceStopId is { } failedStopId
            && stops.FirstOrDefault(s => s.Id == failedStopId) is { StopType: Modules.Orders.Entities.StopType.Unloading })
        {
            stops = stops.Where(s => s.StopType != Modules.Orders.Entities.StopType.Unloading || s.Id == failedStopId).ToList();
        }

        var ratio = original.Quantity is > 0 && quantity is { } q ? q / original.Quantity.Value : 1m;
        var partial = ratio < 1m;
        var redelivery = new Modules.Orders.Entities.TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = original.CustomerId,
            OrderDate = redeliveryDate,
            Status = Modules.Orders.Entities.TransportOrderStatus.Draft,
            CustomerReference = $"HERLEVERING {original.OrderNumber}",
            GoodsDescription = original.GoodsDescription,
            Quantity = quantity, QuantityUnit = original.QuantityUnit,
            QuantityUnitCode = original.QuantityUnitCode,
            WeightKg = original.WeightKg is { } w ? decimal.Round(w * ratio, 2) : null,
            VolumeM3 = original.VolumeM3 is { } vol ? decimal.Round(vol * ratio, 3) : null,
            PalletCount = original.PalletCount is { } p ? (int)Math.Ceiling(p * ratio) : null,
            DistanceKm = original.DistanceKm,
            LoadingMeters = original.LoadingMeters is { } lm ? decimal.Round(lm * ratio, 2) : null,
            AdrRequired = original.AdrRequired, CraneRequired = original.CraneRequired,
            LegalEntityId = original.LegalEntityId,
            RedeliveryOfOrderId = original.Id,
            RedeliverySourceIncidentId = incident.Id,
            Notes = $"Herlevering van {original.OrderNumber} wegens incident: {incident.Title}. "
                    + (partial
                        ? $"Te leveren: {quantity:0.##} {original.QuantityUnit} van oorspronkelijk {original.Quantity:0.##} ({quantityBasis})."
                        : $"Volledige hoeveelheid opnieuw te leveren ({quantityBasis})."),
            Stops = stops
                .Select((s, index) => new Modules.Orders.Entities.TransportOrderStop
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Sequence = index + 1,
                    StopType = s.StopType, LocationId = s.LocationId, LocationName = s.LocationName,
                    Address = s.Address, PostalCode = s.PostalCode, City = s.City, CountryCode = s.CountryCode,
                    SnapshotAt = s.SnapshotAt, AddressOverridden = s.AddressOverridden,
                    ContactName = s.ContactName, ContactPhone = s.ContactPhone, ContactMobile = s.ContactMobile, ContactEmail = s.ContactEmail,
                    OpeningHoursSummary = s.OpeningHoursSummary, Gate = s.Gate, AccessCode = s.AccessCode, Dock = s.Dock,
                    RouteDescription = s.RouteDescription,
                    DefaultLoadingMinutes = s.DefaultLoadingMinutes, DefaultUnloadingMinutes = s.DefaultUnloadingMinutes,
                    // The time requirement is the customer's standing promise (time of day): it travels along.
                    TimeRequirement = s.TimeRequirement, TimeRequirementFrom = s.TimeRequirementFrom, TimeRequirementTo = s.TimeRequirementTo,
                    AppointmentRequired = s.AppointmentRequired,
                    // Requested windows move to the redelivery day; confirmed/planned ones belonged to the original.
                    RequestedFrom = s.RequestedFrom?.AddDays(dayShift), RequestedTo = s.RequestedTo?.AddDays(dayShift),
                    EarliestAllowed = s.EarliestAllowed?.AddDays(dayShift), LatestAllowed = s.LatestAllowed?.AddDays(dayShift),
                    Reference = s.Reference, Instructions = s.Instructions,
                })
                .ToList(),
        };
        _dbContext.TransportOrders.Add(redelivery);

        // Into the SAME dossier as the original (its wrapper or first user link) — one file.
        var dossierIds = await ProblemScope.DossiersOfOrderAsync(_dbContext, tenantId, originalOrderId, cancellationToken);
        var dossierId = dossierIds.Count > 0 ? dossierIds[0] : incident.DossierId;
        if (dossierId is { } targetDossierId)
        {
            _dbContext.DossierOrders.Add(new Modules.Dossiers.Entities.DossierOrder
            {
                Id = Guid.NewGuid(), TenantId = tenantId,
                DossierId = targetDossierId, TransportOrderId = redelivery.Id,
            });

            // Transport acceptance M13: the redelivery is operational work of the dossier — an
            // activity ("Herlevering" type, else the original order's own activity type) linked to
            // the new order, so it is planned, priced and executed from the dossier workspace like
            // any other activity instead of hiding under "Gekoppelde opdrachten".
            var activityTypeId = await _dbContext.ActivityTypes
                    .Where(t => t.TenantId == tenantId && t.Code == "HERLEVERING" && t.IsActive)
                    .Select(t => (Guid?)t.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                ?? await _dbContext.DossierActivities
                    .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId == originalOrderId)
                    .Select(a => (Guid?)a.ActivityTypeId)
                    .FirstOrDefaultAsync(cancellationToken);
            if (activityTypeId is { } typeId)
            {
                var nextSequence = (await _dbContext.DossierActivities
                    .Where(a => a.TenantId == tenantId && a.DossierId == targetDossierId)
                    .MaxAsync(a => (int?)a.Sequence, cancellationToken) ?? 0) + 1;
                _dbContext.DossierActivities.Add(new Modules.Dossiers.Entities.DossierActivity
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, DossierId = targetDossierId,
                    ActivityTypeId = typeId, Sequence = nextSequence, LinkedTransportOrderId = redelivery.Id,
                    Label = $"Herlevering {original.OrderNumber}",
                });
            }
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
        incident.RedeliverySuggested = false;
        await Common.Persistence.TenantNumbering.SaveWithClaimedNumberAsync(
            _dbContext, settings,
            () => redelivery.OrderNumber = settings is null
                ? $"ORD-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}"
                : $"{settings.OrderNumberPrefix}{settings.OrderNumberNextValue++:0000}",
            cancellationToken);

        // History on all three records: the incident, the new order and the original order.
        var summary = partial
            ? $"{quantity:0.##} {original.QuantityUnit} van {original.Quantity:0.##} opnieuw te leveren"
            : "volledige hoeveelheid opnieuw te leveren";
        await _auditService.RecordAsync(EntityType, incident.Id.ToString(), "RedeliveryCreated", null,
            new { RedeliveryOrderId = redelivery.Id, redelivery.OrderNumber, Quantity = quantity, QuantityBasis = quantityBasis, request?.Free },
            cancellationToken);
        await _auditService.RecordAsync("TransportOrder", redelivery.Id.ToString(), "RedeliveryCreated", null,
            new
            {
                SourceOrderId = original.Id, SourceOrderNumber = original.OrderNumber, IncidentId = incident.Id, IncidentTitle = incident.Title,
                Quantity = quantity, Summary = $"Herlevering van {original.OrderNumber} (incident '{incident.Title}'): {summary}.",
            },
            cancellationToken);
        await _auditService.RecordAsync("TransportOrder", original.Id.ToString(), "RedeliveryRequested", null,
            new
            {
                RedeliveryOrderId = redelivery.Id, RedeliveryOrderNumber = redelivery.OrderNumber, IncidentId = incident.Id,
                Summary = $"Herlevering {redelivery.OrderNumber} aangemaakt wegens incident '{incident.Title}': {summary}.",
            },
            cancellationToken);

        // Billing is never duplicated silently: unpriced (review) by default, or explicitly free.
        if (request?.Free == true)
        {
            var orders = _services?.GetService(typeof(Modules.Orders.Services.ITransportOrderService)) as Modules.Orders.Services.ITransportOrderService
                ?? throw new DomainValidationException("Gratis bevestigen is hier niet beschikbaar; bevestig de prijs op de herleveringsorder.");
            var priced = await orders.SetOneOffPriceAsync(
                redelivery.Id, new Modules.Orders.Dtos.SetOneOffPriceRequest(0m, null, FreeConfirmed: true), cancellationToken);
            if (priced.Outcome != Modules.Orders.Dtos.TransportOrderOperationOutcome.Success)
            {
                throw new DomainValidationException("free", priced.Error ?? "De herlevering kon niet als gratis worden bevestigd.");
            }

            await _auditService.RecordAsync("TransportOrder", redelivery.Id.ToString(), "RedeliveryConfirmedFree", null,
                new { Reason = request.FreeReason!.Trim(), Summary = $"Herlevering gratis bevestigd: {request.FreeReason!.Trim()}" }, cancellationToken);
        }

        return await MapDetailAsync(incident, cancellationToken);
    }

    /// <summary>What was not delivered — and why the system believes that (shown in the order notes).</summary>
    private async Task<(decimal? Quantity, string Basis)> NotDeliveredQuantityAsync(
        Incident incident, Modules.Orders.Entities.TransportOrder original, decimal? requested, CancellationToken cancellationToken)
    {
        if (requested is { } explicitQuantity)
        {
            if (explicitQuantity <= 0 || (original.Quantity is { } max && explicitQuantity > max))
            {
                throw new DomainValidationException("quantity",
                    $"Geef een hoeveelheid tussen 0 en {original.Quantity:0.##} (de oorspronkelijke hoeveelheid).");
            }

            return (explicitQuantity, "opgegeven bij het aanmaken");
        }

        var tenantId = _tenantContext.TenantId;
        if (incident.SourceExceptionId is { } sourceExceptionId)
        {
            var sourceQuantity = await _dbContext.ExecutionExceptions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Id == sourceExceptionId)
                .Select(e => e.Quantity)
                .FirstOrDefaultAsync(cancellationToken);
            if (sourceQuantity is > 0)
            {
                return (Cap(sourceQuantity.Value, original.Quantity), "hoeveelheid uit de afwijking");
            }
        }

        if (incident.SourceStopId is null)
        {
            var notDeliveredTypes = NotDeliveredTypes;
            var reported = await _dbContext.ExecutionExceptions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.TransportOrderId == original.Id && e.Quantity != null && e.Quantity > 0
                            && notDeliveredTypes.Contains(e.Type)
                            && e.Status != Modules.Exceptions.Entities.ExecutionExceptionStatus.Rejected)
                .SumAsync(e => e.Quantity!.Value, cancellationToken);
            if (reported > 0)
            {
                return (Cap(reported, original.Quantity), "gemelde niet-geleverde hoeveelheid");
            }
        }

        return (original.Quantity, incident.SourceStopId is not null ? "levering volledig mislukt" : "geen deelhoeveelheid gemeld");
    }

    private static decimal Cap(decimal value, decimal? max) => max is { } m && value > m ? m : value;

    private static void ApplyReport(Incident incident, IncidentReportSection report, IncidentType incidentType, IncidentSeverity severity)
    {
        incident.Title = report.Title.Trim();
        incident.Description = report.Description.Trim();
        incident.IncidentType = incidentType;
        incident.CustomTypeName = incidentType == IncidentType.Other ? Trim(report.CustomTypeName) : null;
        incident.Severity = severity;
        incident.Cause = Trim(report.Cause);
        incident.ResponsibleUserId = report.ResponsibleUserId;
        incident.DueDate = report.DueDate;
    }

    private static void ApplyLinks(Incident incident, LinkState links)
    {
        incident.CustomerId = links.CustomerId;
        incident.DossierId = links.DossierId;
        incident.TransportOrderId = links.OrderId;
        incident.TripId = links.TripId;
        incident.DriverId = links.DriverId;
        incident.VehicleId = links.VehicleId;
        incident.TrailerId = links.TrailerId;
    }

    private static void ApplyImpact(Incident incident, IncidentImpactSection impact)
    {
        incident.CustomerImpact = Trim(impact.CustomerImpact);
        incident.OperationalImpact = Trim(impact.OperationalImpact);
        incident.FinancialImpact = Trim(impact.FinancialImpact);
        incident.EstimatedCost = impact.EstimatedCost;
        incident.ActualCost = impact.ActualCost;
    }

    private static void ApplyResponsibility(Incident incident, IncidentResponsibilitySection responsibility)
    {
        // Wave 6 §1: responsibility attribution (validated set; unknown input falls back).
        incident.ResponsibleParty = responsibility.ResponsibleParty is "Customer" or "Own" or "Driver" or "Supplier"
            ? responsibility.ResponsibleParty
            : "Unknown";
        incident.ResponsibilityNotes = Trim(responsibility.ResponsibilityNotes);
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

        var tenantId = _tenantContext.TenantId;
        var resolvedByName = incident.ResolvedByUserId is { } resolverId
            ? await _dbContext.Users.AsNoTracking()
                .Where(u => u.Id == resolverId).Select(u => (string?)(u.FirstName + " " + u.LastName)).FirstOrDefaultAsync(cancellationToken)
            : null;
        var damage = await _dbContext.DamageReports.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.SourceIncidentId == incident.Id)
            .Select(d => new { d.Id, d.Severity, d.Status })
            .FirstOrDefaultAsync(cancellationToken);
        string? sourceExceptionLabel = null;
        if (incident.SourceExceptionId is { } sourceExceptionId)
        {
            var source = await _dbContext.ExecutionExceptions.AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.Id == sourceExceptionId)
                .Select(e => new { e.Type, e.OccurredAt })
                .FirstOrDefaultAsync(cancellationToken);
            sourceExceptionLabel = source is null ? null
                : $"{Modules.Dossiers.Services.DossierTimelineLabels.ExceptionType(source.Type)} ({source.OccurredAt:dd/MM/yyyy})";
        }

        // EX-05: dossiers that were ALREADY confirmed when this problem was registered — never
        // un-confirmed automatically, but shown as post-confirmation attention.
        var dossierIds = await ProblemScope.DossiersOfIncidentAsync(_dbContext, tenantId, incident, cancellationToken);
        var confirmedBefore = dossierIds.Count == 0
            ? []
            : await _dbContext.TransportDossiers.AsNoTracking()
                .Where(d => d.TenantId == tenantId && dossierIds.Contains(d.Id)
                            && d.Status == Modules.Dossiers.Entities.DossierStatus.Closed
                            && d.ClosedAt != null && d.ClosedAt < incident.CreatedAt)
                .Select(d => d.DossierNumber)
                .ToListAsync(cancellationToken);
        var hasAsset = incident.VehicleId is not null || incident.TrailerId is not null;

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
                : null,
            incident.RedeliverySuggested)
        {
            Source = SourceOf(incident),
            SourceExceptionId = incident.SourceExceptionId,
            SourceExceptionLabel = sourceExceptionLabel,
            ResolvedByUserId = incident.ResolvedByUserId,
            ResolvedByName = resolvedByName,
            CustomerMessage = incident.CustomerMessage,
            DamageReportId = damage?.Id,
            DamageReportSeverity = damage?.Severity.ToString(),
            DamageReportStatus = damage?.Status.ToString(),
            CanCreateDamageReport = hasAsset && damage is null && incident.Status != IncidentStatus.Cancelled,
            VehicleSafetyIssue = VehicleSafetyRules.IsSafetyIssue(incident),
            ExecutionLinksLocked = SourceOf(incident) != "Office",
            ConfirmedDossierNumbers = confirmedBefore,
        };
    }

    /// <summary>Office | Driver (driver app) | FailedDelivery (automation) | Exception (escalated afwijking).</summary>
    private static string SourceOf(Incident incident) =>
        incident.SourceStopId is not null ? "FailedDelivery"
        : incident.SourceExceptionId is not null ? "Exception"
        : incident.ClientRequestId is not null ? "Driver"
        : "Office";

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
