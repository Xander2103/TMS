using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Dossiers.Services;

/// <summary>The derived schedule facts of a standalone activity (T-H7).</summary>
public static class StandaloneActivitySchedule
{
    /// <summary>PlannedDate + StartTime + DurationHours as a local date-time; null when a part is missing.</summary>
    public static DateTime? PlannedEnd(DateOnly? date, TimeOnly? start, decimal? durationHours) =>
        date is { } d && start is { } s && durationHours is { } h
            ? d.ToDateTime(s).AddMinutes((double)Math.Round(h * 60m))
            : null;

    public static DateTime? PlannedStart(DateOnly? date, TimeOnly? start) =>
        date is { } d && start is { } s ? d.ToDateTime(s) : null;
}

/// <summary>Blocking planning conflicts on a standalone assignment: the controller answers 409 with them.</summary>
public sealed class ActivityAssignmentConflictException : Exception
{
    public ActivityAssignmentConflictException(string message, IReadOnlyList<PlanningConflictDto> conflicts) : base(message)
    {
        Conflicts = conflicts;
    }

    public IReadOnlyList<PlanningConflictDto> Conflicts { get; }
}

public interface IDossierActivityExecutionService
{
    /// <summary>Driver/vehicle of a standalone planning-relevant activity. Null when the dossier is not the tenant's.</summary>
    Task<DossierActivityAssignResultDto?> AssignAsync(
        Guid dossierId, Guid activityId, AssignStandaloneActivityRequest request, bool overrideAllowed, CancellationToken cancellationToken);

    Task<DossierDetailDto?> StartAsync(Guid dossierId, Guid activityId, DossierActivityTransitionRequest request, CancellationToken cancellationToken);

    /// <summary>Completes the activity and re-evaluates dossier auto-confirmation (trigger <c>activity:&lt;label&gt;</c>).</summary>
    Task<DossierDetailDto?> CompleteAsync(Guid dossierId, Guid activityId, DossierActivityTransitionRequest request, CancellationToken cancellationToken);

    /// <summary>Cancels the activity (reason required) and re-evaluates dossier auto-confirmation.</summary>
    Task<DossierDetailDto?> CancelAsync(Guid dossierId, Guid activityId, CancelDossierActivityRequest request, CancellationToken cancellationToken);

