using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

public record StartedTokenDto(string Token, DateTime ExpiresAtUtc);

public interface IUserAccountFlowService
{
    /// <summary>
    /// Starts (or restarts) the secure activation flow for a user: previous activation tokens
    /// are revoked, a fresh single-use token is issued and the next login forces a password
    /// change. The RAW token is returned exactly once, for the administrator to hand over
    /// (mail delivery hooks in here later). Null when the user does not exist.
    /// </summary>
    Task<StartedTokenDto?> StartActivationAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Anonymous forgot-password entry: NEVER discloses whether the account exists. Existing
    /// active accounts get a fresh single-use reset token (older ones revoked); requests are
    /// rate-limited per user. In Development the reset link lands in the message sink.
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    /// <summary>Completes activation or reset: consumes the token, sets the password, revokes every refresh token.</summary>
    Task<string?> CompleteWithTokenAsync(string token, string newPassword, CancellationToken cancellationToken);
}

public class UserAccountFlowService : IUserAccountFlowService
{
    private const int ActivationHours = 72;
    private const int ResetHours = 2;
    private const int MaxOpenResetTokens = 3;
    private const int MinPasswordLength = 8;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantAccessor _tenantAccessor;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IHostEnvironment _environment;

    // Deliberately the OPTIONAL tenant accessor, not ITenantContext: forgot-password and
    // reset-password are anonymous and tenant-neutral by design (they scope by the token/email's
    // own user row), while StartActivationAsync demands a resolved tenant explicitly below.
    public UserAccountFlowService(
        TransportationDbContext dbContext,
        ITenantAccessor tenantAccessor,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        TimeProvider timeProvider,
        IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _tenantAccessor = tenantAccessor;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _environment = environment;
    }

    private static string NewRawToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    public async Task<StartedTokenDto?> StartActivationAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Administrative flow: always runs inside an authenticated tenant scope. Fail-closed —
        // without a resolved tenant no account may be targeted, and no tenant is ever assumed.
        if (!_tenantAccessor.TryGetTenantId(out var tenantId))
        {
            throw new InvalidOperationException(
                "Account activation requires a resolved tenant context; the request is not tenant-authenticated.");
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        await RevokeOpenTokensAsync(user.Id, UserSecurityTokenKind.Activation, now, cancellationToken);

        var raw = NewRawToken();
        _dbContext.UserSecurityTokens.Add(new UserSecurityToken
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            Kind = UserSecurityTokenKind.Activation,
            TokenHash = HashToken(raw),
            CreatedAt = now,
            ExpiresAt = now.AddHours(ActivationHours),
        });
        user.MustChangePassword = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Token itself is never audited.
        await _auditService.RecordAsync("User", user.Id.ToString(), "ActivationStarted", null,
            new { user.Email }, cancellationToken);

        return new StartedTokenDto(raw, now.AddHours(ActivationHours));
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        // Cross-tenant lookup mirrors the login flow; the response never differs.
        var users = await _dbContext.Users
            .Where(u => u.Email == normalized && u.IsActive && !u.IsBlocked)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var user in users)
        {
            // Rate limit: silently skip when the user has hammered the endpoint.
            var recent = await _dbContext.UserSecurityTokens.CountAsync(t =>
                t.UserId == user.Id && t.Kind == UserSecurityTokenKind.PasswordReset
                && t.CreatedAt > now.AddHours(-1), cancellationToken);
            if (recent >= MaxOpenResetTokens)
            {
                continue;
            }

            await RevokeOpenTokensAsync(user.Id, UserSecurityTokenKind.PasswordReset, now, cancellationToken);

            var raw = NewRawToken();
            _dbContext.UserSecurityTokens.Add(new UserSecurityToken
            {
                Id = Guid.NewGuid(),
                TenantId = user.TenantId,
                UserId = user.Id,
                Kind = UserSecurityTokenKind.PasswordReset,
                TokenHash = HashToken(raw),
                CreatedAt = now,
                ExpiresAt = now.AddHours(ResetHours),
            });
            // Anonymous flow: no tenant context on the request, so the audit row is written
            // directly with the USER's tenant (never the token itself).
            _dbContext.AuditLogs.Add(new Modules.Auditing.Entities.AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = user.TenantId,
                EntityType = "User",
                EntityId = user.Id.ToString(),
                Action = "PasswordResetRequested",
                Timestamp = now,
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Mail delivery plugs in here later; Development drops the link in the message sink.
            if (_environment.IsDevelopment())
            {
                var sinkDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "message-sink");
                Directory.CreateDirectory(sinkDirectory);
                await File.AppendAllTextAsync(
                    Path.Combine(sinkDirectory, "password-resets.txt"),
                    $"{now:O} {user.Email} /reset-password?token={raw}{Environment.NewLine}",
                    cancellationToken);
            }
        }
    }

    public async Task<string?> CompleteWithTokenAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "De link is ongeldig of verlopen.";
        }

        if (Modules.Authentication.PasswordPolicy.Default.Validate(newPassword) is { } policyError)
        {
            return policyError;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var hash = HashToken(token.Trim());
        var stored = await _dbContext.UserSecurityTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null || !stored.IsUsable(now))
        {
            return "De link is ongeldig of verlopen.";
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId, cancellationToken);
        if (user is null || !user.IsActive || user.IsBlocked)
        {
            return "De link is ongeldig of verlopen.";
        }

        stored.UsedAt = now;
        await RevokeOpenTokensAsync(user.Id, stored.Kind, now, cancellationToken);
        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.MustChangePassword = false;

        // Every existing session dies with the old credential: revoke refresh tokens AND rotate
        // the security stamp so already-issued access tokens are rejected on their next request.
        var refreshTokens = await _dbContext.Set<RefreshToken>()
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.RevokedAt = now;
        }

        user.SecurityStamp = Guid.NewGuid();

        _dbContext.AuditLogs.Add(new Modules.Auditing.Entities.AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            EntityType = "User",
            EntityId = user.Id.ToString(),
            Action = stored.Kind == UserSecurityTokenKind.Activation ? "AccountActivated" : "PasswordResetCompleted",
            Timestamp = now,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return null;
    }

    private async Task RevokeOpenTokensAsync(
        Guid userId, UserSecurityTokenKind kind, DateTime now, CancellationToken cancellationToken)
    {
        var open = await _dbContext.UserSecurityTokens
            .Where(t => t.UserId == userId && t.Kind == kind && t.UsedAt == null && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in open)
        {
            token.RevokedAt = now;
        }
    }
}
