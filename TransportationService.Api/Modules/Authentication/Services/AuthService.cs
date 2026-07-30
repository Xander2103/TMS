using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
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

    public AuthService(
        TransportationDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TimeProvider timeProvider)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _timeProvider = timeProvider;
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var normalized = email.Trim();
        if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(password))
        {
            return AuthResult.InvalidCredentials;
        }

        // Email is unique per tenant, so more than one candidate can exist across tenants.
        // Verify the password against each candidate; a matching hash selects the account.
        var candidates = await _db.Users
            .Where(u => u.Email == normalized)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _passwordHasher.Verify(TimingEqualizerHash, password);
            return AuthResult.InvalidCredentials;
        }

        var user = candidates.FirstOrDefault(u =>
            _passwordHasher.Verify(u.PasswordHash, password) != PasswordVerificationResult.Failed);

        if (user is null)
        {
            return AuthResult.InvalidCredentials;
        }

        var tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken);

        if (!user.IsActive || user.IsBlocked || tenant is null || !tenant.IsActive)
        {
            // Do not disclose which condition failed.
            return AuthResult.Disabled;
        }

        return await IssueTokensAsync(user, tenant.Name, cancellationToken);
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
        if (stored is null || !stored.IsActive(nowUtc))
        {
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
            return AuthResult.Disabled;
        }

        // Rotate: revoke the presented token and issue a fresh pair.
        var result = await IssueTokensAsync(user, tenant.Name, cancellationToken, revoking: stored);
        return result;
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
        };
        _db.Add(stored);

        if (revoking is not null)
        {
            revoking.RevokedAt = nowUtc;
            revoking.ReplacedByTokenHash = refresh.Hash;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var userDto = new CurrentUserDto(
            user.Id, user.TenantId, tenantName, user.Email, user.FirstName, user.LastName,
            user.EmployeeId, roles, permissions, user.MustChangePassword, user.CustomerId);

        var tokens = new AuthTokensDto(
            access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc, userDto);

        return AuthResult.Success(tokens);
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
}
