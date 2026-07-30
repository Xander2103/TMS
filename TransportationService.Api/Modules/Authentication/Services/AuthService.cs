using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Identity.Entities;

namespace TransportationService.Api.Modules.Authentication.Services;

public sealed class AuthService : IAuthService
{
    // Verified against when the email matches no account, so unknown-email and wrong-password
    // logins take comparable time (no user-enumeration via response timing).
    private static readonly string TimingEqualizerHash =
        new PasswordHasher().Hash(Guid.NewGuid().ToString("N"));

    private readonly TransportationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationSecurityOptions _security;

    public AuthService(
        TransportationDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TimeProvider timeProvider,
        IOptions<AuthenticationSecurityOptions>? security = null)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
        _security = security?.Value ?? new AuthenticationSecurityOptions();
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
        => await LoginAsync(email, password, tenantHint: null, cancellationToken);

    /// <summary>
    /// Sign-in. <paramref name="tenantHint"/> disambiguates the (rare) case where the same email
    /// exists in several tenants: without it an ambiguous login is refused rather than silently
    /// landing in whichever row the database returned first. The response never reveals whether
    /// an account exists, is locked, is disabled or is ambiguous.
    /// </summary>
    public async Task<AuthResult> LoginAsync(
        string email, string password, string? tenantHint, CancellationToken cancellationToken)
    {
        var normalized = email.Trim();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(password))
        {
            return AuthResult.InvalidCredentials;
        }