    /// <summary>Completed/Cancelled → Planned again (dossier must be open; not while on a live invoice).</summary>
    Task<DossierDetailDto?> ReopenAsync(Guid dossierId, Guid activityId, DossierActivityTransitionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// T-H7 (2026-10-16): execution of STANDALONE dossier activities (crane work on site, plateau,
/// storage …) — the ones without a transport order. Status model
/// <c>Planned → InProgress (optional) → Completed</c>, or <c>Cancelled</c> with a reason; a
/// resolved activity can be put back to Planned while the dossier is open.
/// <list type="bullet">
///   <item>Completing needs a planned date for planning-relevant types and never lies in the future.</item>
///   <item>Completing and cancelling re-run the dossier auto-confirmation exactly like an order
///   completion does (<see cref="IDossierLifecycleService.TryAutoConfirmAsync"/>).</item>
///   <item>Assignment (driver/operator, vehicle/equipment) only exists for planning-relevant types;
///   the existing planning conflict engine judges it (absence, employment, blocked/inactive driver,
///   qualifications, vehicle state, crane capability when the type <see cref="ActivityType.RequiresCrane"/>).
///   Double bookings against trips of the same day only warn: a crane operator often drives the
///   crane transport and then lifts on site the same day.</item>
/// </list>
/// The assigned driver starts and completes the work from the driver app through
/// <see cref="MyActivitiesService"/>, which reuses these transitions.
/// </summary>
public class DossierActivityExecutionService : IDossierActivityExecutionService
{
    private const string EntityType = "TransportDossier";

    private static readonly PlanningConflictCode[] TripOnlyCodes =
        [PlanningConflictCode.MissingDriver, PlanningConflictCode.MissingVehicle, PlanningConflictCode.NoOrders];

    private static readonly PlanningConflictCode[] SoftenedCodes =
        [PlanningConflictCode.DriverDoubleBooked, PlanningConflictCode.VehicleDoubleBooked];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IDossierService _dossierService;
    private readonly IDossierLifecycleService _lifecycle;
    private readonly IPlanningConflictService _conflicts;
    private readonly ICurrentUserContext _currentUser;
    private readonly TimeProvider _timeProvider;

    public DossierActivityExecutionService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        IDossierService dossierService, IDossierLifecycleService lifecycle, IPlanningConflictService conflicts,
        ICurrentUserContext currentUser, TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _dossierService = dossierService;
        _lifecycle = lifecycle;
        _conflicts = conflicts;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    private Guid TenantId => _tenantContext.TenantId;

    public async Task<DossierActivityAssignResultDto?> AssignAsync(
        Guid dossierId, Guid activityId, AssignStandaloneActivityRequest request, bool overrideAllowed, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(dossierId, activityId, request.Version, "toegewezen", cancellationToken);
        if (loaded is null) return null;
        var (dossier, activity) = loaded.Value;

        if (!activity.ActivityType!.PlanningRelevant)
        {
            throw new DomainValidationException(
                "Dit activiteitstype is niet planningsrelevant; een chauffeur of voertuig toewijzen is niet van toepassing.");
        }

        RequireNotResolved(activity, "toegewezen");

        await _dbContext.Drivers.EnsureBelongsToTenantAsync(request.DriverId, TenantId, "De gekozen chauffeur", cancellationToken);
        await _dbContext.Vehicles.EnsureBelongsToTenantAsync(request.VehicleId, TenantId, "Het gekozen voertuig", cancellationToken);

        var conflicts = await EvaluateConflictsAsync(activity, request.DriverId, request.VehicleId, cancellationToken);
        var blocking = conflicts.Where(c => c.Blocking).ToList();
        string? overrideReason = null;
        if (blocking.Count > 0)
        {
            if (!request.Override)
            {
                throw new ActivityAssignmentConflictException("De toewijzing botst met blokkerende planningsconflicten.", conflicts);
            }

            if (!overrideAllowed)
            {
                throw new UnauthorizedAccessException("Je hebt geen recht om planningsconflicten te overschrijven.");
            }

            if (string.IsNullOrWhiteSpace(request.OverrideReason))
            {
                throw new DomainValidationException("overrideReason", "Geef een reden op om de conflicten te overschrijven.");
            }

            overrideReason = request.OverrideReason.Trim();
        }

        var before = new { activity.DriverId, activity.VehicleId };
        activity.DriverId = request.DriverId;
        activity.VehicleId = request.VehicleId;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityAssigned", before,
            new
            {
                activity.Id, activity.Label, activity.DriverId, activity.VehicleId,
                OverriddenConflicts = overrideReason is null ? null : blocking.Select(c => c.Code.ToString()).ToList(),
                OverrideReason = overrideReason,
            },
            cancellationToken);

        var detail = await _dossierService.GetAsync(dossierId, cancellationToken);
        return new DossierActivityAssignResultDto(detail!, conflicts.Where(c => !c.Blocking).ToList());
    }

    public async Task<DossierDetailDto?> StartAsync(
        Guid dossierId, Guid activityId, DossierActivityTransitionRequest request, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(dossierId, activityId, request.Version, "gestart", cancellationToken);
        if (loaded is null) return null;
        var (dossier, activity) = loaded.Value;

        if (activity.ExecutionStatus == DossierActivityExecutionStatus.InProgress)
        {
            return await _dossierService.GetAsync(dossierId, cancellationToken);
        }

        RequireNotResolved(activity, "gestart");
        RequirePlannedDate(activity);

        activity.ExecutionStatus = DossierActivityExecutionStatus.InProgress;
        activity.StartedAt = Now;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityStarted",
            new { ExecutionStatus = DossierActivityExecutionStatus.Planned.ToString() },
            new { activity.Id, activity.Label, ExecutionStatus = activity.ExecutionStatus.ToString(), activity.StartedAt },
            cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> CompleteAsync(
        Guid dossierId, Guid activityId, DossierActivityTransitionRequest request, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(dossierId, activityId, request.Version, "afgerond", cancellationToken);
        if (loaded is null) return null;
        var (dossier, activity) = loaded.Value;

        // Idempotent: completing a completed activity changes and audits nothing.
        if (activity.ExecutionStatus == DossierActivityExecutionStatus.Completed)
        {
            return await _dossierService.GetAsync(dossierId, cancellationToken);
        }

        if (activity.ExecutionStatus == DossierActivityExecutionStatus.Cancelled)
        {
            throw new DomainValidationException("status", "Een geannuleerde activiteit kan niet afgerond worden; zet ze eerst terug naar gepland.");
        }

        RequirePlannedDate(activity);
        // The planned date is a tenant-local calendar day: compare with the tenant's TODAY, not the
        // UTC date (just after midnight in Brussels the UTC date is still yesterday).
        var zone = await TenantTimeZone.ForTenantAsync(_dbContext, TenantId, cancellationToken);
        var today = DateOnly.FromDateTime(TenantTimeZone.ToWallClock(Now, zone));
        if (activity.PlannedDate is { } planned && planned > today)
        {
            throw new DomainValidationException("plannedDate",
                $"Deze activiteit staat gepland op {planned:dd/MM/yyyy} en kan nog niet afgerond worden. Pas eerst de datum aan.");
        }

        var previous = activity.ExecutionStatus;
        activity.ExecutionStatus = DossierActivityExecutionStatus.Completed;
        activity.CompletedAt = Now;
        activity.CompletedByUserId = _currentUser.CurrentUserId;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityCompleted",
            new { ExecutionStatus = previous.ToString() },
            new { activity.Id, activity.Label, ExecutionStatus = activity.ExecutionStatus.ToString(), activity.CompletedAt, activity.CompletedByUserId },
            cancellationToken);

        await _lifecycle.TryAutoConfirmAsync(dossierId, Trigger(activity), cancellationToken);
        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> CancelAsync(
        Guid dossierId, Guid activityId, CancelDossierActivityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainValidationException("reason", "Geef een reden op om de activiteit te annuleren.");
        }

        if (request.Reason.Trim().Length > 500)
        {
            throw new DomainValidationException("reason", "De reden mag maximaal 500 tekens lang zijn.");
        }

        var loaded = await LoadAsync(dossierId, activityId, request.Version, "geannuleerd", cancellationToken);
        if (loaded is null) return null;
        var (dossier, activity) = loaded.Value;

        if (activity.ExecutionStatus == DossierActivityExecutionStatus.Cancelled)
        {
            throw new DomainValidationException("status", "Deze activiteit is al geannuleerd.");
        }

        if (activity.ExecutionStatus == DossierActivityExecutionStatus.Completed)
        {
            throw new DomainValidationException("status",
                "Een uitgevoerde activiteit annuleer je niet; zet ze eerst terug naar gepland als ze toch niet uitgevoerd werd.");
        }

        await RequireNotInvoicedAsync(activity, cancellationToken);

        var previous = activity.ExecutionStatus;
        activity.ExecutionStatus = DossierActivityExecutionStatus.Cancelled;
        activity.CancelledAt = Now;
        activity.CancelledByUserId = _currentUser.CurrentUserId;
        activity.CancellationReason = request.Reason.Trim();
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityCancelled",
            new { ExecutionStatus = previous.ToString() },
            new { activity.Id, activity.Label, ExecutionStatus = activity.ExecutionStatus.ToString(), activity.CancellationReason },
            cancellationToken);

        await _lifecycle.TryAutoConfirmAsync(dossierId, Trigger(activity), cancellationToken);
        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    public async Task<DossierDetailDto?> ReopenAsync(
        Guid dossierId, Guid activityId, DossierActivityTransitionRequest request, CancellationToken cancellationToken)
    {
        var loaded = await LoadAsync(dossierId, activityId, request.Version, "teruggezet", cancellationToken);
        if (loaded is null) return null;
        var (dossier, activity) = loaded.Value;

        if (activity.ExecutionStatus is not (DossierActivityExecutionStatus.Completed or DossierActivityExecutionStatus.Cancelled))
        {
            throw new DomainValidationException("status", "Alleen een afgeronde of geannuleerde activiteit kan teruggezet worden.");
        }

        await RequireNotInvoicedAsync(activity, cancellationToken);

        var before = new
        {
            ExecutionStatus = activity.ExecutionStatus.ToString(), activity.CompletedAt, activity.CancelledAt, activity.CancellationReason,
        };
        activity.ExecutionStatus = DossierActivityExecutionStatus.Planned;
        activity.StartedAt = null;
        activity.CompletedAt = null;
        activity.CompletedByUserId = null;
        activity.CancelledAt = null;
        activity.CancelledByUserId = null;
        activity.CancellationReason = null;
        dossier.Version = Guid.NewGuid();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, dossierId.ToString(), "ActivityReopened", before,
            new { activity.Id, activity.Label, ExecutionStatus = activity.ExecutionStatus.ToString() },
            cancellationToken);

        return await _dossierService.GetAsync(dossierId, cancellationToken);
    }

