using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Attendance.Services;

public interface IAttendanceCorrectionService
{
    Task<AttendanceCorrectionResult> CorrectSessionAsync(Guid sessionId, CorrectSessionRequest request, CancellationToken cancellationToken);
    Task<AttendanceCorrectionResult> CorrectBreakAsync(Guid sessionId, Guid breakId, CorrectBreakRequest request, CancellationToken cancellationToken);
    Task<AttendanceCorrectionResult> CancelSessionAsync(Guid sessionId, CancelSessionRequest request, CancellationToken cancellationToken);
    Task<AttendanceCorrectionResult> CreateManualSessionAsync(CreateManualSessionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Manuele correcties door HR. Grondprincipes (spec §24/§67): history wordt NOOIT
/// overschreven alsof het origineel niet bestond — elke wijziging bewaart de oude
/// waarde in een AttendanceCorrection-rij, voegt een ManualCorrection-event toe aan de
/// immutable timeline en gaat de algemene auditlog in. Reden is altijd verplicht.
/// Verwijderen bestaat niet: een foutieve registratie wordt geannuleerd (Cancelled) en
/// blijft daarmee traceerbaar. Optimistic concurrency via het Version-token.
/// </summary>
public class AttendanceCorrectionService : IAttendanceCorrectionService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public AttendanceCorrectionService(
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

    public async Task<AttendanceCorrectionResult> CorrectSessionAsync(
        Guid sessionId, CorrectSessionRequest request, CancellationToken cancellationToken)
    {
        if (ValidateReason(request.Reason) is { } reasonError)
        {
            return reasonError;
        }

        var session = await Scoped().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.NotFound, "Registratie niet gevonden.");
        }

        if (session.Status == AttendanceSessionStatus.Cancelled)
        {
            // Eindtoestand: een geannuleerde registratie mag nooit stil weer gaan
            // meetellen. Herstel loopt via een nieuwe manuele sessie.
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Een geannuleerde registratie kan niet gecorrigeerd worden. Maak zo nodig een manuele registratie aan.");
        }

        if (request.Version is { } expected && expected != session.Version)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.StaleVersion,
                "De registratie is intussen gewijzigd. Herlaad en probeer opnieuw.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var newClockIn = request.ClockInAt ?? session.ClockInAt;
        var newClockOut = request.ClockOutAt ?? session.ClockOutAt;

        if (newClockOut is { } co && co <= newClockIn)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Het uitpuntmoment moet na het inpuntmoment liggen.");
        }

