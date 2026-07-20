using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Qualifications.Services;

public class QualificationService : IQualificationService
{
    private const int DefaultExpiryWarningDays = 30;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditService _auditService;
    private readonly ICountryCodeValidator _countryValidator;
    private readonly IFileStorageService _fileStorage;

    public QualificationService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IQualificationStatusCalculator statusCalculator,
        TimeProvider timeProvider,
        IAuditService auditService,
        ICountryCodeValidator countryValidator,
        IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider;
        _auditService = auditService;
        _countryValidator = countryValidator;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<EmployeeQualificationDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var qualifications = await _dbContext.EmployeeQualifications
            .AsNoTracking()
            .Where(q => q.TenantId == _tenantContext.TenantId && q.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);

        return await MapManyAsync(qualifications, cancellationToken);
    }

    public async Task<EmployeeQualificationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);

        return qualification is null ? null : await MapAsync(qualification, cancellationToken);
    }

    public async Task<EmployeeQualificationDto> CreateAsync(Guid employeeId, CreateEmployeeQualificationRequest request, CancellationToken cancellationToken)
    {
        var qualification = new EmployeeQualification
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            QualificationTypeId = request.QualificationTypeId,
            DocumentNumber = request.DocumentNumber,
            ObtainedDate = request.ObtainedDate,
            ExpiryDate = request.ExpiryDate,
            IssuingCountryCode = await _countryValidator.NormalizeAndValidateAsync(request.IssuingCountryCode, "uitgifteland", cancellationToken, "issuingCountryCode"),
            Status = QualificationStatus.Pending,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.EmployeeQualifications.Add(qualification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("EmployeeQualification", qualification.Id.ToString(), "Created", null,
            new { qualification.EmployeeId, qualification.QualificationTypeId, qualification.ObtainedDate, qualification.ExpiryDate, qualification.Status }, cancellationToken);

        return (await MapAsync(qualification, cancellationToken))!;
    }

    public async Task<EmployeeQualificationDto?> UpdateAsync(Guid id, UpdateEmployeeQualificationRequest request, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications.FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);
        if (qualification is null) return null;

        qualification.DocumentNumber = request.DocumentNumber;
        qualification.ObtainedDate = request.ObtainedDate;
        qualification.ExpiryDate = request.ExpiryDate;
        qualification.IssuingCountryCode = await _countryValidator.NormalizeAndValidateAsync(request.IssuingCountryCode, "uitgifteland", cancellationToken, "issuingCountryCode");
        qualification.Notes = request.Notes;
        qualification.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    public async Task<EmployeeQualificationDto?> VerifyAsync(Guid id, Guid verifyingUserId, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications.FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);
        if (qualification is null) return null;

        var oldStatus = qualification.Status;
        qualification.Status = QualificationStatus.Valid;
        qualification.VerifiedAt = DateTime.UtcNow;
        qualification.VerifiedByUserId = verifyingUserId;
        qualification.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("EmployeeQualification", qualification.Id.ToString(), "Verified",
            new { Status = oldStatus }, new { qualification.Status, qualification.VerifiedAt, qualification.VerifiedByUserId }, cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    public async Task<EmployeeQualificationDto?> SuspendAsync(Guid id, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications.FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);
        if (qualification is null) return null;

        var oldStatus = qualification.Status;
        qualification.Status = QualificationStatus.Suspended;
        qualification.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("EmployeeQualification", qualification.Id.ToString(), "Suspended",
            new { Status = oldStatus }, new { qualification.Status }, cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    // The overview is an expiry RADAR: it goes by the expiry date itself (also for Pending
    // items and for windows wider than the tenant's warning window), and only leaves out
    // qualifications that are administratively inactive (suspended/rejected).
    private static bool CountsForExpiryRadar(EmployeeQualificationDto q) =>
        q.StoredStatus is not (QualificationStatus.Suspended or QualificationStatus.Rejected);

    public async Task<IReadOnlyList<ExpiringQualificationDto>> ListExpiringWithinDaysAsync(int days, CancellationToken cancellationToken)
    {
        var today = Today();
        var limit = today.AddDays(days);
        return await ListOverviewAsync(
            q => CountsForExpiryRadar(q) && q.ExpiryDate is { } expiry && expiry >= today && expiry <= limit,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ExpiringQualificationDto>> ListExpiredAsync(CancellationToken cancellationToken)
    {
        var today = Today();
        return await ListOverviewAsync(
            q => CountsForExpiryRadar(q) && q.ExpiryDate is { } expiry && expiry < today,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ExpiringQualificationDto>> ListOverviewAsync(
        Func<EmployeeQualificationDto, bool> predicate, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var qualifications = await _dbContext.EmployeeQualifications
            .AsNoTracking()
            .Where(q => q.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var mapped = (await MapManyAsync(qualifications, cancellationToken)).Where(predicate).ToList();
        if (mapped.Count == 0)
        {
            return [];
        }

        var employeeIds = mapped.Select(q => q.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                Name = e.FirstName + " " + e.LastName,
                e.EmployeeNumber,
                IsDriver = _dbContext.Drivers.Any(d => d.EmployeeId == e.Id && d.TenantId == tenantId),
            })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        return mapped
            .OrderBy(q => q.ExpiryDate)
            .Select(q =>
            {
                var employee = employees.GetValueOrDefault(q.EmployeeId);
                return new ExpiringQualificationDto(
                    q.Id, q.EmployeeId,
                    employee?.Name ?? "Onbekend", employee?.EmployeeNumber ?? "?",
                    q.QualificationTypeCode, q.QualificationTypeName,
                    q.ExpiryDate, q.EffectiveStatus, employee?.IsDriver ?? false);
            })
            .ToList();
    }

    public async Task<EmployeeQualificationDto?> AttachDocumentAsync(
        Guid employeeId, Guid id, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var qualification = await FindScopedAsync(employeeId, id, cancellationToken);
        if (qualification is null)
        {
            return null;
        }

        // Replace: remove the previous document so storage never accumulates orphans.
        if (qualification.DocumentPath is { } existing)
        {
            await _fileStorage.DeleteAsync(existing, cancellationToken);
        }

        qualification.DocumentPath = await _fileStorage.SaveAsync(
            _tenantContext.TenantId, "qualifications", fileName, content, cancellationToken);
        qualification.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("EmployeeQualification", qualification.Id.ToString(), "DocumentUploaded",
            null, new { FileName = fileName }, cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    public async Task<(Stream Content, string FileName)?> OpenDocumentAsync(
        Guid employeeId, Guid id, CancellationToken cancellationToken)
    {
        var qualification = await FindScopedAsync(employeeId, id, cancellationToken);
        if (qualification?.DocumentPath is not { } storageKey)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(storageKey, cancellationToken);
        var fileName = Path.GetFileName(storageKey);
        return (stream, fileName);
    }

    public async Task<bool> RemoveDocumentAsync(Guid employeeId, Guid id, CancellationToken cancellationToken)
    {
        var qualification = await FindScopedAsync(employeeId, id, cancellationToken);
        if (qualification?.DocumentPath is not { } storageKey)
        {
            return false;
        }

        await _fileStorage.DeleteAsync(storageKey, cancellationToken);
        qualification.DocumentPath = null;
        qualification.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("EmployeeQualification", qualification.Id.ToString(), "DocumentRemoved",
            null, null, cancellationToken);

        return true;
    }

    private Task<EmployeeQualification?> FindScopedAsync(Guid employeeId, Guid id, CancellationToken cancellationToken) =>
        _dbContext.EmployeeQualifications.FirstOrDefaultAsync(
            q => q.Id == id && q.EmployeeId == employeeId && q.TenantId == _tenantContext.TenantId, cancellationToken);

    public async Task<IReadOnlyList<QualificationTypeDto>> ListQualificationTypesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.QualificationTypes
            .AsNoTracking()
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .Select(t => new QualificationTypeDto(t.Id, t.Code, t.Name, t.Description, t.Category, t.RequiresExpiryDate, t.IsActive))
            .ToListAsync(cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

    private async Task<int> GetExpiryWarningDaysAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);

        return settings?.QualificationExpiryWarningDays ?? DefaultExpiryWarningDays;
    }

    private async Task<EmployeeQualificationDto?> MapAsync(EmployeeQualification qualification, CancellationToken cancellationToken) =>
        (await MapManyAsync([qualification], cancellationToken)).FirstOrDefault();

    private async Task<IReadOnlyList<EmployeeQualificationDto>> MapManyAsync(IReadOnlyList<EmployeeQualification> qualifications, CancellationToken cancellationToken)
    {
        if (qualifications.Count == 0) return [];

        var today = Today();
        var warningDays = await GetExpiryWarningDaysAsync(cancellationToken);

        var typeIds = qualifications.Select(q => q.QualificationTypeId).Distinct().ToList();
        var types = await _dbContext.QualificationTypes
            .AsNoTracking()
            .Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return qualifications
            .Select(q =>
            {
                var type = types.GetValueOrDefault(q.QualificationTypeId);
                var effectiveStatus = _statusCalculator.CalculateEffectiveStatus(q, today, warningDays);

                return new EmployeeQualificationDto(
                    q.Id, q.EmployeeId, q.QualificationTypeId, type?.Code ?? string.Empty, type?.Name ?? string.Empty,
                    q.DocumentNumber, q.ObtainedDate, q.ExpiryDate, q.IssuingCountryCode, q.Status, effectiveStatus,
                    q.DocumentPath is not null, q.Notes, q.VerifiedAt, q.VerifiedByUserId);
            })
            .ToList();
    }
}
