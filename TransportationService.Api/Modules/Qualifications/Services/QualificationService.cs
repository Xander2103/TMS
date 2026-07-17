using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
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

    public QualificationService(
        TransportationDbContext dbContext,
        ITenantContext tenantContext,
        IQualificationStatusCalculator statusCalculator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _statusCalculator = statusCalculator;
        _timeProvider = timeProvider;
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
            Status = QualificationStatus.Pending,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.EmployeeQualifications.Add(qualification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (await MapAsync(qualification, cancellationToken))!;
    }

    public async Task<EmployeeQualificationDto?> UpdateAsync(Guid id, UpdateEmployeeQualificationRequest request, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications.FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);
        if (qualification is null) return null;

        qualification.DocumentNumber = request.DocumentNumber;
        qualification.ObtainedDate = request.ObtainedDate;
        qualification.ExpiryDate = request.ExpiryDate;
        qualification.Notes = request.Notes;
        qualification.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    public async Task<EmployeeQualificationDto?> VerifyAsync(Guid id, Guid verifyingUserId, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications.FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);
        if (qualification is null) return null;

        qualification.Status = QualificationStatus.Valid;
        qualification.VerifiedAt = DateTime.UtcNow;
        qualification.VerifiedByUserId = verifyingUserId;
        qualification.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    public async Task<EmployeeQualificationDto?> SuspendAsync(Guid id, CancellationToken cancellationToken)
    {
        var qualification = await _dbContext.EmployeeQualifications.FirstOrDefaultAsync(q => q.Id == id && q.TenantId == _tenantContext.TenantId, cancellationToken);
        if (qualification is null) return null;

        qualification.Status = QualificationStatus.Suspended;
        qualification.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await MapAsync(qualification, cancellationToken);
    }

    public async Task<IReadOnlyList<EmployeeQualificationDto>> ListExpiringWithinDaysAsync(int days, CancellationToken cancellationToken)
    {
        var today = Today();
        var warningDays = await GetExpiryWarningDaysAsync(cancellationToken);

        var qualifications = await _dbContext.EmployeeQualifications
            .AsNoTracking()
            .Where(q => q.TenantId == _tenantContext.TenantId)
            .ToListAsync(cancellationToken);

        var mapped = await MapManyAsync(qualifications, cancellationToken);

        return mapped
            .Where(q => q.EffectiveStatus == QualificationStatus.ExpiringSoon && q.ExpiryDate is { } expiry && expiry <= today.AddDays(days))
            .ToList();
    }

    public async Task<IReadOnlyList<EmployeeQualificationDto>> ListExpiredAsync(CancellationToken cancellationToken)
    {
        var qualifications = await _dbContext.EmployeeQualifications
            .AsNoTracking()
            .Where(q => q.TenantId == _tenantContext.TenantId)
            .ToListAsync(cancellationToken);

        var mapped = await MapManyAsync(qualifications, cancellationToken);

        return mapped.Where(q => q.EffectiveStatus == QualificationStatus.Expired).ToList();
    }

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
                    q.DocumentNumber, q.ObtainedDate, q.ExpiryDate, q.Status, effectiveStatus,
                    q.DocumentPath, q.Notes, q.VerifiedAt, q.VerifiedByUserId);
            })
            .ToList();
    }
}
