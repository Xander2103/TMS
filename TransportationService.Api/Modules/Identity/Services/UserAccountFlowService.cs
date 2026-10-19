using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Services;

namespace TransportationService.Api.Modules.Identity.Services;

/// <param name="EmailQueued">True when the activation link was also queued as an e-mail to the
/// account's own address (outbox kind <c>user_activation</c>).</param>
public record StartedTokenDto(string Token, DateTime ExpiresAtUtc, bool EmailQueued = false);

/// <summary>
/// Outcome of issuing an activation token, for server-side callers (tenant provisioning) that
/// must report the queued mail WITHOUT ever handling the link itself in their output.
/// </summary>
public sealed record ActivationIssueResult(Guid UserId, string Token, DateTime ExpiresAtUtc, QueueResult? Mail);

public interface IUserAccountFlowService
{
    /// <summary>
    /// Starts (or restarts) the secure activation flow for a user: previous activation tokens
    /// are revoked, a fresh single-use token is issued and the next login forces a password
    /// change. The activation link is queued as an e-mail to the account's own address via the
    /// outbox (H9), and the RAW token is additionally returned exactly once, for the
    /// administrator who may hand it over in person. Null when the user does not exist.
    /// </summary>
    Task<StartedTokenDto?> StartActivationAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Anonymous forgot-password entry: NEVER discloses whether the account exists. Existing
    /// active accounts get a fresh single-use reset token (older ones revoked) and the reset link
    /// is queued as an e-mail through the outbox (sent by the dispatcher via SMTP in every
    /// environment; the Development sink in Development); requests are rate-limited per user.
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    /// <summary>Completes activation or reset: consumes the token, sets the password, revokes every refresh token.</summary>
    Task<string?> CompleteWithTokenAsync(string token, string newPassword, CancellationToken cancellationToken);
}

public class UserAccountFlowService : IUserAccountFlowService
{
    public const int ActivationHours = 72;
    public const int ResetHours = 2;
    private const int MaxOpenResetTokens = 3;
    private const int MinPasswordLength = 8;

    private readonly TransportationDbContext _dbContext;
    private readonly ITenantAccessor _tenantAccessor;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<UserAccountFlowService>? _logger;

    // Deliberately the OPTIONAL tenant accessor, not ITenantContext: forgot-password and
    // reset-password are anonymous and tenant-neutral by design (they scope by the token/email's
    // own user row), while StartActivationAsync demands a resolved tenant explicitly below.
    // `environment` is no longer consulted (H9: mail delivery is identical in every environment;
    // the Development sink is the outbox provider there) and stays for call-site compatibility.
    public UserAccountFlowService(
        TransportationDbContext dbContext,
        ITenantAccessor tenantAccessor,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        TimeProvider timeProvider,
        IHostEnvironment environment,
        IConfiguration? configuration = null,
        ILogger<UserAccountFlowService>? logger = null)
    {
        _dbContext = dbContext;
        _tenantAccessor = tenantAccessor;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _timeProvider = timeProvider;
        _configuration = configuration;
        _logger = logger;
    }

    private static string NewRawToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    /// <summary>One-way reference to a raw token: unique per issuance, useless for activation (C3).</summary>
    private static string TokenReference(string raw) => HashToken(raw)[..12];

    public async Task<StartedTokenDto?> StartActivationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var issued = await IssueActivationAsync(userId, cancellationToken);
        return issued is null
            ? null
            : new StartedTokenDto(issued.Token, issued.ExpiresAtUtc, issued.Mail?.Outcome == QueueOutcome.Queued);
    }

    /// <summary>
    /// <see cref="StartActivationAsync"/> with the full mail outcome. Same tenant rule: the
    /// tenant comes from the injected accessor (the request, or — for tenant provisioning — a
    /// fixed tenant context the caller constructs explicitly). Never assumes a tenant.
    /// </summary>
    public async Task<ActivationIssueResult?> IssueActivationAsync(Guid userId, CancellationToken cancellationToken)
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
        var expiresAt = now.AddHours(ActivationHours);
        _dbContext.UserSecurityTokens.Add(new UserSecurityToken
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId,
            UserId = user.Id,
            Kind = UserSecurityTokenKind.Activation,
            TokenHash = HashToken(raw),
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        user.MustChangePassword = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Token itself is never audited.
        await _auditService.RecordAsync("User", user.Id.ToString(), "ActivationStarted", null,
            new { user.Email }, cancellationToken);

        var query = $"?token={Uri.EscapeDataString(raw)}&email={Uri.EscapeDataString(user.Email)}";
        var mail = await QueueAccountMailAsync(
            user, MessageKinds.UserActivation, BuildLink("/activeren", query), ActivationHours, raw, cancellationToken);

        return new ActivationIssueResult(user.Id, raw, expiresAt, mail);
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
            .Where(u => u.NormalizedEmail == Entities.User.NormalizeEmail(normalized) && u.IsActive && !u.IsBlocked)
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

            // H9: the reset link goes out through the outbox in EVERY environment (SMTP outside
            // Development, the message sink in Development). A queue failure is logged and never
            // changes the answer — the endpoint stays a uniform 204 (no account enumeration).
            await QueueAccountMailAsync(
                user, MessageKinds.PasswordReset,
                BuildLink("/reset-password", $"?token={Uri.EscapeDataString(raw)}"), ResetHours, raw, cancellationToken);
        }
    }

    /// <summary>Absolute link from <c>Frontend:BaseUrl</c>; relative when it is not configured.</summary>
    private string BuildLink(string path, string query)
    {
        var baseUrl = _configuration?["Frontend:BaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl) ? $"{path}{query}" : $"{baseUrl.TrimEnd('/')}{path}{query}";
    }

    /// <summary>
    /// Queues an account-security mail for the account's OWN address, in the account's tenant
    /// (anonymous flows have no request tenant, so the outbox is scoped explicitly — the same
    /// pattern the background dispatchers use). Recipient preferences never apply. The idempotency
    /// key carries only a one-way token reference; the dispatcher scrubs the body after delivery.
    /// </summary>
    private async Task<QueueResult?> QueueAccountMailAsync(
        User user, string kind, string link, int validHours, string rawToken, CancellationToken cancellationToken)
    {
        try
        {
            var language = string.IsNullOrWhiteSpace(user.PreferredLanguageCode)
                ? "nl"
                : user.PreferredLanguageCode.Trim().ToLowerInvariant();
            var outbox = new MessageOutboxService(_dbContext, new DevTenantContext(user.TenantId), _timeProvider);
            return await outbox.QueueAsync(new MessageRequest(
                kind,
                MessageOwnerType.Employee, user.EmployeeId ?? user.Id,
                new Dictionary<string, string>
                {
                    ["firstName"] = user.FirstName,
                    ["link"] = link,
                    ["validity"] = Validity(language, validHours),
                },
                RelatedEntityType: "User",
                RelatedEntityId: user.Id.ToString(),
                IdempotencyKey: $"{kind}:User:{user.Id}:{TokenReference(rawToken)}",
                OverrideAddress: user.Email,
                OverrideName: $"{user.FirstName} {user.LastName}".Trim(),
                OverrideLanguage: language,
                IgnoreRecipientPreferences: true), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Never the token or the link in the log — only which account and which kind.
            _logger?.LogError(exception, "Account mail '{Kind}' could not be queued for user {UserId}.", kind, user.Id);
            return null;
        }
    }

    private static string Validity(string language, int hours) => language switch
    {
        "fr" => $"{hours} heures",
        "en" => $"{hours} hours",
        _ => $"{hours} uur",
    };

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
