using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

/// <summary>
/// Central, fail-closed security helpers shared by user- and role-management so that privilege
/// rules live in exactly one place. No decision here trusts client input; every check reads the
/// current tenant-scoped state from the database. Callers must be authenticated — a missing acting
/// user resolves to "deny".
/// </summary>
public interface IAccountSecurityService
{
    /// <summary>The acting user's id (fail-closed: throws when there is no authenticated actor).</summary>
    Guid RequireActorId();

    /// <summary>Effective permission codes a user holds across all of their active roles.</summary>
    Task<IReadOnlySet<string>> EffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>True when the user is a member of any protected (IsSystemRole) role in the tenant.</summary>
    Task<bool> IsProtectedSystemUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The acting user may manage (reset password, change roles of) the target only when the
    /// target holds no permission the actor lacks AND the target is not a protected system user
    /// unless the actor is one too. Fail-closed on any uncertainty.
    /// </summary>
    Task<bool> CanManageUserAsync(Guid targetUserId, CancellationToken cancellationToken);

    /// <summary>True when <paramref name="requested"/> ⊆ the acting user's effective permissions.</summary>
    Task<bool> ActorHoldsAllAsync(IEnumerable<string> requested, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes all outstanding sessions of a user: revokes every active refresh token AND rotates
    /// the user's SecurityStamp so existing access tokens are rejected on their next request. The
    /// caller owns the SaveChanges (so this composes into one transaction with the triggering change).
    /// </summary>
    Task RevokeAllSessionsAsync(User user, CancellationToken cancellationToken);

    /// <summary>Whether a role is a customer-portal template (only customer_portal.* permissions allowed).</summary>
    bool IsPortalTemplateRole(Role role);

    /// <summary>All codes belong to the customer_portal.* namespace (case-insensitive).</summary>
    bool IsPortalPermissionSet(IEnumerable<string> permissionCodes);
}

public sealed class AccountSecurityService : IAccountSecurityService
{
    public const string PortalTemplatePrefix = "klantportaal";

    /// <summary>Kept for callers that already reference it; the rule itself lives in
    /// <see cref="PortalPermissionScope"/>.</summary>
    public const string PortalPermissionPrefix = PortalPermissionScope.Prefix;

    private readonly TransportationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPermissionSetService _permissions;
    private readonly TimeProvider _timeProvider;

    public AccountSecurityService(
        TransportationDbContext db, ITenantContext tenant, ICurrentUserContext currentUser,
        IPermissionSetService permissions, TimeProvider timeProvider)
    {
        _db = db;
        _tenant = tenant;
        _currentUser = currentUser;
        _permissions = permissions;
        _timeProvider = timeProvider;
    }

    public Guid RequireActorId() =>
        _currentUser.CurrentUserId
        ?? throw new UnauthorizedAccessException("No authenticated actor for a privileged operation (fail-closed).");

    /// <summary>
    /// Deliberately the RAW assigned set, not the identity-class-filtered one: this feeds the
    /// privilege-comparison guards below, where over-reporting the target's rights is the safe
    /// direction. (A portal identity whose account still carries an internal role cannot USE that
    /// role — see the guard in PermissionSetService — but it must still count against an actor who
    /// wants to manage that account.)
    /// </summary>
    public Task<IReadOnlySet<string>> EffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        _permissions.GetAssignedPermissionCodesAsync(userId, cancellationToken);

    public async Task<bool> IsProtectedSystemUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(_db.Roles.AsNoTracking().Where(r => r.TenantId == _tenant.TenantId && r.IsSystemRole),
                ur => ur.RoleId, r => r.Id, (ur, r) => r.Id)
            .AnyAsync(cancellationToken);

    public async Task<bool> CanManageUserAsync(Guid targetUserId, CancellationToken cancellationToken)
    {
        var actorId = RequireActorId();
        var actorPermissions = await EffectivePermissionsAsync(actorId, cancellationToken);
        var targetPermissions = await EffectivePermissionsAsync(targetUserId, cancellationToken);

        // Escalation guard: the target may hold nothing the actor lacks.
        if (!targetPermissions.All(actorPermissions.Contains))
        {
            return false;
        }

        // System-account guard: a protected system user may only be managed by another system user.
        if (await IsProtectedSystemUserAsync(targetUserId, cancellationToken)
            && !await IsProtectedSystemUserAsync(actorId, cancellationToken))
        {
            return false;
        }

        return true;
    }

    public async Task<bool> ActorHoldsAllAsync(IEnumerable<string> requested, CancellationToken cancellationToken)
    {
        var actorPermissions = await EffectivePermissionsAsync(RequireActorId(), cancellationToken);
        return requested.All(code => actorPermissions.Contains(code));
    }

    public async Task RevokeAllSessionsAsync(User user, CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var activeTokens = await _db.Set<RefreshToken>()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = nowUtc;
        }

        // Rotating the stamp invalidates every already-issued access token on its next request.
        user.SecurityStamp = Guid.NewGuid();
    }

    public bool IsPortalTemplateRole(Role role) =>
        role.TemplateCode is { } code && code.StartsWith(PortalTemplatePrefix, StringComparison.OrdinalIgnoreCase);

    // One rule, one place: see PortalPermissionScope for why the comparison is ordinal.
    public bool IsPortalPermissionSet(IEnumerable<string> permissionCodes) =>
        PortalPermissionScope.CoversAll(permissionCodes);
}
