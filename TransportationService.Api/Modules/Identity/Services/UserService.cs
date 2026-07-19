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

    public UserService(
        TransportationDbContext dbContext, ITenantContext tenantContext, IAuditService auditService,
        Authentication.Services.IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditService = auditService;
        _passwordHasher = passwordHasher;
    }

    /// <summary>Administrative (re)set of a user's password; the change is audited, the hash never leaves the server.</summary>
    public async Task<UserOperationResult> SetPasswordAsync(Guid id, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
        {
            return new UserOperationResult(UserOperationOutcome.ValidationFailed, null,
                $"Het wachtwoord moet minstens {MinPasswordLength} tekens lang zijn.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null)
        {
            return new UserOperationResult(UserOperationOutcome.NotFound, null);
        }

        user.PasswordHash = _passwordHasher.Hash(password);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "PasswordSet", null,
            new { ByAdministrator = true }, cancellationToken);

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
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId, cancellationToken);
        if (user is null) return new UserOperationResult(UserOperationOutcome.NotFound, null);

        var newRoleIds = request.RoleIds.Distinct().ToHashSet();
        await EnsureRolesInTenantAsync(newRoleIds, cancellationToken);

        var wasAdministrator = await IsAdministratorAsync(user.Id, cancellationToken);
        var staysAdministrator = await RoleIdsIncludeAdministratorAsync(newRoleIds, cancellationToken);

        if (wasAdministrator && !staysAdministrator && await IsLastActiveAdministratorAsync(user, cancellationToken))
        {
            return new UserOperationResult(UserOperationOutcome.LastActiveAdministrator, await MapAsync(user, cancellationToken));
        }

        var existingRoles = await _dbContext.UserRoles.Where(ur => ur.UserId == id).ToListAsync(cancellationToken);
        var oldRoleIds = existingRoles.Select(ur => ur.RoleId).ToList();
        _dbContext.UserRoles.RemoveRange(existingRoles);
        foreach (var roleId in newRoleIds)
        {
            _dbContext.UserRoles.Add(new UserRole { UserId = id, RoleId = roleId });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditService.RecordAsync("User", user.Id.ToString(), "AssignRoles", new { RoleIds = oldRoleIds }, new { RoleIds = newRoleIds }, cancellationToken);

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
