using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Eligibility.Models;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Eligibility.Services;

public class DriverEligibilityService : IDriverEligibilityService
{
    private const int DefaultExpiryWarningDays = 30;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IQualificationStatusCalculator _statusCalculator;
    private readonly TimeProvider _timeProvider;
    private readonly DriverEligibilityEvaluator _evaluator = new();

    public DriverEligibilityService(
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

    public async Task<EligibilityResult> CheckEligibilityAsync(Guid employeeId, DriverEligibilityRequest request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var settings = await _dbContext.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == _tenantContext.TenantId, cancellationToken);
        var warningDays = settings?.QualificationExpiryWarningDays ?? DefaultExpiryWarningDays;

        var qualifications = await _dbContext.EmployeeQualifications
            .AsNoTracking()
            .Where(q => q.TenantId == _tenantContext.TenantId && q.EmployeeId == employeeId)
            .Join(_dbContext.QualificationTypes.AsNoTracking(), q => q.QualificationTypeId, t => t.Id, (q, t) => new { Qualification = q, TypeCode = t.Code })
            .ToListAsync(cancellationToken);

        var snapshots = qualifications
            .Select(x => new QualificationSnapshot(
                x.TypeCode,
                _statusCalculator.CalculateEffectiveStatus(x.Qualification, today, warningDays),
                x.Qualification.ExpiryDate))
            .ToList();

        return _evaluator.Evaluate(snapshots, request);
    }
}
