using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

public class RoleService : IRoleService
{
    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAccountSecurityService _accountSecurity;

    public RoleService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        ICurrentUserContext currentUser, IAccountSecurityService accountSecurity)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _currentUser = currentUser;
        _accountSecurity = accountSecurity;
    }

    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await _dbContext.Roles
            .AsNoTracking()
            .Where(r => r.TenantId == _tenantContext.TenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return await MapManyAsync(roles, cancellationToken);
    }

    public async Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == _tenantContext.TenantId, cancellationToken);

        return role is null ? null : await MapAsync(role, cancellationToken);
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsSystemRole = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("Role", role.Id.ToString(), "Created", null,
            new { role.Name, role.Description, role.IsActive }, cancellationToken);

        return (await MapAsync(role, cancellationToken))!;
    }

    public async Task<RoleOperationResult> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == _tenantContext.TenantId, cancellationToken);
        if (role is null) return new RoleOperationResult(RoleOperationOutcome.NotFound, null);

        var newName = request.Name.Trim();
        if (role.IsSystemRole && newName != role.Name)
        {
            return new RoleOperationResult(RoleOperationOutcome.SystemRoleProtected, await MapAsync(role, cancellationToken));
        }

        var oldValues = new { role.Name, role.Description, role.IsActive };
        role.Name = newName;
        role.Description = request.Description?.Trim();
        role.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("Role", role.Id.ToString(), "Updated", oldValues,
            new { role.Name, role.Description, role.IsActive }, cancellationToken);

        return new RoleOperationResult(RoleOperationOutcome.Success, await MapAsync(role, cancellationToken));
    }

    public async Task<RoleOperationResult> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == _tenantContext.TenantId, cancellationToken);
        if (role is null) return new RoleOperationResult(RoleOperationOutcome.NotFound, null);

        if (role.IsSystemRole)
        {
            return new RoleOperationResult(RoleOperationOutcome.SystemRoleProtected, await MapAsync(role, cancellationToken));
        }

        role.IsActive = false;
        role.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("Role", role.Id.ToString(), "Deactivated", new { IsActive = true }, new { role.IsActive }, cancellationToken);

        return new RoleOperationResult(RoleOperationOutcome.Success, await MapAsync(role, cancellationToken));
    }

    public async Task<RoleOperationResult> AssignPermissionsAsync(Guid id, AssignPermissionsRequest request, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == _tenantContext.TenantId, cancellationToken);
        if (role is null) return new RoleOperationResult(RoleOperationOutcome.NotFound, null);

        // System roles (Administrator etc.) are immutable through the ordinary management flow —
        // this prevents both hollowing out Administrator (tenant-wide lockout) and re-seeding it.
        if (role.IsSystemRole)
        {
            return new RoleOperationResult(RoleOperationOutcome.SystemRoleProtected, await MapAsync(role, cancellationToken));
        }

        if (_currentUser.CurrentUserId is null)
        {
            return new RoleOperationResult(RoleOperationOutcome.Forbidden, null, "Authenticatie is vereist voor deze actie.");
        }

        var requestedCodes = request.PermissionCodes
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal).ToList();

        var knownPermissions = await _dbContext.Permissions
            .Where(p => requestedCodes.Contains(p.Code))
            .ToListAsync(cancellationToken);

        // Unknown codes (including wrong-casing variants of internal permissions) are rejected —
        // never silently dropped — so a caller can't smuggle intent past validation.
        if (knownPermissions.Count != requestedCodes.Count)
        {
            return new RoleOperationResult(RoleOperationOutcome.ValidationFailed, await MapAsync(role, cancellationToken),
                RoleOperationMessages.UnknownPermission);
        }

        // Customer-portal roles may ONLY carry customer_portal.* permissions — an internal
        // permission on a portal role would hand every portal user of every customer that right.
        if (_accountSecurity.IsPortalTemplateRole(role) && !_accountSecurity.IsPortalPermissionSet(requestedCodes))
        {
            return new RoleOperationResult(RoleOperationOutcome.Forbidden, await MapAsync(role, cancellationToken),
                RoleOperationMessages.PortalRoleInternalPermission);
        }

        // The caller may only grant permissions they themselves effectively hold — no indirect
        // creation of a higher privilege set via a role.
        if (!await _accountSecurity.ActorHoldsAllAsync(requestedCodes, cancellationToken))
        {
            return new RoleOperationResult(RoleOperationOutcome.Forbidden, await MapAsync(role, cancellationToken),
                RoleOperationMessages.PrivilegeEscalation);
        }

        var permissionIds = knownPermissions.Select(p => p.Id).ToList();

        var existing = await _dbContext.RolePermissions.Where(rp => rp.RoleId == id).ToListAsync(cancellationToken);
        var oldPermissionIds = existing.Select(rp => rp.PermissionId).ToList();
        _dbContext.RolePermissions.RemoveRange(existing);
        foreach (var permissionId in permissionIds)
        {
            _dbContext.RolePermissions.Add(new RolePermission { RoleId = id, PermissionId = permissionId });
        }

        role.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("Role", role.Id.ToString(), "AssignPermissions", new { PermissionIds = oldPermissionIds }, new { PermissionIds = permissionIds }, cancellationToken);

        return new RoleOperationResult(RoleOperationOutcome.Success, await MapAsync(role, cancellationToken));
    }

    public async Task<IReadOnlyList<PermissionDto>> ListPermissionsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Module).ThenBy(p => p.Action)
            .Select(p => new PermissionDto(p.Id, p.Code, p.Module, p.Action, p.Description))
            .ToListAsync(cancellationToken);
    }

    private async Task<RoleDto> MapAsync(Role role, CancellationToken cancellationToken) => (await MapManyAsync([role], cancellationToken))[0];

    private async Task<IReadOnlyList<RoleDto>> MapManyAsync(IReadOnlyList<Role> roles, CancellationToken cancellationToken)
    {
        var roleIds = roles.Select(r => r.Id).ToList();

        var permissionCodeRows = await _dbContext.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(_dbContext.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { rp.RoleId, p.Code })
            .ToListAsync(cancellationToken);

        return roles
            .Select(r => new RoleDto(
                r.Id, r.Name, r.Description, r.IsSystemRole, r.IsActive,
                permissionCodeRows.Where(x => x.RoleId == r.Id).Select(x => x.Code).ToList()))
            .ToList();
    }
}
