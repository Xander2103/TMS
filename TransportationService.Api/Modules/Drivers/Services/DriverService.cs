using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Persistence;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Drivers.Services;

public class DriverService : IDriverService
{
    private const string EntityType = "Driver";
    private const int DefaultExpiryWarningDays = 30;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;

    public DriverService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IAuditService auditService,
        IQualificationStatusCalculator statusCalculator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider;
    }

    private IQueryable<Driver> TenantScoped() =>
        _dbContext.Set<Driver>().Where(d => d.TenantId == _tenantContext.TenantId);

    public async Task<PagedResult<DriverListItemDto>> SearchAsync(
        string? search, bool? isActive, bool? isBlocked, Guid? categoryId,
        DriverAvailabilityStatus? availabilityStatus, Guid? qualificationTypeId,
        int? expiringWithinDays, bool hasExpired, bool eligibleOnly,
        PageRequest page, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var query = TenantScoped().AsNoTracking();

        if (isActive is { } active) query = query.Where(d => d.IsActive == active);
        if (isBlocked is { } blocked) query = query.Where(d => d.IsBlocked == blocked);
        if (categoryId is { } category) query = query.Where(d => d.DriverCategoryId == category);
        if (availabilityStatus is { } availability) query = query.Where(d => d.AvailabilityStatus == availability);

        if (qualificationTypeId is { } qualType)
        {
            query = query.Where(d => _dbContext.EmployeeQualifications.Any(q =>
                q.TenantId == tenantId && q.EmployeeId == d.EmployeeId && q.QualificationTypeId == qualType));
        }

        if (expiringWithinDays is { } withinDays)
        {
            var limit = today.AddDays(Math.Clamp(withinDays, 1, 365));
            query = query.Where(d => _dbContext.EmployeeQualifications.Any(q =>
                q.TenantId == tenantId && q.EmployeeId == d.EmployeeId
                && q.ExpiryDate != null && q.ExpiryDate >= today && q.ExpiryDate <= limit));
        }

        if (hasExpired)
        {
            query = query.Where(d => _dbContext.EmployeeQualifications.Any(q =>
                q.TenantId == tenantId && q.EmployeeId == d.EmployeeId && q.ExpiryDate != null && q.ExpiryDate < today));
        }

        if (eligibleOnly)
        {
            // Inzetbaar: administratively active, not blocked, and no expired qualification.
            query = query.Where(d => d.IsActive && !d.IsBlocked
                && !_dbContext.EmployeeQualifications.Any(q =>
                    q.TenantId == tenantId && q.EmployeeId == d.EmployeeId && q.ExpiryDate != null && q.ExpiryDate < today));
        }

        // Join employee (for name/number and search) and category name — both tenant-scoped.
        var joined = from d in query
                     join e in _dbContext.Employees.AsNoTracking().Where(e => e.TenantId == _tenantContext.TenantId)
                         on d.EmployeeId equals e.Id
                     join c in _dbContext.Set<DriverCategory>().AsNoTracking().Where(c => c.TenantId == _tenantContext.TenantId)
                         on d.DriverCategoryId equals c.Id into cats
                     from c in cats.DefaultIfEmpty()
                     select new { d, e, CategoryName = c != null ? c.Name : null };

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Case-insensitive on both PostgreSQL and SQLite (plain LIKE is case-sensitive on PostgreSQL).
            var term = search.Trim().ToLowerInvariant();
            joined = joined.Where(x =>
                x.d.DriverNumber.ToLower().Contains(term) ||
                x.e.FirstName.ToLower().Contains(term) ||
                x.e.LastName.ToLower().Contains(term) ||
                x.e.EmployeeNumber.ToLower().Contains(term));
        }

        var ordered = joined.OrderBy(x => x.e.LastName).ThenBy(x => x.e.FirstName);

        // Effective availability: an approved absence covering today overrides the stored status.
        return await ordered.ToPagedResultAsync(
            page,
            x => new DriverListItemDto(
                x.d.Id, x.d.DriverNumber, x.e.FirstName + " " + x.e.LastName, x.e.EmployeeNumber,
                x.CategoryName,
                _dbContext.Absences.Any(a =>
                    a.TenantId == x.d.TenantId && a.EmployeeId == x.d.EmployeeId
                    && a.Status == AbsenceStatus.Approved
                    && a.StartDate <= today && a.EndDate >= today)
                    ? DriverAvailabilityStatus.OnLeave
                    : x.d.AvailabilityStatus,
                x.d.IsActive, x.d.IsBlocked),
            cancellationToken);
    }

    public async Task<DriverDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var driver = await TenantScoped().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return driver is null ? null : await MapToDetailAsync(driver, cancellationToken);
    }

    public async Task<DriverOperationResult> CreateAsync(CreateDriverRequest request, CancellationToken cancellationToken)
    {
        var employeeExists = await _dbContext.Employees
            .AnyAsync(e => e.Id == request.EmployeeId && e.TenantId == _tenantContext.TenantId, cancellationToken);
        if (!employeeExists)
        {
            return DriverOperationResult.EmployeeNotFound;
        }

        var alreadyDriver = await TenantScoped().AnyAsync(d => d.EmployeeId == request.EmployeeId, cancellationToken);
        if (alreadyDriver)
        {
            return DriverOperationResult.EmployeeAlreadyDriver;
        }

        if (!await ReferencesInTenantAsync(request.DriverCategoryId, request.FixedTrailerId, cancellationToken))
        {
            return DriverOperationResult.InvalidReference;
        }

        var settings = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = request.EmployeeId,
            DriverCategoryId = request.DriverCategoryId,
            AvailabilityStatus = request.AvailabilityStatus,
            FixedTrailerId = request.FixedTrailerId,
            Notes = Trim(request.Notes),
            IsActive = true,
        };

        _dbContext.Add(driver);
        try
        {
            await TenantNumbering.SaveWithClaimedNumberAsync(
                _dbContext, settings,
                () => driver.DriverNumber = GenerateDriverNumber(settings),
                cancellationToken);
        }
        catch (DbUpdateException e) when (e is not DbUpdateConcurrencyException)
        {
            // The (TenantId, EmployeeId) unique index caught a race the pre-check missed.
            return DriverOperationResult.EmployeeAlreadyDriver;
        }

        await _auditService.RecordAsync(EntityType, driver.Id.ToString(), "Created", null,
            new { driver.DriverNumber, driver.EmployeeId }, cancellationToken);

        return DriverOperationResult.Success(await MapToDetailAsync(driver, cancellationToken));
    }

    public async Task<DriverOperationResult> UpdateAsync(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken)
    {
        var driver = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (driver is null)
        {
            return DriverOperationResult.NotFound;
        }

        if (!await ReferencesInTenantAsync(request.DriverCategoryId, request.FixedTrailerId, cancellationToken))
        {
            return DriverOperationResult.InvalidReference;
        }

        var oldValues = new { driver.DriverCategoryId, driver.AvailabilityStatus, driver.IsActive };

        driver.DriverCategoryId = request.DriverCategoryId;
        driver.AvailabilityStatus = request.AvailabilityStatus;
        driver.IsActive = request.IsActive;
        driver.FixedTrailerId = request.FixedTrailerId;
        driver.Notes = Trim(request.Notes);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, driver.Id.ToString(), "Updated", oldValues,
            new { driver.DriverCategoryId, driver.AvailabilityStatus, driver.IsActive }, cancellationToken);

        return DriverOperationResult.Success(await MapToDetailAsync(driver, cancellationToken));
    }

    public async Task<DriverOperationResult> SetBlockedAsync(Guid id, SetDriverBlockedRequest request, CancellationToken cancellationToken)
    {
        var driver = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (driver is null)
        {
            return DriverOperationResult.NotFound;
        }

        driver.IsBlocked = request.IsBlocked;
        driver.BlockReason = request.IsBlocked ? Trim(request.Reason) : null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, driver.Id.ToString(),
            request.IsBlocked ? "Blocked" : "Unblocked", null,
            new { driver.IsBlocked, driver.BlockReason }, cancellationToken);

        return DriverOperationResult.Success(await MapToDetailAsync(driver, cancellationToken));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var driver = await TenantScoped().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (driver is null)
        {
            return false;
        }

        _dbContext.Remove(driver); // soft delete via interceptor
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync(EntityType, driver.Id.ToString(), "Deleted",
            new { driver.DriverNumber, driver.EmployeeId }, null, cancellationToken);

        return true;
    }

    /// <summary>All optional references on a driver must resolve within the current tenant.</summary>
    private async Task<bool> ReferencesInTenantAsync(
        Guid? categoryId, Guid? fixedTrailerId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;

        if (categoryId is { } cat && !await _dbContext.DriverCategories
                .AnyAsync(c => c.Id == cat && c.TenantId == tenantId, cancellationToken))
        {
            return false;
        }

        if (fixedTrailerId is { } t && !await _dbContext.Trailers
                .AnyAsync(x => x.Id == t && x.TenantId == tenantId, cancellationToken))
        {
            return false;
        }

        return true;
    }

    private async Task<DriverDetailDto> MapToDetailAsync(Driver driver, CancellationToken cancellationToken)
    {
        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == driver.EmployeeId && e.TenantId == _tenantContext.TenantId, cancellationToken);

        var categoryName = driver.DriverCategoryId is { } catId
            ? await _dbContext.Set<DriverCategory>().AsNoTracking()
                .Where(c => c.Id == catId && c.TenantId == _tenantContext.TenantId)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;

        var qualifications = await LoadQualificationsAsync(driver.EmployeeId, cancellationToken);
        var absenceToday = await CurrentAbsenceTypeAsync(driver.EmployeeId, cancellationToken);
        var readiness = BuildReadiness(driver, qualifications, absenceToday);

        // Vehicle assignment is stored on the Vehicle side; resolve both slots from there.
        var fixedVehicle = await _dbContext.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && v.FixedDriverId == driver.Id)
            .Select(v => new AssignedAssetRef(v.Id, v.InternalNumber + " · " + v.LicensePlate))
            .FirstOrDefaultAsync(cancellationToken);
        var currentVehicle = await _dbContext.Vehicles.AsNoTracking()
            .Where(v => v.TenantId == _tenantContext.TenantId && v.CurrentDriverId == driver.Id)
            .Select(v => new AssignedAssetRef(v.Id, v.InternalNumber + " · " + v.LicensePlate))
            .FirstOrDefaultAsync(cancellationToken);
        var fixedTrailerLabel = driver.FixedTrailerId is { } trailerId
            ? await _dbContext.Trailers.AsNoTracking()
                .Where(t => t.Id == trailerId && t.TenantId == _tenantContext.TenantId)
                .Select(t => t.InternalNumber + " · " + t.LicensePlate)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new DriverDetailDto(
            driver.Id, driver.DriverNumber, driver.EmployeeId,
            employee is null ? string.Empty : $"{employee.FirstName} {employee.LastName}",
            employee?.EmployeeNumber ?? string.Empty,
            driver.DriverCategoryId, categoryName,
            absenceToday is not null ? DriverAvailabilityStatus.OnLeave : driver.AvailabilityStatus,
            driver.IsActive, driver.IsBlocked, driver.BlockReason,
            fixedVehicle, currentVehicle,
            driver.FixedTrailerId, fixedTrailerLabel,
            driver.Notes, readiness, qualifications);
    }

    private async Task<List<DriverQualificationDto>> LoadQualificationsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var warningDays = await _dbContext.TenantSettings.AsNoTracking()
            .Where(s => s.TenantId == _tenantContext.TenantId)
            .Select(s => (int?)s.QualificationExpiryWarningDays)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultExpiryWarningDays;

        var rows = await _dbContext.EmployeeQualifications.AsNoTracking()
            .Where(q => q.TenantId == _tenantContext.TenantId && q.EmployeeId == employeeId)
            .Join(_dbContext.QualificationTypes.AsNoTracking(),
                q => q.QualificationTypeId, t => t.Id,
                (q, t) => new { Qualification = q, t.Code, t.Name })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new DriverQualificationDto(
                x.Code, x.Name,
                _statusCalculator.CalculateEffectiveStatus(x.Qualification, today, warningDays).ToString(),
                x.Qualification.ExpiryDate))
            .OrderBy(q => q.TypeName)
            .ToList();
    }

    /// <summary>An approved absence covering today, if any (drives readiness and effective availability).</summary>
    private async Task<AbsenceType?> CurrentAbsenceTypeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        return await _dbContext.Absences.AsNoTracking()
            .Where(a => a.TenantId == _tenantContext.TenantId && a.EmployeeId == employeeId
                        && a.Status == AbsenceStatus.Approved
                        && a.StartDate <= today && a.EndDate >= today)
            .Select(a => (AbsenceType?)a.Type)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static readonly IReadOnlyDictionary<AbsenceType, string> AbsenceTypeLabels =
        new Dictionary<AbsenceType, string>
        {
            [AbsenceType.Vacation] = "verlof",
            [AbsenceType.Sick] = "ziekte",
            [AbsenceType.Training] = "opleiding",
            [AbsenceType.PersonalLeave] = "persoonlijk verlof",
            [AbsenceType.Unpaid] = "onbetaald verlof",
            [AbsenceType.Other] = "afwezigheid",
        };

    /// <summary>
    /// Driver readiness summary computed on the server (never in the browser). Blocked overrides
    /// everything; otherwise expired qualifications make a driver not-ready and expiring ones warn.
    /// An approved absence covering today also makes the driver not-ready.
    /// </summary>
    private static DriverReadinessDto BuildReadiness(
        Driver driver, IReadOnlyList<DriverQualificationDto> qualifications, AbsenceType? absenceToday)
    {
        var blocking = new List<string>();
        var warnings = new List<string>();

        if (driver.IsBlocked)
        {
            blocking.Add(driver.BlockReason is { Length: > 0 } r ? $"Geblokkeerd: {r}" : "Chauffeur is geblokkeerd.");
        }

        if (!driver.IsActive)
        {
            blocking.Add("Chauffeur is inactief.");
        }

        if (absenceToday is { } absence)
        {
            blocking.Add($"Vandaag afwezig ({AbsenceTypeLabels[absence]}).");
        }

        foreach (var q in qualifications)
        {
            switch (q.Status)
            {
                case nameof(QualificationStatus.Expired):
                    blocking.Add($"{q.TypeName} is verlopen.");
                    break;
                case nameof(QualificationStatus.Suspended):
                case nameof(QualificationStatus.Rejected):
                    blocking.Add($"{q.TypeName} is niet geldig ({q.Status}).");
                    break;
                case nameof(QualificationStatus.ExpiringSoon):
                    warnings.Add($"{q.TypeName} verloopt binnenkort.");
                    break;
                case nameof(QualificationStatus.Pending):
                    warnings.Add($"{q.TypeName} is nog niet bevestigd.");
                    break;
            }
        }

        var status = blocking.Count > 0
            ? (driver.IsBlocked ? "Blocked" : "NotReady")
            : warnings.Count > 0 ? "Warning" : "Ready";

        return new DriverReadinessDto(status, blocking, warnings);
    }

    private static string GenerateDriverNumber(TenantSettings? settings)
    {
        if (settings is null)
        {
            return $"CH-{Guid.NewGuid().ToString("N")[..8]}";
        }

        var prefix = settings.DriverNumberPrefix ?? "CH-";
        var number = $"{prefix}{settings.DriverNumberNextValue:D4}";
        settings.DriverNumberNextValue++;
        return number;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
