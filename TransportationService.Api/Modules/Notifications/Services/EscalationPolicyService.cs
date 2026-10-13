using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Notifications.Services;

public record EscalationPolicyDto(Guid Id, EscalationKind Kind, int DelayHours, string TargetPermissionCode, bool IsActive);

public record SaveEscalationPolicyRequest(int DelayHours, string TargetPermissionCode, bool IsActive);

public interface IEscalationPolicyService
{
    /// <summary>All kinds, materialising missing rows with their defaults (idempotent).</summary>
    Task<IReadOnlyList<EscalationPolicyDto>> ListAsync(CancellationToken cancellationToken);

    Task<EscalationPolicyDto?> UpdateAsync(EscalationKind kind, SaveEscalationPolicyRequest request, CancellationToken cancellationToken);
}

public class EscalationPolicyService : IEscalationPolicyService
{
    /// <summary>Safe defaults: only the two clearest conditions start active.</summary>
    public static readonly IReadOnlyDictionary<EscalationKind, (int DelayHours, string Target, bool Active)> Defaults =
        new Dictionary<EscalationKind, (int, string, bool)>
        {
            [EscalationKind.NegativeStockUnresolved] = (24, PermissionCodes.InventoryManageThresholds, true),
            [EscalationKind.CriticalStockUnhandled] = (48, PermissionCodes.InventoryManageThresholds, false),
            [EscalationKind.TaskOverdue] = (48, PermissionCodes.TasksViewAll, true),
            [EscalationKind.AcknowledgementMissing] = (72, PermissionCodes.MessagesViewDeliveryStatus, false),
            [EscalationKind.ReturnOverdue] = (72, PermissionCodes.IssuedItemsManage, false),
        };

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;

    public EscalationPolicyService(TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
    }

    public async Task<IReadOnlyList<EscalationPolicyDto>> ListAsync(CancellationToken cancellationToken)
    {
        var policies = await EnsureRowsAsync(cancellationToken);
        return policies
            .OrderBy(p => p.Kind)
            .Select(Map)
            .ToList();
    }

    public async Task<EscalationPolicyDto?> UpdateAsync(
        EscalationKind kind, SaveEscalationPolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.DelayHours is < 1 or > 24 * 30)
        {
            throw new DomainValidationException("delayHours", "De escalatietermijn moet tussen 1 uur en 30 dagen liggen.");
        }

        if (!PermissionCodes.All.Any(p => p.Code == request.TargetPermissionCode))
        {
            throw new DomainValidationException("targetPermissionCode", "Onbekende permissie als escalatiedoel.");
        }

        var policies = await EnsureRowsAsync(cancellationToken);
        var policy = policies.FirstOrDefault(p => p.Kind == kind);
        if (policy is null)
        {
            return null;
        }

        var old = new { policy.DelayHours, policy.TargetPermissionCode, policy.IsActive };
        policy.DelayHours = request.DelayHours;
        policy.TargetPermissionCode = request.TargetPermissionCode;
        policy.IsActive = request.IsActive;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _auditService.RecordAsync("EscalationPolicy", policy.Id.ToString(), "Updated", old,
            new { policy.Kind, policy.DelayHours, policy.TargetPermissionCode, policy.IsActive }, cancellationToken);
        return Map(policy);
    }

    private async Task<List<EscalationPolicy>> EnsureRowsAsync(CancellationToken cancellationToken)
    {
        var policies = await _dbContext.Set<EscalationPolicy>()
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .ToListAsync(cancellationToken);
        var missing = Enum.GetValues<EscalationKind>().Where(kind => policies.All(p => p.Kind != kind)).ToList();
        foreach (var kind in missing)
        {
            var (delay, target, active) = Defaults[kind];
            var policy = new EscalationPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId,
                Kind = kind,
                DelayHours = delay,
                TargetPermissionCode = target,
                IsActive = active,
            };
            policies.Add(policy);
            _dbContext.Add(policy);
        }

        if (missing.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return policies;
    }

    private static EscalationPolicyDto Map(EscalationPolicy p) =>
        new(p.Id, p.Kind, p.DelayHours, p.TargetPermissionCode, p.IsActive);
}
