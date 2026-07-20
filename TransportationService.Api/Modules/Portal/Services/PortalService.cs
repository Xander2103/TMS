using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using AbsenceOperationOutcome = TransportationService.Api.Modules.Hr.Dtos.AbsenceOperationOutcome;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Portal.Services;

public class PortalService : IPortalService
{
    private const int MinPasswordLength = 8;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly IAbsenceService _absenceService;
    private readonly IQualificationService _qualificationService;
    private readonly INotificationService _notificationService;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly IShiftService _shiftService;
    private readonly TimeProvider _timeProvider;

    public PortalService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext,
        IAbsenceService absenceService,
        IQualificationService qualificationService,
        INotificationService notificationService,
        IQualificationStatusCalculator statusCalculator,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        IShiftService shiftService,
        TimeProvider timeProvider)
    {
        _shiftService = shiftService;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUserContext = currentUserContext;
        _absenceService = absenceService;
        _qualificationService = qualificationService;
        _notificationService = notificationService;
        _statusCalculator = statusCalculator;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _timeProvider = timeProvider;
    }

    /// <summary>The one and only employee resolution: via the logged-in user's employee link.</summary>
    private async Task<Guid?> MyEmployeeIdAsync(CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return null;
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.TenantId == _tenantContext.TenantId)
            .Select(u => u.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<MyProfileDto?> GetMyProfileAsync(CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .Include(e => e.JobFunctions)
            .ThenInclude(jf => jf.JobFunction)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var departmentName = employee.DepartmentId is { } departmentId
            ? await _dbContext.Departments.AsNoTracking()
                .Where(d => d.Id == departmentId && d.TenantId == _tenantContext.TenantId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var driver = await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.TenantId == _tenantContext.TenantId)
            .Select(d => new { d.DriverNumber })
            .FirstOrDefaultAsync(cancellationToken);

        return new MyProfileDto(
            employee.Id, employee.EmployeeNumber, employee.FirstName, employee.LastName,
            employee.Email, employee.PhoneNumber, employee.MobilePhone,
            employee.Street, employee.HouseNumber, employee.PostalCode, employee.City, employee.CountryCode,
            employee.EmergencyContactName, employee.EmergencyContactPhone,
            employee.EmploymentStartDate, employee.EmploymentStatus.ToString(),
            departmentName,
            employee.JobFunctions.Select(jf => jf.JobFunction?.Name).Where(n => n is not null).Cast<string>().ToList(),
            driver is not null, driver?.DriverNumber);
    }

    public async Task<MyDashboardDto?> GetMyDashboardAsync(CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var firstName = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => e.FirstName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var unread = await _notificationService.UnreadCountAsync(cancellationToken);

        var openRequests = await _dbContext.Absences.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId
                             && a.Status == AbsenceStatus.Requested, cancellationToken);

        var upcomingApproved = await _dbContext.Absences.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.EmployeeId == employeeId
                             && a.Status == AbsenceStatus.Approved && a.EndDate >= today, cancellationToken);

        var qualifications = await _qualificationService.ListForEmployeeAsync(employeeId, cancellationToken);
        var expiring = qualifications.Count(q =>
            q.EffectiveStatus is QualificationStatus.ExpiringSoon or QualificationStatus.Expired);

        var driverId = await _dbContext.Drivers.AsNoTracking()
            .Where(d => d.EmployeeId == employeeId && d.TenantId == tenantId)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var tripsToday = 0;
        var tripsThisWeek = 0;
        if (driverId is { } drv)
        {
            var weekEnd = today.AddDays(7);
            var tripDates = await _dbContext.Trips.AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.DriverId == drv
                            && t.TripDate >= today && t.TripDate <= weekEnd
                            && t.Status != TripStatus.Cancelled && t.Status != TripStatus.Draft)
                .Select(t => t.TripDate)
                .ToListAsync(cancellationToken);
            tripsToday = tripDates.Count(d => d == today);
            tripsThisWeek = tripDates.Count;
        }

        return new MyDashboardDto(
            firstName, unread, openRequests, upcomingApproved, expiring,
            driverId is not null, tripsToday, tripsThisWeek);
    }

    public async Task<IReadOnlyList<AbsenceDto>?> ListMyAbsencesAsync(CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        var absences = await _absenceService.ListForEmployeeAsync(employeeId, cancellationToken);
        // HR-internal notes never leave the back office.
        return absences?.Select(a => a with { InternalNote = null }).ToList();
    }

    public async Task<PortalAbsenceResult> AttachMyAbsenceDocumentAsync(
        Guid absenceId, string fileName, Stream content, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return PortalAbsenceResult.NoEmployeeLink;
        }

        var absence = await _dbContext.Absences.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.TenantId == _tenantContext.TenantId
                                      && a.EmployeeId == employeeId, cancellationToken);
        if (absence is null)
        {
            return PortalAbsenceResult.NotFound;
        }

        if (absence.Status is not (AbsenceStatus.Requested or AbsenceStatus.UnderReview))
        {
            return PortalAbsenceResult.InvalidState("Bijlagen kunnen alleen bij een openstaande aanvraag.");
        }

        var result = await _absenceService.AttachDocumentAsync(absenceId, fileName, content, cancellationToken);
        return result.Outcome == AbsenceOperationOutcome.Success
            ? PortalAbsenceResult.Success(result.Absence! with { InternalNote = null })
            : Map(result);
    }

    public async Task<(Stream Content, string FileName)?> OpenMyAbsenceDocumentAsync(
        Guid absenceId, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        var belongsToMe = await _dbContext.Absences.AsNoTracking()
            .AnyAsync(a => a.Id == absenceId && a.TenantId == _tenantContext.TenantId
                           && a.EmployeeId == employeeId, cancellationToken);
        return belongsToMe ? await _absenceService.OpenDocumentAsync(absenceId, cancellationToken) : null;
    }

    public async Task<PortalAbsenceResult> CreateMyAbsenceAsync(
        CreateAbsenceRequest request, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return PortalAbsenceResult.NoEmployeeLink;
        }

        var result = await _absenceService.CreateForEmployeeAsync(employeeId, request, cancellationToken);
        return Map(result);
    }

    public async Task<PortalAbsenceResult> CancelMyAbsenceAsync(Guid absenceId, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return PortalAbsenceResult.NoEmployeeLink;
        }

        var absence = await _dbContext.Absences.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == absenceId && a.TenantId == _tenantContext.TenantId
                                      && a.EmployeeId == employeeId, cancellationToken);
        if (absence is null)
        {
            return PortalAbsenceResult.NotFound;
        }

        // Self-service may only withdraw a request that HR has not decided yet.
        if (absence.Status != AbsenceStatus.Requested)
        {
            return PortalAbsenceResult.InvalidState(
                "Alleen een nog niet besliste aanvraag kan worden ingetrokken; neem contact op met HR.");
        }

        return Map(await _absenceService.CancelAsync(absenceId, cancellationToken));
    }

    public async Task<IReadOnlyList<EmployeeQualificationDto>?> ListMyQualificationsAsync(CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        return await _qualificationService.ListForEmployeeAsync(employeeId, cancellationToken);
    }

    public async Task<(Stream Content, string FileName)?> OpenMyQualificationDocumentAsync(
        Guid qualificationId, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        // Ownership first: a colleague's qualification id opens nothing.
        var belongsToMe = await _dbContext.EmployeeQualifications.AsNoTracking()
            .AnyAsync(q => q.Id == qualificationId && q.TenantId == _tenantContext.TenantId
                           && q.EmployeeId == employeeId, cancellationToken);
        if (!belongsToMe)
        {
            return null;
        }

        return await _qualificationService.OpenDocumentAsync(employeeId, qualificationId, cancellationToken);
    }

    public async Task<PortalOperationResult> ChangeMyPasswordAsync(
        ChangeMyPasswordRequest request, CancellationToken cancellationToken)
    {
        if (_currentUserContext.CurrentUserId is not { } userId)
        {
            return PortalOperationResult.NotFound;
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < MinPasswordLength)
        {
            return PortalOperationResult.Invalid($"Het nieuwe wachtwoord moet minstens {MinPasswordLength} tekens lang zijn.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null)
        {
            return PortalOperationResult.NotFound;
        }

        if (_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return PortalOperationResult.Invalid("Het huidige wachtwoord is niet correct.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false; // the user now owns their credential
        user.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "PasswordChanged", null,
            new { Self = true }, cancellationToken);

        return PortalOperationResult.Success;
    }

    public async Task<IReadOnlyList<ScheduleDayDto>?> GetMyPlanningAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (await MyEmployeeIdAsync(cancellationToken) is not { } employeeId)
        {
            return null;
        }

        return await _shiftService.GetEmployeeScheduleAsync(employeeId, from, to, cancellationToken);
    }

    private static PortalAbsenceResult Map(AbsenceOperationResult result) => result.Outcome switch
    {
        AbsenceOperationOutcome.Success => PortalAbsenceResult.Success(result.Absence!),
        AbsenceOperationOutcome.NotFound or AbsenceOperationOutcome.OwnerNotFound => PortalAbsenceResult.NotFound,
        AbsenceOperationOutcome.Overlap => PortalAbsenceResult.Invalid(
            result.Error ?? "De aanvraag overlapt met een bestaande afwezigheid."),
        AbsenceOperationOutcome.InvalidState => PortalAbsenceResult.InvalidState(result.Error ?? "Ongeldige status."),
        _ => PortalAbsenceResult.Invalid(result.Error ?? "De aanvraag is ongeldig."),
    };
}
