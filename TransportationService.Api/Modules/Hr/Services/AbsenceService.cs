using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Hr.Services;

public class AbsenceService : IAbsenceService
{
    /// <summary>Default overview window when no explicit range is requested.</summary>
    public const int DefaultRangeDays = 60;

    private const string EntityType = "Absence";

    private static readonly string[] AllowedAttachmentExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly INotificationService _notificationService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ICalendarSyncService _calendarSyncService;
    private readonly TimeProvider _timeProvider;
    private readonly INotificationEventService? _notificationEvents;
    private readonly Microsoft.Extensions.Logging.ILogger<AbsenceService>? _logger;

    public AbsenceService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        INotificationService notificationService,
        IFileStorageService fileStorageService,
        ICalendarSyncService calendarSyncService,
        // Retained only for constructor-signature compatibility with existing call sites;
        // leave-decided (approve/reject) now queues its e-mail through PublishEventAsync
        // (NotificationEventService) instead of this service calling the outbox directly.
        IMessageOutboxService messageOutbox,
        TimeProvider timeProvider,
        INotificationEventService? notificationEvents = null,
        Microsoft.Extensions.Logging.ILogger<AbsenceService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _notificationService = notificationService;
        _fileStorageService = fileStorageService;
        _calendarSyncService = calendarSyncService;
        _ = messageOutbox;
        _timeProvider = timeProvider;
        _notificationEvents = notificationEvents;
        _logger = logger;
    }

    /// <summary>Fire-and-forget event publication: a notification failure never breaks the
    /// already-committed business operation.</summary>
    private async Task PublishEventAsync(string eventKey, NotificationEventContext context, CancellationToken cancellationToken)
    {
        if (_notificationEvents is null)
        {
            return;
        }

        try
        {
            await _notificationEvents.PublishAsync(eventKey, context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogError(exception, "Notification event '{EventKey}' failed to publish; business operation already committed.", eventKey);
        }
    }

    private static string? PartDayError(CreateAbsenceRequest request) =>
        request.PartDay != AbsencePartDay.FullDay && request.StartDate != request.EndDate
            ? "Een halve dag kan alleen bij een aanvraag van één dag."
            : null;

    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private IQueryable<Absence> TenantScoped() =>
        _dbContext.Absences.Where(a => a.TenantId == _tenantContext.TenantId);

    public async Task<IReadOnlyList<AbsenceDto>?> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Employees.AnyAsync(
                e => e.Id == employeeId && e.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return null;
        }

        var rows = await Joined(TenantScoped().AsNoTracking().Where(a => a.EmployeeId == employeeId))
            .ToListAsync(cancellationToken);

        // Bounded per-employee set; ordering through the record projection does not translate to SQL.
        return rows
            .OrderByDescending(r => r.Absence.StartDate)
            .Select(Map)
            .ToList();
    }

    public async Task<IReadOnlyList<AbsenceDto>> ListAsync(
        DateOnly? from, DateOnly? to, AbsenceType? type, AbsenceStatus? status, CancellationToken cancellationToken)
    {
        var rangeFrom = from ?? Today;
        var rangeTo = to ?? rangeFrom.AddDays(DefaultRangeDays);
        if (rangeTo < rangeFrom)
        {
            (rangeFrom, rangeTo) = (rangeTo, rangeFrom);
        }

        var query = TenantScoped().AsNoTracking()
            // Inclusive period overlap with the requested window.
            .Where(a => a.StartDate <= rangeTo && a.EndDate >= rangeFrom);

        if (type is { } t)
        {
            query = query.Where(a => a.Type == t);
        }

        if (status is { } s)
        {
            query = query.Where(a => a.Status == s);
        }

        var rows = await Joined(query).ToListAsync(cancellationToken);

        // Window-bounded set; ordering through the record projection does not translate to SQL.
        return rows
            .OrderBy(r => r.Absence.StartDate).ThenBy(r => r.EmployeeName)
            .Select(Map)
            .ToList();
    }

    public async Task<AbsenceOperationResult> CreateForEmployeeAsync(
        Guid employeeId, CreateAbsenceRequest request, CancellationToken cancellationToken)
    {
        if (request.EndDate < request.StartDate)
        {
            return AbsenceOperationResult.Invalid("De einddatum moet op of na de begindatum liggen.");
        }

        if (PartDayError(request) is { } partDayError)
        {
            return AbsenceOperationResult.Invalid(partDayError);
        }

        if (!await _dbContext.Employees.AnyAsync(
                e => e.Id == employeeId && e.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return AbsenceOperationResult.OwnerNotFound;
        }

        if (await HasOverlapAsync(employeeId, request.StartDate, request.EndDate, excludeId: null, cancellationToken))
        {
            return AbsenceOperationResult.Overlap;
        }

        var (resolvedType, leaveTypeId, leaveTypeError) =
            await ResolveLeaveTypeAsync(request.LeaveTypeId, request.Type, request.Reason, request.PartDay, cancellationToken);
        if (leaveTypeError is { } ltError)
        {
            return AbsenceOperationResult.Invalid(ltError);
        }

        var absence = new Absence
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            Type = resolvedType,
            LeaveTypeId = leaveTypeId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            PartDay = request.PartDay,
            Status = AbsenceStatus.Requested,
            Reason = Trim(request.Reason),
        };

        _dbContext.Add(absence);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "Created", null,
            new { absence.EmployeeId, absence.Type, absence.StartDate, absence.EndDate }, cancellationToken);

        var dto = await RequireDtoAsync(absence.Id, cancellationToken);

        // Notification ownership moved to the configurable event (Phase 6): NotificationEventCatalog's
        // leave_requested default recipient is InternalPermission(AbsencesApprove), replacing what
        // used to be a hardcoded NotifyPermissionHoldersAsync call here.
        await PublishEventAsync(MessageKinds.LeaveRequested, new NotificationEventContext(
            EntityType, absence.Id.ToString(),
            new Dictionary<string, string>
            {
                ["employeeName"] = dto.EmployeeName,
                ["period"] = $"{absence.StartDate:dd-MM-yyyy} t/m {absence.EndDate:dd-MM-yyyy}",
            })
        {
            EmployeeId = absence.EmployeeId,
            LinkPath = "/absences",
            InAppTitle = "Afwezigheid aangevraagd",
            InAppMessage = $"{dto.EmployeeName}: {absence.Type} van {absence.StartDate:dd-MM-yyyy} t/m {absence.EndDate:dd-MM-yyyy}.",
        }, cancellationToken);

        return AbsenceOperationResult.Success(dto);
    }

    public async Task<AbsenceOperationResult> UpdateAsync(
        Guid id, UpdateAbsenceRequest request, CancellationToken cancellationToken)
    {
        if (request.EndDate < request.StartDate)
        {
            return AbsenceOperationResult.Invalid("De einddatum moet op of na de begindatum liggen.");
        }

        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        if (absence.Status != AbsenceStatus.Requested)
        {
            return AbsenceOperationResult.InvalidState("Alleen aangevraagde afwezigheden kunnen worden bewerkt.");
        }

        if (await HasOverlapAsync(absence.EmployeeId, request.StartDate, request.EndDate, excludeId: id, cancellationToken))
        {
            return AbsenceOperationResult.Overlap;
        }

        if (request.PartDay != AbsencePartDay.FullDay && request.StartDate != request.EndDate)
        {
            return AbsenceOperationResult.Invalid("Een halve dag kan alleen bij een aanvraag van één dag.");
        }

        var before = new { absence.Type, absence.StartDate, absence.EndDate };

        absence.Type = request.Type;
        absence.StartDate = request.StartDate;
        absence.EndDate = request.EndDate;
        absence.PartDay = request.PartDay;
        absence.Reason = Trim(request.Reason);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "Updated", before,
            new { absence.Type, absence.StartDate, absence.EndDate }, cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<AbsenceOperationResult> DecideAsync(
        Guid id, DecideAbsenceRequest request, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        if (absence.Status is not (AbsenceStatus.Requested or AbsenceStatus.UnderReview))
        {
            return AbsenceOperationResult.InvalidState("Alleen aangevraagde afwezigheden kunnen worden goedgekeurd of afgewezen.");
        }

        var before = new { absence.Status };

        absence.Status = request.Approve ? AbsenceStatus.Approved : AbsenceStatus.Rejected;
        absence.DecisionNote = Trim(request.Note);
        absence.DecidedByUserId = _currentUserContext.CurrentUserId;
        absence.DecidedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(),
            request.Approve ? "Approved" : "Rejected", before,
            new { absence.Status, absence.DecisionNote }, cancellationToken);

        // Approved leave flows towards the (future) external calendar through the sync seam.
        if (request.Approve)
        {
            await _calendarSyncService.QueueAsync(new CalendarSyncEvent(
                "leave_approved", absence.Id, absence.EmployeeId, absence.StartDate, absence.EndDate,
                $"Afwezigheid: {absence.Type}"), cancellationToken);
        }

        // Notification ownership moved to the configurable event (Phase 6): leave_decided covers
        // both the in-app notice (was a hardcoded NotifyAsync) and the e-mail (was a direct
        // LeaveApproved/LeaveRejected outbox queue) in one call; its default recipient resolves
        // to the employee themselves (Driver = "subject employee of this event").
        var employeeName = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == absence.EmployeeId && e.TenantId == _tenantContext.TenantId)
            .Select(e => e.FirstName + " " + e.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var decisionLabel = request.Approve ? "Goedgekeurd" : "Afgewezen";
        await PublishEventAsync(MessageKinds.LeaveDecided, new NotificationEventContext(
            EntityType, absence.Id.ToString(),
            new Dictionary<string, string>
            {
                ["employeeName"] = employeeName,
                ["period"] = $"{absence.StartDate:dd-MM-yyyy} t/m {absence.EndDate:dd-MM-yyyy}",
                ["note"] = absence.DecisionNote ?? string.Empty,
                ["decision"] = decisionLabel,
            })
        {
            EmployeeId = absence.EmployeeId,
            LinkPath = "/portal/absences",
            InAppTitle = request.Approve ? "Afwezigheid goedgekeurd" : "Afwezigheid afgewezen",
            InAppMessage = $"Je {absence.Type} van {absence.StartDate:dd-MM-yyyy} t/m {absence.EndDate:dd-MM-yyyy} is {(request.Approve ? "goedgekeurd" : "afgewezen")}.",
        }, cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<AbsenceOperationResult> StartReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        if (absence.Status != AbsenceStatus.Requested)
        {
            return AbsenceOperationResult.InvalidState("Alleen een nieuwe aanvraag kan in behandeling worden genomen.");
        }

        var before = new { absence.Status };
        absence.Status = AbsenceStatus.UnderReview;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "ReviewStarted", before,
            new { absence.Status }, cancellationToken);

        await _notificationService.NotifyAsync(await EmployeeUserIdAsync(absence.EmployeeId, cancellationToken),
            "absence_decided", "Aanvraag in behandeling",
            $"Je aanvraag van {absence.StartDate:dd-MM-yyyy} t/m {absence.EndDate:dd-MM-yyyy} wordt bekeken door HR.",
            "/portal/absences", cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<AbsenceOperationResult> RequestChangesAsync(
        Guid id, RequestAbsenceChangesRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return AbsenceOperationResult.Invalid("Een toelichting voor de medewerker is verplicht.");
        }

        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        if (absence.Status is not (AbsenceStatus.Requested or AbsenceStatus.UnderReview))
        {
            return AbsenceOperationResult.InvalidState("Wijzigingen vragen kan alleen bij een openstaande aanvraag.");
        }

        var note = request.Note.Trim();
        if (request.ProposedStartDate is { } proposedStart && request.ProposedEndDate is { } proposedEnd)
        {
            note = $"{note} Voorstel: {proposedStart:dd-MM-yyyy} t/m {proposedEnd:dd-MM-yyyy}.";
        }

        var before = new { absence.Status, absence.DecisionNote };
        absence.Status = AbsenceStatus.Requested;
        absence.DecisionNote = note;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "ChangesRequested", before,
            new { absence.DecisionNote }, cancellationToken);

        await _notificationService.NotifyAsync(await EmployeeUserIdAsync(absence.EmployeeId, cancellationToken),
            "absence_changes_requested", "HR vraagt een aanpassing",
            note, "/portal/absences", cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<AbsenceOperationResult> SetInternalNoteAsync(Guid id, string? note, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        var before = new { absence.InternalNote };
        absence.InternalNote = Trim(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "InternalNoteUpdated", before,
            new { absence.InternalNote }, cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<AbsenceReviewContextDto?> GetReviewContextAsync(Guid id, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;

        var shifts = await _dbContext.Shifts.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == absence.EmployeeId
                        && s.Date >= absence.StartDate && s.Date <= absence.EndDate)
            .OrderBy(s => s.Date)
            .Select(s => new OverlappingShiftDto(s.Date, s.StartTime, s.EndTime, s.WorkLocation))
            .ToListAsync(cancellationToken);

        var trips = new List<OverlappingTripDto>();
        var driverId = await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.EmployeeId == absence.EmployeeId)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (driverId is { } drv)
        {
            trips = await _dbContext.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.DriverId == drv
                            && t.TripDate >= absence.StartDate && t.TripDate <= absence.EndDate
                            && t.Status != TripStatus.Cancelled)
                .OrderBy(t => t.TripDate)
                .Select(t => new OverlappingTripDto(t.Id, t.TripNumber, t.TripDate))
                .ToListAsync(cancellationToken);
        }

        var departmentId = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == absence.EmployeeId && e.TenantId == tenantId)
            .Select(e => e.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);

        var colleaguesQuery = TenantScoped().AsNoTracking()
            .Where(a => a.Id != absence.Id && a.EmployeeId != absence.EmployeeId
                        && (a.Status == AbsenceStatus.Requested || a.Status == AbsenceStatus.UnderReview
                            || a.Status == AbsenceStatus.Approved)
                        && a.StartDate <= absence.EndDate && a.EndDate >= absence.StartDate);
        var colleagueRows = await (from a in colleaguesQuery
                                   join e in _dbContext.Employees.AsNoTracking()
                                           .Where(e => e.TenantId == tenantId
                                                       && (departmentId == null || e.DepartmentId == departmentId))
                                       on a.EmployeeId equals e.Id
                                   select new OverlappingColleagueDto(
                                       e.FirstName + " " + e.LastName, a.Type, a.StartDate, a.EndDate, a.Status))
            .ToListAsync(cancellationToken);

        // Balance architecture: used approved vacation days in the request's start year.
        var yearStart = new DateOnly(absence.StartDate.Year, 1, 1);
        var yearEnd = new DateOnly(absence.StartDate.Year, 12, 31);
        var approvedVacations = await TenantScoped().AsNoTracking()
            .Where(a => a.EmployeeId == absence.EmployeeId && a.Id != absence.Id
                        && a.Type == AbsenceType.Vacation && a.Status == AbsenceStatus.Approved
                        && a.StartDate <= yearEnd && a.EndDate >= yearStart)
            .Select(a => new { a.StartDate, a.EndDate, a.PartDay })
            .ToListAsync(cancellationToken);
        var usedDays = approvedVacations.Sum(a =>
        {
            var start = a.StartDate < yearStart ? yearStart : a.StartDate;
            var end = a.EndDate > yearEnd ? yearEnd : a.EndDate;
            var days = end.DayNumber - start.DayNumber + 1;
            return a.PartDay == AbsencePartDay.FullDay ? days : days - 0;
        });

        return new AbsenceReviewContextDto(shifts, trips, colleagueRows, usedDays, absence.AttachmentPath is not null);
    }

    public async Task<AbsenceOperationResult> AttachDocumentAsync(
        Guid id, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedAttachmentExtensions.Contains(extension))
        {
            return AbsenceOperationResult.Invalid("Alleen pdf-, jpg- of png-bestanden zijn toegestaan.");
        }

        if (absence.AttachmentPath is { } existing)
        {
            await _fileStorageService.DeleteAsync(existing, cancellationToken);
        }

        absence.AttachmentPath = await _fileStorageService.SaveAsync(
            _tenantContext.TenantId, "absence-attachments", fileName, content, cancellationToken);
        absence.AttachmentFileName = fileName;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "AttachmentAdded", null,
            new { fileName }, cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<(Stream Content, string FileName)?> OpenDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence?.AttachmentPath is not { } path)
        {
            return null;
        }

        var stream = await _fileStorageService.OpenReadAsync(path, cancellationToken);
        return (stream, absence.AttachmentFileName ?? "bijlage");
    }

    private async Task<Guid?> EmployeeUserIdAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await _dbContext.Users.AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId && u.EmployeeId == employeeId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AbsenceOperationResult> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return AbsenceOperationResult.NotFound;
        }

        if (absence.Status is not (AbsenceStatus.Requested or AbsenceStatus.Approved))
        {
            return AbsenceOperationResult.InvalidState("Alleen aangevraagde of goedgekeurde afwezigheden kunnen worden geannuleerd.");
        }

        var before = new { absence.Status };
        var wasApproved = absence.Status == AbsenceStatus.Approved;

        absence.Status = AbsenceStatus.Cancelled;

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Withdrawn approved leave disappears from the external calendar too.
        if (wasApproved)
        {
            await _calendarSyncService.CancelAsync("leave_approved", absence.Id, cancellationToken);
        }

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "Cancelled", before,
            new { absence.Status }, cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var absence = await TenantScoped().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (absence is null)
        {
            return false;
        }

        _dbContext.Remove(absence); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "Deleted",
            new { absence.EmployeeId, absence.StartDate, absence.EndDate }, null, cancellationToken);

        return true;
    }

    /// <summary>Requested and approved absences block the period; rejected/cancelled ones do not.</summary>
    private Task<bool> HasOverlapAsync(
        Guid employeeId, DateOnly start, DateOnly end, Guid? excludeId, CancellationToken cancellationToken) =>
        TenantScoped().AnyAsync(a =>
            a.EmployeeId == employeeId
            && (excludeId == null || a.Id != excludeId)
            && (a.Status == AbsenceStatus.Requested || a.Status == AbsenceStatus.Approved)
            && a.StartDate <= end && a.EndDate >= start, cancellationToken);

    private sealed record JoinedAbsence(Absence Absence, string EmployeeName, string EmployeeNumber, bool IsDriver);

    private IQueryable<JoinedAbsence> Joined(IQueryable<Absence> absences) =>
        from a in absences
        join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == _tenantContext.TenantId)
            on a.EmployeeId equals e.Id
        select new JoinedAbsence(
            a,
            e.FirstName + " " + e.LastName,
            e.EmployeeNumber,
            _dbContext.Drivers.Any(d => d.TenantId == _tenantContext.TenantId && d.EmployeeId == e.Id));

    private static AbsenceDto Map(JoinedAbsence r) => new(
        r.Absence.Id, r.Absence.EmployeeId, r.EmployeeName, r.EmployeeNumber, r.IsDriver,
        r.Absence.Type, r.Absence.StartDate, r.Absence.EndDate, r.Absence.Status,
        r.Absence.Reason, r.Absence.DecisionNote, r.Absence.DecidedAt,
        r.Absence.PartDay, r.Absence.InternalNote,
        r.Absence.AttachmentPath != null, r.Absence.AttachmentFileName, r.Absence.LeaveTypeId);

    /// <summary>
    /// Resolves an optional configurable leave type to the effective <see cref="AbsenceType"/>
    /// (leave type is the source of truth) and validates its per-request rules. Returns an error
    /// message when the type is unknown/inactive or a rule is violated; null leave type keeps the
    /// caller's fallback type (legacy/backward-compatible path).
    /// </summary>
    private async Task<(AbsenceType Type, Guid? LeaveTypeId, string? Error)> ResolveLeaveTypeAsync(
        Guid? leaveTypeId, AbsenceType fallbackType, string? reason, AbsencePartDay partDay, CancellationToken cancellationToken)
    {
        if (leaveTypeId is not { } id) return (fallbackType, null, null);
        var leaveType = await _dbContext.LeaveTypes
            .FirstOrDefaultAsync(t => t.TenantId == _tenantContext.TenantId && t.Id == id, cancellationToken);
        if (leaveType is null) return (fallbackType, null, "Onbekend verloftype.");
        if (!leaveType.IsActive) return (fallbackType, null, "Dit verloftype is niet meer actief.");
        if (leaveType.RequiresReason && string.IsNullOrWhiteSpace(reason))
            return (leaveType.AbsenceType, id, "Voor dit verloftype is een reden verplicht.");
        if (!leaveType.AllowsHalfDays && partDay != AbsencePartDay.FullDay)
            return (leaveType.AbsenceType, id, "Halve dagen zijn niet toegestaan voor dit verloftype.");
        return (leaveType.AbsenceType, id, null);
    }

    private async Task<AbsenceDto> RequireDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await Joined(TenantScoped().AsNoTracking().Where(a => a.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Absence {id} disappeared after save.");
        return Map(row);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
