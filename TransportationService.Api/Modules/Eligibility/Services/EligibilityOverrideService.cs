using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Eligibility.Dtos;
using TransportationService.Api.Modules.Eligibility.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Eligibility.Services;

public class EligibilityOverrideService : IEligibilityOverrideService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public EligibilityOverrideService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<EligibilityOverrideDto> CreateAsync(Guid approvingUserId, CreateEligibilityOverrideRequest request, CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(request));
        }

        var overrideEntity = new EligibilityOverride
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = request.EmployeeId,
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
            RuleCode = request.RuleCode,
            Reason = reason,
            ApprovedByUserId = approvingUserId,
            ApprovedAt = DateTime.UtcNow,
            ValidUntil = request.ValidUntil,
            CreatedAt = DateTime.UtcNow,
        };

        _dbContext.EligibilityOverrides.Add(overrideEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("EligibilityOverride", overrideEntity.Id.ToString(), "Created", null,
            new { overrideEntity.EmployeeId, overrideEntity.RuleCode, overrideEntity.Reason, overrideEntity.ApprovedByUserId }, cancellationToken);

        return Map(overrideEntity);
    }

    public async Task<IReadOnlyList<EligibilityOverrideDto>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var overrides = await _dbContext.EligibilityOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == _tenantContext.TenantId && o.EmployeeId == employeeId)
            .OrderByDescending(o => o.ApprovedAt)
            .ToListAsync(cancellationToken);

        return overrides.Select(Map).ToList();
    }

    private static EligibilityOverrideDto Map(EligibilityOverride o) => new(
        o.Id, o.EmployeeId, o.RelatedEntityType, o.RelatedEntityId, o.RuleCode, o.Reason, o.ApprovedByUserId, o.ApprovedAt, o.ValidUntil);
}