        if (newClockIn > now || (newClockOut is { } futureCo && futureCo > now))
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Een registratie kan niet in de toekomst liggen.");
        }

        if (await OverlapsOtherSessionAsync(session.EmployeeId, sessionId, newClockIn, newClockOut, cancellationToken))
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.OverlapsOtherSession,
                "De gecorrigeerde tijden overlappen met een andere registratie van deze medewerker.");
        }

        var breaks = await SessionBreaksAsync(sessionId, cancellationToken);
        var effectiveEnd = newClockOut ?? now;
        if (breaks.Any(b => b.StartedAt < newClockIn || (b.EndedAt ?? effectiveEnd) > effectiveEnd))
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Er vallen pauzes buiten de gecorrigeerde sessietijden. Corrigeer eerst de pauzes.");
        }

        var reason = request.Reason.Trim();
        var old = new { session.ClockInAt, session.ClockOutAt, session.Status };
        var correctionsAdded = 0;

        if (request.ClockInAt is { } clockIn && clockIn != session.ClockInAt)
        {
            AddCorrection(session, AttendanceCorrectionKind.ClockIn, null, session.ClockInAt, clockIn, reason, now);
            session.ClockInAt = clockIn;
            correctionsAdded++;
        }

        if (request.ClockOutAt is { } clockOut && clockOut != session.ClockOutAt)
        {
            // Vergeten uitpunt: een nog open pauze wordt op het uitpuntmoment gesloten,
            // met een eigen correctierij zodat ook dat traceerbaar is.
            var openBreak = breaks.FirstOrDefault(b => b.EndedAt is null);
            if (openBreak is not null)
            {
                AddCorrection(session, AttendanceCorrectionKind.BreakEnd, openBreak.Id, null, clockOut, reason, now);
                openBreak.EndedAt = clockOut;
            }

            AddCorrection(session, AttendanceCorrectionKind.ClockOut, null, session.ClockOutAt, clockOut, reason, now);
            session.ClockOutAt = clockOut;
            session.ClockOutSource ??= AttendanceSource.Manual;
            session.Status = AttendanceSessionStatus.Completed;
            correctionsAdded++;
        }

        if (correctionsAdded == 0)
        {
            // Niets gewijzigd: geen correctievlag, geen auditruis.
            return await ResultAsync(session, cancellationToken);
        }

        session.HasCorrections = true;
        if (await SaveCorrectionAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await _auditService.RecordAsync("AttendanceSession", session.Id.ToString(), "Corrected",
            old, new { session.ClockInAt, session.ClockOutAt, session.Status, Reason = reason }, cancellationToken);

        return await ResultAsync(session, cancellationToken);
    }

    public async Task<AttendanceCorrectionResult> CorrectBreakAsync(
        Guid sessionId, Guid breakId, CorrectBreakRequest request, CancellationToken cancellationToken)
    {
        if (ValidateReason(request.Reason) is { } reasonError)
        {
            return reasonError;
        }

        var session = await Scoped().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.NotFound, "Registratie niet gevonden.");
        }

        if (session.Status == AttendanceSessionStatus.Cancelled)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Een geannuleerde registratie kan niet gecorrigeerd worden.");
        }

        if (request.Version is { } expected && expected != session.Version)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.StaleVersion,
                "De registratie is intussen gewijzigd. Herlaad en probeer opnieuw.");
        }

        var breaks = await SessionBreaksAsync(sessionId, cancellationToken);
        var target = breaks.FirstOrDefault(b => b.Id == breakId);
        if (target is null)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.NotFound, "Pauze niet gevonden.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var newStart = request.StartedAt ?? target.StartedAt;
        var newEnd = request.EndedAt ?? target.EndedAt;

        if (newEnd is { } end && end <= newStart)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Het einde van de pauze moet na het begin liggen.");
        }

        var sessionEnd = session.ClockOutAt ?? now;
        if (newStart < session.ClockInAt || (newEnd ?? sessionEnd) > sessionEnd)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "De pauze moet binnen de sessietijden vallen.");
        }

        // Overlappende pauzes dubbeltellen in de pauzeduur en drukken de netto werktijd:
        // het gecorrigeerde interval mag geen andere pauze van de sessie raken.
        var newEndEffective = newEnd ?? sessionEnd;
        if (breaks.Any(b => b.Id != target.Id
                            && b.StartedAt < newEndEffective
                            && (b.EndedAt ?? sessionEnd) > newStart))
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "De pauze overlapt met een andere pauze van deze sessie.");
        }

        var reason = request.Reason.Trim();
        var correctionsAdded = 0;

        if (request.StartedAt is { } started && started != target.StartedAt)
        {
            AddCorrection(session, AttendanceCorrectionKind.BreakStart, target.Id, target.StartedAt, started, reason, now);
            target.StartedAt = started;
            correctionsAdded++;
        }

        if (request.EndedAt is { } ended && ended != target.EndedAt)
        {
            AddCorrection(session, AttendanceCorrectionKind.BreakEnd, target.Id, target.EndedAt, ended, reason, now);
            var wasOpen = target.EndedAt is null;
            target.EndedAt = ended;
            if (wasOpen && session.Status == AttendanceSessionStatus.OnBreak)
            {
                session.Status = AttendanceSessionStatus.Working;
            }

            correctionsAdded++;
        }

        if (correctionsAdded == 0)
        {
            return await ResultAsync(session, cancellationToken);
        }

        session.HasCorrections = true;
        if (await SaveCorrectionAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await _auditService.RecordAsync("AttendanceSession", session.Id.ToString(), "BreakCorrected",
            new { BreakId = breakId }, new { target.StartedAt, target.EndedAt, Reason = reason }, cancellationToken);

        return await ResultAsync(session, cancellationToken);
    }

    public async Task<AttendanceCorrectionResult> CancelSessionAsync(
        Guid sessionId, CancelSessionRequest request, CancellationToken cancellationToken)
    {
        if (ValidateReason(request.Reason) is { } reasonError)
        {
            return reasonError;
        }

        var session = await Scoped().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.NotFound, "Registratie niet gevonden.");
        }

        if (request.Version is { } expected && expected != session.Version)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.StaleVersion,
                "De registratie is intussen gewijzigd. Herlaad en probeer opnieuw.");
        }

        if (session.Status == AttendanceSessionStatus.Cancelled)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Deze registratie is al geannuleerd.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var reason = request.Reason.Trim();
        var old = new { session.Status, session.ClockInAt, session.ClockOutAt };

        // Een nog open pauze/sessie afsluiten zodat de unieke actieve-sessie-index
        // vrijkomt; de sessie telt door de Cancelled-status nergens meer mee.
        var openBreak = (await SessionBreaksAsync(sessionId, cancellationToken)).FirstOrDefault(b => b.EndedAt is null);
        if (openBreak is not null)
        {
            openBreak.EndedAt = now;
        }

        session.ClockOutAt ??= now;
        session.ClockOutSource ??= AttendanceSource.Manual;
        session.Status = AttendanceSessionStatus.Cancelled;
        session.HasCorrections = true;
        AddCorrection(session, AttendanceCorrectionKind.SessionCancelled, null, old.ClockInAt, null, reason, now,
            AttendanceEventType.SessionCancelled);

        if (await SaveCorrectionAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await _auditService.RecordAsync("AttendanceSession", session.Id.ToString(), "Cancelled",
            old, new { session.Status, Reason = reason }, cancellationToken);

        return await ResultAsync(session, cancellationToken);
    }

    public async Task<AttendanceCorrectionResult> CreateManualSessionAsync(
        CreateManualSessionRequest request, CancellationToken cancellationToken)
    {
        if (ValidateReason(request.Reason) is { } reasonError)
        {
            return reasonError;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.ClockOutAt <= request.ClockInAt || request.ClockOutAt > now)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "De sessietijden zijn ongeldig (einde vóór begin of in de toekomst).");
        }

        var employeeExists = await _dbContext.Employees.AsNoTracking()
            .AnyAsync(e => e.TenantId == _tenantContext.TenantId && e.Id == request.EmployeeId, cancellationToken);
        if (!employeeExists)
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.NotFound, "Medewerker niet gevonden.");
        }

        var requestedBreaks = (request.Breaks ?? []).OrderBy(b => b.StartedAt).ToList();
        foreach (var b in requestedBreaks)
        {
            if (b.EndedAt <= b.StartedAt || b.StartedAt < request.ClockInAt || b.EndedAt > request.ClockOutAt)
            {
                return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                    "Elke pauze moet binnen de sessietijden vallen en een geldig begin/einde hebben.");
            }
        }

        for (var i = 1; i < requestedBreaks.Count; i++)
        {
            if (requestedBreaks[i].StartedAt < requestedBreaks[i - 1].EndedAt)
            {
                return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                    "Pauzes mogen elkaar niet overlappen.");
            }
        }

        if (await OverlapsOtherSessionAsync(request.EmployeeId, null, request.ClockInAt, request.ClockOutAt, cancellationToken))
        {
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.OverlapsOtherSession,
                "De tijden overlappen met een bestaande registratie van deze medewerker.");
        }

        var reason = request.Reason.Trim();
        var session = new AttendanceSession
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = request.EmployeeId,
            ClockInAt = request.ClockInAt,
            ClockOutAt = request.ClockOutAt,
            Status = AttendanceSessionStatus.Completed,
            ClockInSource = AttendanceSource.Manual,
            ClockOutSource = AttendanceSource.Manual,
            HasCorrections = true,
        };
        _dbContext.AttendanceSessions.Add(session);

        foreach (var b in requestedBreaks)
        {
            _dbContext.AttendanceBreaks.Add(new AttendanceBreak
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                SessionId = session.Id,
                EmployeeId = request.EmployeeId,
                StartedAt = b.StartedAt,
                EndedAt = b.EndedAt,
            });
        }

        AddCorrection(session, AttendanceCorrectionKind.ManualSession, null, null, request.ClockInAt, reason, now,
            AttendanceEventType.ManualSessionCreated);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("AttendanceSession", session.Id.ToString(), "ManualCreated",
            null,
            new { session.EmployeeId, session.ClockInAt, session.ClockOutAt, Breaks = request.Breaks?.Count ?? 0, Reason = reason },
            cancellationToken);

        return await ResultAsync(session, cancellationToken);
    }

    // ── Intern ───────────────────────────────────────────────────────────────────

    private IQueryable<AttendanceSession> Scoped() =>
        _dbContext.AttendanceSessions.Where(s => s.TenantId == _tenantContext.TenantId);

    private Task<List<AttendanceBreak>> SessionBreaksAsync(Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.AttendanceBreaks
            .Where(b => b.TenantId == _tenantContext.TenantId && b.SessionId == sessionId)
            .ToListAsync(cancellationToken);

    private Task<bool> OverlapsOtherSessionAsync(
        Guid employeeId, Guid? exceptSessionId, DateTime start, DateTime? end, CancellationToken cancellationToken)
    {
        var effectiveEnd = end ?? DateTime.MaxValue;
        return Scoped().AnyAsync(s =>
                s.EmployeeId == employeeId
                && s.Id != exceptSessionId
                && s.Status != AttendanceSessionStatus.Cancelled
                && s.ClockInAt < effectiveEnd
                && (s.ClockOutAt == null || s.ClockOutAt > start),
            cancellationToken);
    }

    /// <summary>SaveChanges met concurrency-token-bewaking: een verloren race wordt een expliciete StaleVersion, nooit een stille overschrijving.</summary>
    private async Task<AttendanceCorrectionResult?> SaveCorrectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.StaleVersion,
                "De registratie is intussen gewijzigd. Herlaad en probeer opnieuw.");
        }
    }

    private static AttendanceCorrectionResult? ValidateReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? AttendanceCorrectionResult.Fail(AttendanceCorrectionOutcome.ValidationFailed,
                "Een reden is verplicht bij elke correctie.")
            : null;

    private void AddCorrection(
        AttendanceSession session,
        AttendanceCorrectionKind kind,
        Guid? breakId,
        DateTime? oldValue,
        DateTime? newValue,
        string reason,
        DateTime now,
        AttendanceEventType eventType = AttendanceEventType.ManualCorrection)
    {
        var correction = new AttendanceCorrection
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            SessionId = session.Id,
            EmployeeId = session.EmployeeId,
            BreakId = breakId,
            Kind = kind,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = reason,
        };
        _dbContext.AttendanceCorrections.Add(correction);
        _dbContext.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            SessionId = session.Id,
            EmployeeId = session.EmployeeId,
            EventType = eventType,
            OccurredAt = now,
            Source = AttendanceSource.Manual,
            CorrectionId = correction.Id,
            Note = reason,
        });
    }

    private async Task<AttendanceCorrectionResult> ResultAsync(AttendanceSession session, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var breaks = await SessionBreaksAsync(session.Id, cancellationToken);
        var corrections = await _dbContext.AttendanceCorrections.AsNoTracking()
            .Where(c => c.TenantId == _tenantContext.TenantId && c.SessionId == session.Id)
            .ToListAsync(cancellationToken);

        var correctorIds = corrections.Where(c => c.CreatedByUserId is not null)
            .Select(c => c.CreatedByUserId!.Value).Distinct().ToList();
        var correctorNames = correctorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId && correctorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var locationNames = session.LocationId is { } locationId
            ? await _dbContext.Locations.AsNoTracking()
                .Where(l => l.TenantId == _tenantContext.TenantId && l.Id == locationId)
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        return new AttendanceCorrectionResult(
            AttendanceCorrectionOutcome.Success,
            AttendanceMapper.ToSessionDto(session, breaks, corrections, correctorNames, locationNames, now),
            null);
    }
}
