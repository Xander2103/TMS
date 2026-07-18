using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Hr.Services;

public class AbsenceService : IAbsenceService
{
    /// <summary>Default overview window when no explicit range is requested.</summary>
    public const int DefaultRangeDays = 60;

    private const string EntityType = "Absence";

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;

    public AbsenceService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

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

        if (!await _dbContext.Employees.AnyAsync(
                e => e.Id == employeeId && e.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            return AbsenceOperationResult.OwnerNotFound;
        }

        if (await HasOverlapAsync(employeeId, request.StartDate, request.EndDate, excludeId: null, cancellationToken))
        {
            return AbsenceOperationResult.Overlap;
        }

        var absence = new Absence
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            Type = request.Type,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = AbsenceStatus.Requested,
            Reason = Trim(request.Reason),
        };

        _dbContext.Add(absence);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, absence.Id.ToString(), "Created", null,
            new { absence.EmployeeId, absence.Type, absence.StartDate, absence.EndDate }, cancellationToken);

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
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

        var before = new { absence.Type, absence.StartDate, absence.EndDate };

        absence.Type = request.Type;
        absence.StartDate = request.StartDate;
        absence.EndDate = request.EndDate;
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

        if (absence.Status != AbsenceStatus.Requested)
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

        return AbsenceOperationResult.Success(await RequireDtoAsync(absence.Id, cancellationToken));
    }

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

        absence.Status = AbsenceStatus.Cancelled;

        await _dbContext.SaveChangesAsync(cancellationToken);

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
        r.Absence.Reason, r.Absence.DecisionNote, r.Absence.DecidedAt);

    private async Task<AbsenceDto> RequireDtoAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await Joined(TenantScoped().AsNoTracking().Where(a => a.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Absence {id} disappeared after save.");
        return Map(row);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