    // ------------------------------------------------------------ helpers

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    private static string Trigger(DossierActivity activity) =>
        $"activity:{(string.IsNullOrWhiteSpace(activity.Label) ? activity.ActivityType!.Name : activity.Label.Trim())}";

    private async Task<(TransportDossier Dossier, DossierActivity Activity)?> LoadAsync(
        Guid dossierId, Guid activityId, Guid? version, string verb, CancellationToken cancellationToken)
    {
        var dossier = await _dbContext.TransportDossiers
            .FirstOrDefaultAsync(d => d.TenantId == TenantId && d.Id == dossierId, cancellationToken);
        if (dossier is null)
        {
            return null;
        }

        DossierStateRules.RequireEditable(dossier, verb);

        if (version is { } expected && expected != dossier.Version)
        {
            throw new DossierVersionConflictException((await _dossierService.GetAsync(dossierId, cancellationToken))!);
        }

        var activity = await _dbContext.DossierActivities
            .Include(a => a.ActivityType)
            .FirstOrDefaultAsync(a => a.TenantId == TenantId && a.DossierId == dossierId && a.Id == activityId, cancellationToken)
            ?? throw new DomainValidationException("Deze activiteit bestaat niet.");

        if (activity.ActivityType!.HasStops)
        {
            throw new DomainValidationException(
                "Deze activiteit wordt uitgevoerd via haar transportopdracht; de status volgt de opdracht en de rit.");
        }

        return (dossier, activity);
    }