        var candidates = await _db.Users
            .Where(u => u.Email == normalized)
            .OrderBy(u => u.TenantId).ThenBy(u => u.Id) // deterministic; never "first row wins"
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _passwordHasher.Verify(TimingEqualizerHash, password);
            await RecordAsync(null, null, SecurityAuditEvents.LoginFailed, new { Reason = "unknown-account" }, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        // Narrow by explicit tenant selection first, so at most one password hash is computed per
        // attempt (no N×PBKDF2 amplification across tenants).
        if (candidates.Count > 1 && !string.IsNullOrWhiteSpace(tenantHint))
        {
            var hint = tenantHint.Trim();
            var tenantIds = candidates.Select(c => c.TenantId).Distinct().ToList();
            var matchingTenantIds = await _db.Tenants.AsNoTracking()
                .Where(t => tenantIds.Contains(t.Id) && (t.Slug == hint || t.Name == hint))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            candidates = candidates.Where(c => matchingTenantIds.Contains(c.TenantId)).ToList();
        }

        if (candidates.Count == 0)
        {
            _passwordHasher.Verify(TimingEqualizerHash, password);
            return AuthResult.InvalidCredentials;
        }

        if (candidates.Count > 1)
        {
            // Ambiguous: refuse rather than pick. No information about the other tenants leaks.
            _passwordHasher.Verify(TimingEqualizerHash, password);
            await RecordAsync(candidates[0].TenantId, null, SecurityAuditEvents.LoginAmbiguousTenant,
                new { Reason = "multiple-tenants-for-email" }, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        var user = candidates[0];

        // Locked accounts never reach password verification, and the response is indistinguishable
        // from a wrong password so lockout state cannot be probed.
        if (user.LockoutEndsAt is { } lockedUntil && lockedUntil > nowUtc)
        {
            _passwordHasher.Verify(TimingEqualizerHash, password);
            await RecordAsync(user.TenantId, user.Id, SecurityAuditEvents.LoginBlockedWhileLocked,
                new { LockedUntilUtc = lockedUntil }, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        var verification = _passwordHasher.Verify(user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RegisterFailedAttemptAsync(user, nowUtc, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken);

        if (!user.IsActive || user.IsBlocked || tenant is null || !tenant.IsActive)
        {
            // Do not disclose which condition failed.
            return AuthResult.Disabled;
        }

        // Rehash-on-login: an outdated hash (older work factor / format) is transparently upgraded
        // now that we hold the plaintext and it verified.
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.Hash(password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEndsAt = null;

        var result = await IssueTokensAsync(user, tenant.Name, cancellationToken);
        await RecordAsync(user.TenantId, user.Id, SecurityAuditEvents.LoginSucceeded, null, cancellationToken);
        return result;
    }

    private async Task RegisterFailedAttemptAsync(User user, DateTime nowUtc, CancellationToken cancellationToken)
    {
        user.FailedLoginCount += 1;

        if (user.FailedLoginCount >= _security.MaxFailedLoginAttempts)
        {
            // Exponential back-off, capped: repeated lockouts get progressively longer but never
            // become permanent, so this cannot be abused to lock a victim out indefinitely.
            var overshoot = user.FailedLoginCount - _security.MaxFailedLoginAttempts;
            var minutes = Math.Min(
                _security.BaseLockoutMinutes * Math.Pow(2, Math.Min(overshoot, 10)),
                _security.MaxLockoutMinutes);
            user.LockoutEndsAt = nowUtc.AddMinutes(minutes);
            await RecordAsync(user.TenantId, user.Id, SecurityAuditEvents.AccountLocked,
                new { user.FailedLoginCount, LockedUntilUtc = user.LockoutEndsAt }, cancellationToken, save: false);
        }

        await RecordAsync(user.TenantId, user.Id, SecurityAuditEvents.LoginFailed,
            new { Reason = "invalid-password", user.FailedLoginCount }, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthResult.InvalidCredentials;
        }

        var hash = _tokenService.HashRefreshToken(refreshToken);
        var stored = await _db.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (stored is null)
        {
            return AuthResult.InvalidCredentials;
        }

        // REUSE DETECTION: a token that was already rotated/revoked is being presented again.
        // Either it was stolen, or the legitimate client replayed it — in both cases the whole
        // rotation family is burned and every session of that user is revoked.
        if (stored.RevokedAt is not null)
        {
            await RevokeFamilyAsync(stored, nowUtc, cancellationToken);
            await RecordAsync(stored.TenantId, stored.UserId, SecurityAuditEvents.RefreshReuseDetected,
                new { stored.FamilyId, Action = "family-revoked" }, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        if (!stored.IsActive(nowUtc))
        {
            await RecordAsync(stored.TenantId, stored.UserId, SecurityAuditEvents.RefreshRejected,
                new { Reason = "expired" }, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        var tenant = user is null
            ? null
            : await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken);

        if (user is null || !user.IsActive || user.IsBlocked || tenant is null || !tenant.IsActive)
        {
            stored.RevokedAt = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync(stored.TenantId, stored.UserId, SecurityAuditEvents.RefreshRejected,
                new { Reason = "account-or-tenant-disabled" }, cancellationToken);
            return AuthResult.Disabled;
        }

        AuthResult result;
        try
        {
            // Rotate: revoke the presented token and issue a fresh pair. The optimistic-concurrency
            // guard below makes two simultaneous refreshes of the SAME token produce at most one
            // successful rotation — the loser's UPDATE affects zero rows and is rejected.
            result = await IssueTokensAsync(user, tenant.Name, cancellationToken, revoking: stored);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            await RecordAsync(stored.TenantId, stored.UserId, SecurityAuditEvents.RefreshRejected,
                new { Reason = "concurrent-rotation" }, cancellationToken);
            return AuthResult.InvalidCredentials;
        }

        await RecordAsync(user.TenantId, user.Id, SecurityAuditEvents.TokenRefreshed, null, cancellationToken);
        return result;
    }

    /// <summary>Revokes every token in the presented token's rotation family (reuse response).</summary>
    private async Task RevokeFamilyAsync(RefreshToken presented, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var family = await _db.Set<RefreshToken>()
            .Where(t => t.UserId == presented.UserId
                        && (t.FamilyId == presented.FamilyId || t.Id == presented.Id)
                        && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in family)
        {
            token.RevokedAt = nowUtc;
        }

        // A confirmed reuse is a compromise signal: burn the access tokens too.
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == presented.UserId, cancellationToken);
        if (user is not null)
        {
            user.SecurityStamp = Guid.NewGuid();
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = _tokenService.HashRefreshToken(refreshToken);
        var stored = await _db.Set<RefreshToken>()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = _timeProvider.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
            await RecordAsync(stored.TenantId, stored.UserId, SecurityAuditEvents.Logout, null, cancellationToken);
        }
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken);

        var (roles, permissions) = await LoadRolesAndPermissionsAsync(user, cancellationToken);

        return new CurrentUserDto(
            user.Id,
            user.TenantId,
            tenant?.Name ?? string.Empty,
            user.Email,
            user.FirstName,
            user.LastName,
            user.EmployeeId,
            roles,
            permissions,
            user.MustChangePassword,
            user.CustomerId);
    }

    private async Task<AuthResult> IssueTokensAsync(
        User user,
        string tenantName,
        CancellationToken cancellationToken,
        RefreshToken? revoking = null)
    {
        var (roles, permissions) = await LoadRolesAndPermissionsAsync(user, cancellationToken);

        var access = _tokenService.CreateAccessToken(
            user.Id, user.TenantId, user.Email, user.FirstName, user.LastName, roles, permissions,
            user.SecurityStamp, user.MustChangePassword);

        var refresh = _tokenService.CreateRefreshToken();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var stored = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = refresh.Hash,
            CreatedAt = nowUtc,
            ExpiresAt = refresh.ExpiresAtUtc,
            // A rotation keeps its predecessor's family; a fresh login starts a new one.
            FamilyId = revoking?.FamilyId is { } family && family != Guid.Empty ? family : Guid.NewGuid(),
        };
        _db.Add(stored);

        if (revoking is not null)
        {
            revoking.RevokedAt = nowUtc;
            revoking.ReplacedByTokenHash = refresh.Hash;
        }

        await EnforceSessionLimitAsync(user.Id, nowUtc, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var userDto = new CurrentUserDto(
            user.Id, user.TenantId, tenantName, user.Email, user.FirstName, user.LastName,
            user.EmployeeId, roles, permissions, user.MustChangePassword, user.CustomerId);

        var tokens = new AuthTokensDto(
            access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc, userDto);

        return AuthResult.Success(tokens);
    }

    /// <summary>Caps concurrent sessions by revoking the oldest active tokens beyond the limit.</summary>
    private async Task EnforceSessionLimitAsync(Guid userId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var active = await _db.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > nowUtc)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        // The token being added in this unit of work is not in the query result yet, so keep
        // (limit - 1) existing ones.
        foreach (var stale in active.Skip(Math.Max(_security.MaxActiveSessionsPerUser - 1, 0)))
        {
            stale.RevokedAt = nowUtc;
        }
    }

    private async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> LoadRolesAndPermissionsAsync(
        User user, CancellationToken cancellationToken)
    {
        // Only active roles that belong to the user's own tenant grant access.
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == user.Id)
            .Join(
                _db.Roles.AsNoTracking().Where(r => r.IsActive && r.TenantId == user.TenantId),
                ur => ur.RoleId, r => r.Id, (ur, r) => r.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var roles = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var permissions = await _db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(_db.Permissions.AsNoTracking(), rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        return (roles, permissions);
    }

    /// <summary>
    /// Writes a security audit row. Deliberately free of password/token material; the account is
    /// identified by id only. Never throws into the caller's flow.
    /// </summary>
    private async Task RecordAsync(
        Guid? tenantId, Guid? userId, string action, object? details,
        CancellationToken cancellationToken, bool save = true)
    {
        try
        {
            _db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId ?? Guid.Empty,
                EntityType = SecurityAuditEvents.EntityType,
                EntityId = userId?.ToString() ?? "unknown",
                Action = action,
                UserId = userId,
                NewValuesJson = details is null ? null : System.Text.Json.JsonSerializer.Serialize(details),
                Timestamp = _timeProvider.GetUtcNow().UtcDateTime,
            });

            if (save)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Auditing must never break authentication; the failure surfaces via logs/monitoring.
        }
    }
}
