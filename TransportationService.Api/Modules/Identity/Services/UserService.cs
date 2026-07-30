using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Dtos;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

public class UserService : IUserService
{
    private const string AdministratorRoleName = "Administrator";

    private const int MinPasswordLength = 8;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _auditService;
    private readonly Authentication.Services.IPasswordHasher _passwordHasher;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAccountSecurityService _accountSecurity;

    public UserService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        Authentication.Services.IPasswordHasher passwordHasher,
        ICurrentUserContext currentUser, IAccountSecurityService accountSecurity)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _accountSecurity = accountSecurity;
    }

    /// <summary>
    /// Administrative (re)set of ANOTHER user's password. Gated by the sensitive
    /// users.reset_password permission at the endpoint; here it is additionally fail-closed:
    /// the actor may never reset a higher-privileged or protected system account, and may not
    /// reset their own password via this path (self-service change-password exists for that).
    /// On success all of the target's sessions are revoked (refresh tokens + security stamp),
    /// MustChangePassword is set, and the action is audited without any password/token material.
    /// </summary>
    public async Task<UserOperationResult> SetPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            return new UserOperationResult(UserOperationOutcome.ValidationFailed, null,
                $"Het wachtwoord moet minstens {MinPasswordLength} tekens lang zijn.");
        }

        if (_currentUser.CurrentUserId is not { } actorId)
        {
            return new UserOperationResult(UserOperationOutcome.Forbidden, null,
                "Authenticatie is vereist voor deze actie.");
        }

        if (actorId == id)
        {
            return new UserOperationResult(UserOperationOutcome.Forbidden, null,
                "Je kunt je eigen wachtwoord niet via de administratieve reset wijzigen; gebruik de beveiligde wachtwoord-wijzigen-flow.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null)
        {
            return new UserOperationResult(UserOperationOutcome.NotFound, null);
        }

        if (!await _accountSecurity.CanManageUserAsync(id, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.Forbidden, null,
                "Je kunt het wachtwoord van deze gebruiker niet resetten: het doel heeft rechten die je zelf niet bezit, of is een beschermd systeemaccount.");
        }

        user.PasswordHash = _passwordHasher.Hash(password);
        // An admin-set credential is temporary by definition: the user must pick their own.
        user.MustChangePassword = true;
        // Immediately invalidate the target's outstanding sessions (refresh tokens + access tokens
        // via the rotated security stamp) — a reset performed on a suspected compromise must not
        // leave the attacker's tokens alive.
        await _accountSecurity.RevokeAllSessionsAsync(user, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "PasswordResetByAdmin", null,
            new { ByAdministrator = true, ActorUserId = actorId, TargetUserId = user.Id, SessionsRevoked = true },
            cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.TenantId == _tenantContext.TenantId)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);

        return await MapManyAsync(users, cancellationToken);
    }

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);

        return user is null ? null : await MapAsync(user, cancellationToken);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await EnsureLinksInTenantAsync(request.EmployeeId, request.CustomerId, cancellationToken);
        await EnsureRolesInTenantAsync(request.RoleIds, cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId,
            Email = request.Email.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            EmployeeId = request.EmployeeId,
            CustomerId = request.CustomerId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _dbContext.Users.Add(user);

        foreach (var roleId in request.RoleIds.Distinct())
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "Created", null,
            new { user.Email, user.FirstName, user.LastName, user.IsActive, user.IsBlocked }, cancellationToken);

        return (await MapAsync(user, cancellationToken))!;
    }

    public async Task<UserDto?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return null;

        await EnsureLinksInTenantAsync(request.EmployeeId, request.CustomerId, cancellationToken);

        var oldValues = new { user.Email, user.FirstName, user.LastName, user.IsActive, user.IsBlocked };

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.EmployeeId = request.EmployeeId;
        user.CustomerId = request.CustomerId;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "Updated", oldValues,
            new { user.Email, user.FirstName, user.LastName, user.IsActive, user.IsBlocked }, cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    public async Task<UserOperationResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        if (!isActive && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        var oldValues = new { user.IsActive };
        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "SetActive", oldValues, new { user.IsActive }, cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    public async Task<UserOperationResult> SetBlockedAsync(Guid id, bool isBlocked, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        if (isBlocked && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        var oldValues = new { user.IsBlocked };
        user.IsBlocked = isBlocked;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "SetBlocked", oldValues, new { user.IsBlocked }, cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    public async Task<UserOperationResult> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.CurrentUserId is not { } actorId)
        {
            return new UserOperationResult(UserOperationOutcome.Forbidden, null, "Authenticatie is vereist voor deze actie.");
        }

        // A user may never change their own role membership (self-escalation).
        if (actorId == id)
        {
            return new UserOperationResult(UserOperationOutcome.Forbidden, null, "Je kunt je eigen rollen niet wijzigen.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        var newRoleIds = request.RoleIds.Distinct().ToHashSet();
        await EnsureRolesInTenantAsync(newRoleIds, cancellationToken);

        var existingRoles = await _dbContext.UserRoles.Where(ur => ur.UserId == id).ToListAsync(cancellationToken);
        var oldRoleIds = existingRoles.Select(ur => ur.RoleId).ToHashSet();

        // Replace-semantics: BOTH additions and removals must be authorized. The actor may only
        // touch a role whose permission set is a subset of their own, and may not add/remove a
        // protected system role unless they are themselves a system user. This blocks granting
        // yourself/another user a higher privilege set, and blocks stripping a high-privilege role
        // you are not entitled to manage.
        var affectedRoleIds = newRoleIds.Except(oldRoleIds).Concat(oldRoleIds.Except(newRoleIds)).ToHashSet();
        if (affectedRoleIds.Count > 0)
        {
            var actorPermissions = await _accountSecurity.EffectivePermissionsAsync(actorId, cancellationToken);
            var actorIsSystemUser = await _accountSecurity.IsProtectedSystemUserAsync(actorId, cancellationToken);

            var affectedRoles = await _dbContext.Roles
                .Where(r => affectedRoleIds.Contains(r.Id) && r.TenantId == _tenantContext.TenantId)
                .ToListAsync(cancellationToken);
            var permsByRole = (await _dbContext.RolePermissions
                    .Where(rp => affectedRoleIds.Contains(rp.RoleId))
                    .Join(_dbContext.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { rp.RoleId, p.Code })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.RoleId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Code).ToHashSet());

            foreach (var role in affectedRoles)
            {
                if (role.IsSystemRole && !actorIsSystemUser)
                {
                    return new UserOperationResult(UserOperationOutcome.Forbidden, null,
                        "Je kunt een beschermde systeemrol niet toekennen of verwijderen.");
                }

                var rolePermissions = permsByRole.GetValueOrDefault(role.Id, []);
                if (!rolePermissions.All(actorPermissions.Contains))
                {
                    return new UserOperationResult(UserOperationOutcome.Forbidden, null,
                        "Je kunt geen rol toekennen of verwijderen met rechten die je zelf niet bezit.");
                }
            }
        }

        var wasAdministrator = await IsAdministratorAsync(user.Id, cancellationToken);
        var staysAdministrator = await RoleIdsIncludeAdministratorAsync(newRoleIds, cancellationToken);

        if (wasAdministrator && !staysAdministrator && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        _dbContext.UserRoles.RemoveRange(existingRoles);
        foreach (var roleId in newRoleIds)
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = id, RoleId = roleId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "AssignRoles",
            new { RoleIds = oldRoleIds.ToList() }, new { RoleIds = newRoleIds.ToList(), ActorUserId = actorId }, cancellationToken);

        return new UserOperationResult(UserOperationOutcome.Success, await MapAsync(user, cancellationToken));
    }

    /// <summary>Employee/customer links on a user must resolve within the current tenant.</summary>
    private async Task EnsureLinksInTenantAsync(Guid? employeeId, Guid? customerId, CancellationToken cancellationToken)
    {
        if (employeeId is { } emp && !await _dbContext.Employees
                .AnyAsync(e => e.Id == emp && e.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("medewerker");
        }

        if (customerId is { } cust && !await _dbContext.Customers
                .AnyAsync(c => c.Id == cust && c.TenantId == _tenantContext.TenantId, cancellationToken))
        {
            throw new InvalidTenantReferenceException("klant");
        }
    }

    /// <summary>Only roles belonging to the current tenant may be assigned.</summary>
    private async Task EnsureRolesInTenantAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return;
        }

        var distinct = roleIds.Distinct().ToList();
        var known = await _dbContext.Roles
            .CountAsync(r => distinct.Contains(r.Id) && r.TenantId == _tenantContext.TenantId, cancellationToken);

        if (known != distinct.Count)
        {
            throw new InvalidTenantReferenceException("rol");
        }
    }

    private async Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r)
            .AnyAsync(r => r.Name == AdministratorRoleName && r.IsSystemRole, cancellationToken);
    }

    private async Task<bool> RoleIdsIncludeAdministratorAsync(IReadOnlySet<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0) return false;

        return await _dbContext.Roles
            .Where(r => roleIds.Contains(r.Id) && r.Name == AdministratorRoleName && r.IsSystemRole)
            .AnyAsync(cancellationToken);
    }

    private async Task<bool> IsLastActiveAdministratorAsync(User user, CancellationToken cancellationToken)
    {
        var isCurrentlyAdministrator = await IsAdministratorAsync(user.Id, cancellationToken);
        if (!isCurrentlyAdministrator) return false;

        var otherActiveAdministratorCount = await _dbContext.UserRoles
            .Join(_dbContext.Roles.Where(r => r.Name == AdministratorRoleName && r.IsSystemRole), ur => ur.RoleId, r => r.Id, (ur, r) => ur.UserId)
            .Join(_dbContext.Users.Where(u => u.TenantId == _tenantContext.TenantId && u.IsActive && !u.IsBlocked && u.Id != user.Id), userId => userId, u => u.Id, (userId, u) => u.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        return otherActiveAdministratorCount == 0;
    }

    private async Task<UserDto> MapAsync(User user, CancellationToken cancellationToken) => (await MapManyAsync([user], cancellationToken))[0];

    private async Task<IReadOnlyList<UserDto>> MapManyAsync(IReadOnlyList<User> users, CancellationToken cancellationToken)
    {
        var userIds = users.Select(u => u.Id).ToList();

        var roleRows = await _dbContext.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleSummary = new RoleSummaryDto(r.Id, r.Name) })
            .ToListAsync(cancellationToken);

        return users
            .Select(u => new UserDto(
                u.Id, u.Email, u.FirstName, u.LastName, u.EmployeeId, u.CustomerId, u.IsActive, u.IsBlocked,
                roleRows.Where(r => r.UserId == u.Id).Select(r => r.RoleSummary).ToList()))
            .ToList();
    }
}