    private static void RequireNotResolved(DossierActivity activity, string verb)
    {
        if (activity.ExecutionStatus is DossierActivityExecutionStatus.Completed or DossierActivityExecutionStatus.Cancelled)
        {
            throw new DomainValidationException("status",
                $"Een {(activity.ExecutionStatus == DossierActivityExecutionStatus.Completed ? "afgeronde" : "geannuleerde")} activiteit kan niet {verb} worden; zet ze eerst terug naar gepland.");
        }
    }

    /// <summary>"Completion requires the activity to be planned": a planning-relevant type needs its date.</summary>
    private static void RequirePlannedDate(DossierActivity activity)
    {
        if (activity.ActivityType!.PlanningRelevant && activity.PlannedDate is null)
        {
            throw new DomainValidationException("plannedDate", "Plan de activiteit eerst in (datum) voor je ze start of afrondt.");
        }
    }

    private async Task RequireNotInvoicedAsync(DossierActivity activity, CancellationToken cancellationToken)
    {
        var onLiveInvoice = await _dbContext.InvoiceLines.AsNoTracking()
            .Where(l => l.TenantId == TenantId && l.DossierActivityId == activity.Id)
            .Join(_dbContext.Invoices.AsNoTracking().Where(i => i.Status != Modules.Invoicing.Entities.InvoiceStatus.Cancelled),
                l => l.InvoiceId, i => i.Id, (l, i) => i.Id)
            .AnyAsync(cancellationToken);
        if (onLiveInvoice)
        {
            throw new DomainValidationException("status", "Deze activiteit staat al op een factuur; haal ze daar eerst af.");
        }
    }

    /// <summary>
    /// The existing conflict engine on a transient (never saved) trip of the activity's date and
    /// time window: absence, employment, blocked/inactive driver, qualifications, licence rank,
    /// vehicle state, tachograph, and the operator's crane certificate when the type requires a crane
    /// (M8). Trip-only checks are dropped; double bookings only warn.
    /// </summary>
    private async Task<IReadOnlyList<PlanningConflictDto>> EvaluateConflictsAsync(
        DossierActivity activity, Guid? driverId, Guid? vehicleId, CancellationToken cancellationToken)
    {
        var result = new List<PlanningConflictDto>();
        if (driverId is null && vehicleId is null)
        {
            return result;
        }

        var probe = new Trip
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            TripNumber = "—",
            TripDate = activity.PlannedDate ?? DateOnly.FromDateTime(Now),
            DriverId = driverId,
            VehicleId = vehicleId,
            PlannedStart = StandaloneActivitySchedule.PlannedStart(activity.PlannedDate, activity.StartTime),
            PlannedEnd = StandaloneActivitySchedule.PlannedEnd(activity.PlannedDate, activity.StartTime, activity.DurationHours),
            Status = TripStatus.Draft,
        };

        // Without a date there is no day to check availability against: only resource state counts.
        foreach (var conflict in await _conflicts.EvaluateAsync(probe, activity.ActivityType!.RequiresCrane, cancellationToken))
        {
            if (TripOnlyCodes.Contains(conflict.Code))
            {
                continue;
            }

            if (SoftenedCodes.Contains(conflict.Code))
            {
                result.Add(PlanningConflictService.Enrich(
                    new PlanningConflictDto(conflict.Code, false, conflict.Description, ConflictSeverity.Warning), probe));
                continue;
            }

            result.Add(conflict);
        }

        if (activity.ActivityType!.RequiresCrane && vehicleId is { } vid)
        {
            var vehicle = await _dbContext.Vehicles.AsNoTracking()
                .Where(v => v.TenantId == TenantId && v.Id == vid)
                .Select(v => new { v.InternalNumber, v.HasCrane })
                .FirstOrDefaultAsync(cancellationToken);
            if (vehicle is { HasCrane: false })
            {
                result.Add(PlanningConflictService.Enrich(new PlanningConflictDto(PlanningConflictCode.OrderRequiresCrane, true,
                    $"{activity.ActivityType.Name} vereist een kraan; {vehicle.InternalNumber} heeft er geen."), probe));
            }
        }

        return result;
    }
}
