using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

public interface IDossierLifecycleService
{
    /// <summary>What confirming would mean now; null when the dossier is not visible to the tenant.</summary>
    Task<DossierConfirmationEvaluationDto?> EvaluateAsync(Guid dossierId, CancellationToken cancellationToken);

    /// <summary>Manual confirmation by a person. Idempotent on an already confirmed dossier (no second audit).</summary>
    Task<DossierDetailDto?> ConfirmAsync(Guid dossierId, ConfirmDossierRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Automatic confirmation after an operational completion event. Idempotent: false when the
    /// dossier is not Open or the pipeline is not fully done; never audits twice.
    /// </summary>
    Task<bool> TryAutoConfirmAsync(Guid dossierId, string trigger, CancellationToken cancellationToken);

    /// <summary>The same for every dossier that owns one of these orders; returns how many were confirmed.</summary>
    Task<int> TryAutoConfirmForOrdersAsync(IReadOnlyCollection<Guid> orderIds, string trigger, CancellationToken cancellationToken);

    Task<DossierDetailDto?> ReopenAsync(Guid dossierId, ReopenDossierRequest request, CancellationToken cancellationToken);
    Task<DossierDetailDto?> CancelAsync(Guid dossierId, CancelDossierRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Dossier confirmation sprint 2026-09-23 — the ONE place that moves a dossier between Open,
/// Closed (= operationally CONFIRMED) and Cancelled, for people and for the pipeline alike.
///
/// Confirmation is operational, never financial: pricing and invoice states are untouched, a
/// confirmed dossier stays invoiceable. The evaluation matrix (documented in
/// docs/ux-sprint/2026-09-23-dossier-confirmation.md):
/// <list type="bullet">
///   <item><b>Blockers</b> (both flows): dossier not Open; open incidents.</item>
///   <item><b>Auto blockers</b> (pipeline only): no activities; a transport activity without order;
///   an order that is not Completed/Invoiced (Cancelled ones are ignored); a delivery stop whose
///   latest execution Failed/Skipped/PartiallyCompleted; a planning-relevant standalone activity
///   without a planned date or planned in the future; nothing executed at all.</item>
///   <item><b>Warnings</b> (manual, acknowledged deliberately): the auto blockers above, POD
///   missing, pricing attention, no CMR — so a dossier created AFTER the transport happened can
///   be confirmed without inventing trip or order statuses.</item>
/// </list>
/// Activity types are data: "executable" is <see cref="ActivityType.PlanningRelevant"/> +
/// no linked order; a purely commercial activity (storage, supplements) never blocks.
/// </summary>
public class DossierLifecycleService : IDossierLifecycleService
{
    private const string EntityType = "TransportDossier";
    public const string ActionConfirmedManually = "ConfirmedManually";
    public const string ActionConfirmedAutomatically = "ConfirmedAutomatically";
    public const string ActionReopened = "Reopened";
    public const string ActionCancelled = "Cancelled";

    private static readonly TransportOrderStatus[] DoneStatuses = [TransportOrderStatus.Completed, TransportOrderStatus.Invoiced];
    private static readonly StopExecutionStatus[] IncompleteTerminal =
        [StopExecutionStatus.Failed, StopExecutionStatus.Skipped, StopExecutionStatus.PartiallyCompleted];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUserContext _currentUser;
    // Resolved lazily: DossierService optionally depends on TransportOrderService, which triggers this
    // service after a completion — a constructor dependency would be a cycle.
    private readonly Func<IDossierService> _dossiers;
    private readonly IDossierReadinessService _readiness;

    public DossierLifecycleService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        TimeProvider timeProvider, ICurrentUserContext currentUser, Func<IDossierService> dossiers,
        IDossierReadinessService readiness)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
        _dossiers = dossiers;
        _readiness = readiness;
    }

    private Guid TenantId => _tenantContext.TenantId;

    // ------------------------------------------------------------ evaluation

    public async Task<DossierConfirmationEvaluationDto?> EvaluateAsync(Guid dossierId, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(dossierId, cancellationToken);
        return dossier is null ? null : await EvaluateAsync(dossier, cancellationToken);
    }

    private sealed record Check(string Code, string Message, Guid? OrderId = null, Guid? ActivityId = null, bool BlocksAuto = true, bool BlocksManual = false);

    private async Task<DossierConfirmationEvaluationDto> EvaluateAsync(TransportDossier dossier, CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var checks = new List<Check>();

        if (dossier.Status != DossierStatus.Open)
        {
            checks.Add(new Check("dossier.not_open",
                dossier.Status == DossierStatus.Cancelled ? "Een geannuleerd dossier kan niet bevestigd worden; heropen het eerst." : "Dit dossier is al bevestigd.",
                BlocksManual: true));
        }

        var openIncidents = await _dbContext.Incidents.AsNoTracking()
            .CountAsync(i => i.TenantId == tenantId && i.DossierId == dossier.Id
                             && (i.Status == IncidentStatus.New || i.Status == IncidentStatus.InProgress), cancellationToken);
        if (openIncidents > 0)
        {
            checks.Add(new Check("incident.open", $"Dit dossier heeft nog {openIncidents} open incident(en). Handel die eerst af.", BlocksManual: true));
        }

        var activities = await _dbContext.DossierActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DossierId == dossier.Id)
            .Join(_dbContext.ActivityTypes.AsNoTracking().Where(t => t.TenantId == tenantId),
                a => a.ActivityTypeId, t => t.Id,
                (a, t) => new { a.Id, a.Label, a.LinkedTransportOrderId, a.PlannedDate, TypeName = t.Name, t.HasStops, t.PlanningRelevant, t.IsBillable })
            .OrderBy(a => a.TypeName)
            .ToListAsync(cancellationToken);
        if (activities.Count == 0)
        {
            checks.Add(new Check("activity.none", "Dit dossier heeft nog geen activiteiten."));
        }

        var linkedOrderIds = await _dbContext.DossierOrders.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DossierId == dossier.Id)
            .Select(l => l.TransportOrderId)
            .ToListAsync(cancellationToken);
        var orderIds = linkedOrderIds
            .Concat(activities.Where(a => a.LinkedTransportOrderId is not null).Select(a => a.LinkedTransportOrderId!.Value))
            .Distinct()
            .ToList();
        var orders = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber, o.Status, o.InvoiceReadinessReasons })
                .ToListAsync(cancellationToken);

        foreach (var activity in activities)
        {
            if (activity.HasStops)
            {
                if (activity.LinkedTransportOrderId is null)
                {
                    checks.Add(new Check("route.order_missing", $"{Ref(activity.TypeName, activity.Label)}: nog geen transportopdracht.", ActivityId: activity.Id));
                }

                continue;
            }

            // Standalone: executable only when the type is planning-relevant (configuration, never a code).
            if (!activity.PlanningRelevant)
            {
                continue;
            }

            if (activity.PlannedDate is null || activity.PlannedDate > today)
            {
                checks.Add(new Check("activity.not_executed",
                    activity.PlannedDate is null
                        ? $"{Ref(activity.TypeName, activity.Label)}: nog niet ingepland."
                        : $"{Ref(activity.TypeName, activity.Label)}: gepland op {activity.PlannedDate:dd/MM/yyyy}, nog niet uitgevoerd.",
                    ActivityId: activity.Id));
            }
        }

        var doneOrderIds = orders.Where(o => DoneStatuses.Contains(o.Status)).Select(o => o.Id).ToList();
        foreach (var order in orders)
        {
            if (order.Status == TransportOrderStatus.Cancelled || DoneStatuses.Contains(order.Status))
            {
                continue;
            }

            checks.Add(new Check("order.not_completed", $"Opdracht {order.OrderNumber} is nog niet uitgevoerd (status {order.Status}).", OrderId: order.Id));
        }

        // Delivery quality of the executed orders: the LATEST execution per stop decides.
        var failedDeliveries = 0;
        if (doneOrderIds.Count > 0)
        {
            var executions = await _dbContext.StopExecutions.AsNoTracking()
                .Where(e => e.TenantId == tenantId)
                .Join(_dbContext.TransportOrderStops.AsNoTracking().Where(s => s.TenantId == tenantId && doneOrderIds.Contains(s.TransportOrderId)),
                    e => e.TransportOrderStopId, s => s.Id,
                    (e, s) => new { s.TransportOrderId, e.TransportOrderStopId, e.Status, At = e.CompletedAt ?? e.UpdatedAt })
                .ToListAsync(cancellationToken);
            foreach (var stop in executions.GroupBy(e => e.TransportOrderStopId))
            {
                var latest = stop.OrderByDescending(e => e.At).First();
                if (IncompleteTerminal.Contains(latest.Status))
                {
                    failedDeliveries++;
                    var number = orders.Single(o => o.Id == latest.TransportOrderId).OrderNumber;
                    checks.Add(new Check("delivery.not_completed", $"Opdracht {number}: een stop eindigde als {latest.Status}.", OrderId: latest.TransportOrderId));
                }
            }
        }

        var podMissing = orders.Where(o => DoneStatuses.Contains(o.Status) && (o.InvoiceReadinessReasons ?? string.Empty).Contains("pod.missing")).ToList();
        foreach (var order in podMissing)
        {
            checks.Add(new Check("delivery.pod_missing", $"Opdracht {order.OrderNumber}: geen afleverbewijs (POD) geregistreerd.", OrderId: order.Id, BlocksAuto: false));
        }

        var executedStandalone = activities.Count(a => !a.HasStops && a.PlanningRelevant && a.PlannedDate is { } d && d <= today);
        if (doneOrderIds.Count == 0 && executedStandalone == 0)
        {
            checks.Add(new Check("nothing.completed", "Er is nog niets uitgevoerd: geen voltooide opdracht of uitgevoerde activiteit."));
        }

        // Commercial attention is informational for confirmation (confirmation ≠ invoicing).
        var readiness = await _readiness.EvaluateAsync(dossier.Id, cancellationToken);
        foreach (var issue in readiness.Where(i => i.Code.StartsWith("pricing.", StringComparison.Ordinal) && i.Severity != "Info"))
        {
            checks.Add(new Check(issue.Code, issue.Message, issue.TransportOrderId, issue.ActivityId, BlocksAuto: false));
        }

        var documents = await DossierDocumentQuery.ForDossier(_dbContext, tenantId, dossier.Id)
            .Select(d => d.DocumentType).ToListAsync(cancellationToken);
        var hasCmr = documents.Contains(TransportOrderDocumentType.Cmr)
                     || await _dbContext.IssuedTransportDocuments.AsNoTracking()
                         .AnyAsync(d => d.TenantId == tenantId && d.DossierId == dossier.Id && d.Kind == IssuedTransportDocumentKind.Cmr, cancellationToken);
        if (doneOrderIds.Count > 0 && !hasCmr)
        {
            checks.Add(new Check("documents.cmr_missing", "Geen CMR aanwezig op dit dossier.", BlocksAuto: false));
        }

        var billable = activities.Count(a => a.IsBillable);
        var pricedUnits = billable == 0 ? 0 : billable - readiness.Count(i => i.Code is "pricing.missing");

        var blockers = checks.Where(c => c.BlocksManual).ToList();
        var warnings = checks.Where(c => !c.BlocksManual).ToList();
        var autoBlockers = checks.Where(c => c.BlocksManual || c.BlocksAuto).ToList();

        return new DossierConfirmationEvaluationDto(
            dossier.Id, dossier.Status.ToString(),
            CanConfirmManually: blockers.Count == 0,
            CanAutoConfirm: autoBlockers.Count == 0,
            blockers.Select(c => ToDto(c, "Blocking")).ToList(),
            warnings.Select(c => ToDto(c, c.BlocksAuto ? "Warning" : "Info")).ToList(),
            autoBlockers.Select(c => ToDto(c, "Blocking")).ToList(),
            orderIds,
            activities.Select(a => a.Id).ToList(),
            new DossierConfirmationSummaryDto(
                OrdersTotal: orders.Count,
                OrdersCompleted: doneOrderIds.Count,
                OrdersCancelled: orders.Count(o => o.Status == TransportOrderStatus.Cancelled),
                OrdersOpen: orders.Count(o => o.Status != TransportOrderStatus.Cancelled && !DoneStatuses.Contains(o.Status)),
                DeliveriesFailed: failedDeliveries,
                PodMissing: podMissing.Count,
                ActivitiesExecutable: activities.Count(a => !a.HasStops && a.PlanningRelevant),
                ActivitiesExecuted: executedStandalone,
                BillableUnits: billable,
                PricedUnits: Math.Max(0, pricedUnits),
                Documents: documents.Count,
                HasCmr: hasCmr,
                OpenIncidents: openIncidents));
    }

    private static string Ref(string typeName, string? label) => string.IsNullOrWhiteSpace(label) ? typeName : $"{typeName} '{label.Trim()}'";

    private static DossierConfirmationCheckDto ToDto(Check c, string severity) => new(c.Code, severity, c.Message, c.OrderId, c.ActivityId);

    // ------------------------------------------------------------ transitions

    public async Task<DossierDetailDto?> ConfirmAsync(Guid dossierId, ConfirmDossierRequest request, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(dossierId, cancellationToken);
        if (dossier is null) return null;

        // Idempotent: confirming a confirmed dossier changes nothing and audits nothing.
        if (dossier.Status == DossierStatus.Closed)
        {
            return await _dossiers().GetAsync(dossierId, cancellationToken);
        }

        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        var evaluation = await EvaluateAsync(dossier, cancellationToken);
        if (!evaluation.CanConfirmManually)
        {
            throw new DomainValidationException("status", evaluation.Blockers[0].Message);
        }

        if (evaluation.Warnings.Any(w => w.Severity == "Warning") && !request.AcknowledgeWarnings)
        {
            throw new DomainValidationException("acknowledgeWarnings",
                "Er zijn aandachtspunten voor dit dossier. Bekijk ze en bevestig bewust.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var confirmed = await ConfirmAtomicallyAsync(dossier, now, DossierConfirmationSource.Manual, _currentUser.CurrentUserId, Trim(request.Reason), cancellationToken);
        if (!confirmed)
        {
            // Lost the race against a completion event (or another person): the dossier IS confirmed now.
            return await _dossiers().GetAsync(dossierId, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), ActionConfirmedManually, null,
            new
            {
                dossier.DossierNumber,
                ConfirmedAt = now,
                ConfirmedByUserId = dossier.ConfirmedByUserId,
                Reason = dossier.ConfirmationReason,
                WarningsAcknowledged = evaluation.Warnings.Select(w => w.Code).Distinct().ToList(),
            },
            cancellationToken);

        return await _dossiers().GetAsync(dossierId, cancellationToken);
    }

    public async Task<bool> TryAutoConfirmAsync(Guid dossierId, string trigger, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(dossierId, cancellationToken);
        if (dossier is null || dossier.Status != DossierStatus.Open)
        {
            return false;
        }

        var evaluation = await EvaluateAsync(dossier, cancellationToken);
        if (!evaluation.CanAutoConfirm)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!await ConfirmAtomicallyAsync(dossier, now, DossierConfirmationSource.Automatic, null, null, cancellationToken))
        {
            // A near-simultaneous completion event confirmed it first: nothing to do, nothing to audit.
            return false;
        }

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), ActionConfirmedAutomatically, null,
            new
            {
                dossier.DossierNumber,
                ConfirmedAt = now,
                Trigger = trigger,
                CompletedOrders = evaluation.Summary.OrdersCompleted,
                ExecutedActivities = evaluation.Summary.ActivitiesExecuted,
            },
            cancellationToken);
        return true;
    }

    public async Task<int> TryAutoConfirmForOrdersAsync(IReadOnlyCollection<Guid> orderIds, string trigger, CancellationToken cancellationToken)
    {
        if (orderIds.Count == 0) return 0;
        var tenantId = TenantId;
        var ids = orderIds.Distinct().ToList();

        var dossierIds = (await _dbContext.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == tenantId && ids.Contains(l.TransportOrderId))
                .Select(l => l.DossierId)
                .ToListAsync(cancellationToken))
            .Concat(await _dbContext.DossierActivities.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.LinkedTransportOrderId != null && ids.Contains(a.LinkedTransportOrderId.Value))
                .Select(a => a.DossierId)
                .ToListAsync(cancellationToken))
            .Distinct()
            .ToList();

        var confirmed = 0;
        foreach (var dossierId in dossierIds)
        {
            if (await TryAutoConfirmAsync(dossierId, trigger, cancellationToken))
            {
                confirmed++;
            }
        }

        return confirmed;
    }

    public async Task<DossierDetailDto?> ReopenAsync(Guid dossierId, ReopenDossierRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Geef een reden op om het dossier te heropenen.");
        }

        var dossier = await FindAsync(dossierId, cancellationToken);
        if (dossier is null) return null;
        if (dossier.Status == DossierStatus.Open)
        {
            throw new DomainValidationException("status", "Dit dossier is al open.");
        }

        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        // The previous confirmation/cancellation travels into the audit row; the entity is clean again.
        var before = new
        {
            Status = dossier.Status.ToString(),
            dossier.ClosedAt,
            ConfirmationSource = dossier.ConfirmationSource?.ToString(),
            dossier.ConfirmedByUserId,
            dossier.ConfirmationReason,
            dossier.CancelledAt,
            dossier.CancelledByUserId,
            dossier.CancellationReason,
        };
        dossier.Status = DossierStatus.Open;
        dossier.ClosedAt = null;
        dossier.ConfirmedByUserId = null;
        dossier.ConfirmationSource = null;
        dossier.ConfirmationReason = null;
        dossier.CancelledAt = null;
        dossier.CancelledByUserId = null;
        dossier.CancellationReason = null;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), ActionReopened, before,
            new { dossier.DossierNumber, Reason = request.Reason.Trim(), ReopenedByUserId = _currentUser.CurrentUserId, ReopenedAt = _timeProvider.GetUtcNow().UtcDateTime },
            cancellationToken);

        return await _dossiers().GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> CancelAsync(Guid dossierId, CancelDossierRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Geef een reden op om het dossier te annuleren.");
        }

        var dossier = await FindAsync(dossierId, cancellationToken);
        if (dossier is null) return null;
        if (dossier.Status != DossierStatus.Open)
        {
            throw new DomainValidationException("status", dossier.Status == DossierStatus.Cancelled
                ? "Dit dossier is al geannuleerd."
                : "Een bevestigd dossier kan niet geannuleerd worden; heropen het eerst.");
        }

        await RequireVersionAsync(dossier, request.Version, cancellationToken);

        var tenantId = TenantId;
        var orderIds = (await _dbContext.DossierOrders.AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.DossierId == dossier.Id).Select(l => l.TransportOrderId).ToListAsync(cancellationToken))
            .Concat(await _dbContext.DossierActivities.AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.DossierId == dossier.Id && a.LinkedTransportOrderId != null)
                .Select(a => a.LinkedTransportOrderId!.Value).ToListAsync(cancellationToken))
            .Distinct().ToList();
        var orders = orderIds.Count == 0
            ? []
            : await _dbContext.TransportOrders.Where(o => o.TenantId == tenantId && orderIds.Contains(o.Id)).ToListAsync(cancellationToken);

        // Cancellation is for work that never happened: an order that was planned, executed,
        // submitted for review or invoiced is decided on its own flow first.
        var progressed = orders.Where(o => o.Status is not (TransportOrderStatus.Draft or TransportOrderStatus.Cancelled)).ToList();
        if (progressed.Count > 0)
        {
            throw new DomainValidationException("status",
                $"Opdracht(en) {string.Join(", ", progressed.Select(o => o.OrderNumber))} zijn al in behandeling of uitgevoerd. "
                + "Annuleer of rond die eerst af; een uitgevoerd dossier bevestig je in plaats van het te annuleren.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var reason = request.Reason.Trim();
        // Draft orders go along with the same reason (they were only ever administrative).
        var cascaded = new List<string>();
        foreach (var draft in orders.Where(o => o.Status == TransportOrderStatus.Draft))
        {
            draft.Status = TransportOrderStatus.Cancelled;
            draft.CancellationReason = reason;
            draft.Version = Guid.NewGuid();
            cascaded.Add(draft.OrderNumber);
        }

        dossier.Status = DossierStatus.Cancelled;
        dossier.CancelledAt = now;
        dossier.CancelledByUserId = _currentUser.CurrentUserId;
        dossier.CancellationReason = reason;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var draft in orders.Where(o => cascaded.Contains(o.OrderNumber)))
        {
            await _auditService.RecordAsync("TransportOrder", draft.Id.ToString(), "Cancelled",
                new { Status = TransportOrderStatus.Draft }, new { draft.Status, draft.CancellationReason, ByDossier = dossier.DossierNumber }, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, dossier.Id.ToString(), ActionCancelled, new { Status = DossierStatus.Open },
            new { dossier.DossierNumber, CancelledAt = now, CancelledByUserId = dossier.CancelledByUserId, Reason = reason, DraftOrdersCancelled = cascaded },
            cancellationToken);

        return await _dossiers().GetAsync(dossierId, cancellationToken);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>
    /// The Open → Closed transition as ONE atomic compare-and-set on the database
    /// (<c>UPDATE … WHERE Status = Open</c>). Two near-simultaneous completion events (two trips,
    /// two requests) both evaluate the dossier as ready; only the first UPDATE matches a row, the
    /// second returns 0 and skips its audit — exactly one confirmation, one ClosedAt, no optimistic
    /// concurrency exception on the caller's request. The tracked instance is aligned afterwards.
    /// </summary>
    private async Task<bool> ConfirmAtomicallyAsync(
        TransportDossier dossier, DateTime now, DossierConfirmationSource source, Guid? confirmedBy, string? reason,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantId;
        var version = Guid.NewGuid();
        var affected = await _dbContext.TransportDossiers
            .Where(d => d.TenantId == tenantId && d.Id == dossier.Id && d.Status == DossierStatus.Open)
            .ExecuteUpdateAsync(set => set
                .SetProperty(d => d.Status, DossierStatus.Closed)
                .SetProperty(d => d.ClosedAt, now)
                .SetProperty(d => d.ConfirmedByUserId, confirmedBy)
                .SetProperty(d => d.ConfirmationSource, source)
                .SetProperty(d => d.ConfirmationReason, reason)
                .SetProperty(d => d.Version, version)
                .SetProperty(d => d.UpdatedAt, now), cancellationToken);
        // Either way the tracked instance follows the database (the winner's values, or ours).
        await _dbContext.Entry(dossier).ReloadAsync(cancellationToken);
        return affected == 1;
    }

    private Task<TransportDossier?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.TransportDossiers.FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == id, cancellationToken);

    private async Task RequireVersionAsync(TransportDossier dossier, Guid? version, CancellationToken cancellationToken)
    {
        if (version is { } expected && expected != dossier.Version)
        {
            throw new DossierVersionConflictException((await _dossiers().GetAsync(dossier.Id, cancellationToken))!);
        }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
