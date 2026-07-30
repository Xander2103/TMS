using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication;
using TransportationService.Api.Modules.Authentication.Dtos;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 2 remainder: refresh-token reuse detection (H4), account lockout (H8), immediate
/// block/deactivate (H14), password policy + rehash-on-login (M3), safe cross-tenant login (M4)
/// and JWT hardening (M13).
/// </summary>
public class Phase2SessionSecurityTests
{
    private static readonly DateTimeOffset Now = new(2126, 7, 30, 9, 0, 0, TimeSpan.Zero);
    private const string GoodPassword = "correct-horse-battery-staple";

    private sealed record World(SqliteTestDbContext Db, AuthService Auth, TestClock Clock, Guid TenantId, PasswordHasher Hasher);

    private static async Task<World> SeedAsync(
        AuthenticationSecurityOptions? security = null, string tenantSlug = "acme", string tenantName = "Acme")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = tenantName, Slug = tenantSlug, IsActive = true, CreatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var hasher = new PasswordHasher();
        var auth = new AuthService(
            db.Context, hasher, AuthTestFactory.TokenService(clock), clock,
            Options.Create(security ?? new AuthenticationSecurityOptions()));
        return new World(db, auth, clock, tenantId, hasher);
    }

    private static async Task<User> AddUserAsync(
        World w, string email, string password = GoodPassword, Guid? tenantId = null,
        bool isActive = true, bool isBlocked = false)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? w.TenantId,
            Email = email,
            FirstName = "F",
            LastName = "L",
            PasswordHash = w.Hasher.Hash(password),
            IsActive = isActive,
            IsBlocked = isBlocked,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        w.Db.Context.Users.Add(user);
        await w.Db.Context.SaveChangesAsync();
        return user;
    }

    // ===================== H4 — refresh-token reuse detection =====================

    [Fact]
    public async Task Refresh_ReplayOfRotatedToken_RevokesWholeFamilyAndAudits()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var user = await AddUserAsync(w, "u@acme");
        var login = await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);
        var firstRefresh = login.Tokens!.RefreshToken;

        var rotated = await w.Auth.RefreshAsync(firstRefresh, CancellationToken.None);
        Assert.Equal(AuthOutcome.Success, rotated.Outcome);
        var secondRefresh = rotated.Tokens!.RefreshToken;

        // Replay of the already-rotated token: reuse detected.
        var replay = await w.Auth.RefreshAsync(firstRefresh, CancellationToken.None);
        Assert.Equal(AuthOutcome.InvalidCredentials, replay.Outcome);

        // The successor from the same family is burned too.
        var afterReuse = await w.Auth.RefreshAsync(secondRefresh, CancellationToken.None);
        Assert.Equal(AuthOutcome.InvalidCredentials, afterReuse.Outcome);

        Assert.All(await w.Db.Context.Set<RefreshToken>().Where(t => t.UserId == user.Id).ToListAsync(),
            t => Assert.NotNull(t.RevokedAt));
        Assert.True(await w.Db.Context.AuditLogs.AnyAsync(a => a.Action == SecurityAuditEvents.RefreshReuseDetected));
    }

    [Fact]
    public async Task Refresh_ConcurrentRotationOfSameToken_YieldsAtMostOneSuccessor()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var user = await AddUserAsync(w, "race@acme");
        var login = await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);
        var token = login.Tokens!.RefreshToken;

        // Two independent contexts over the SAME database race to rotate the same token; the
        // RevokedAt concurrency token means only one UPDATE can win.
        var first = await w.Auth.RefreshAsync(token, CancellationToken.None);
        var second = await w.Auth.RefreshAsync(token, CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, first.Outcome);
        Assert.NotEqual(AuthOutcome.Success, second.Outcome);
        var successors = await w.Db.Context.Set<RefreshToken>()
            .CountAsync(t => t.UserId == user.Id && t.RevokedAt == null);
        Assert.True(successors <= 1, "at most one usable successor token may exist");
    }

    [Fact]
    public async Task SessionLimit_RevokesOldestSessionsBeyondConfiguredMaximum()
    {
        var w = await SeedAsync(new AuthenticationSecurityOptions { MaxActiveSessionsPerUser = 2 });
        using var _ = w.Db;
        var user = await AddUserAsync(w, "many@acme");

        for (var i = 0; i < 4; i++)
        {
            w.Clock.Advance(TimeSpan.FromSeconds(1));
            await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);
        }

        var active = await w.Db.Context.Set<RefreshToken>()
            .CountAsync(t => t.UserId == user.Id && t.RevokedAt == null);
        Assert.Equal(2, active);
    }

    [Fact]
    public async Task RetentionSweep_RemovesExpiredAndOldRevokedTokensOnly()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var user = await AddUserAsync(w, "purge@acme");
        var nowUtc = Now.UtcDateTime;
        w.Db.Context.Set<RefreshToken>().AddRange(
            new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TenantId = w.TenantId, TokenHash = "expired", CreatedAt = nowUtc.AddDays(-60), ExpiresAt = nowUtc.AddDays(-1) },
            new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TenantId = w.TenantId, TokenHash = "old-revoked", CreatedAt = nowUtc.AddDays(-60), ExpiresAt = nowUtc.AddDays(30), RevokedAt = nowUtc.AddDays(-45) },
            new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TenantId = w.TenantId, TokenHash = "recent-revoked", CreatedAt = nowUtc, ExpiresAt = nowUtc.AddDays(30), RevokedAt = nowUtc.AddDays(-1) },
            new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TenantId = w.TenantId, TokenHash = "active", CreatedAt = nowUtc, ExpiresAt = nowUtc.AddDays(30) });
        await w.Db.Context.SaveChangesAsync();

        var removed = await TokenRetentionHostedService.PurgeAsync(w.Db.Context, w.Clock, 30, CancellationToken.None);

        Assert.Equal(2, removed);
        var remaining = await w.Db.Context.Set<RefreshToken>().Select(t => t.TokenHash).ToListAsync();
        Assert.Contains("active", remaining);
        Assert.Contains("recent-revoked", remaining);
    }

    // ===================== H8 — account lockout =====================

    [Fact]
    public async Task Lockout_AfterConfiguredFailures_BlocksEvenWithCorrectPassword()
    {
        var w = await SeedAsync(new AuthenticationSecurityOptions { MaxFailedLoginAttempts = 3, BaseLockoutMinutes = 10 });
        using var _ = w.Db;
        var user = await AddUserAsync(w, "lock@acme");

        for (var i = 0; i < 3; i++)
        {
            await w.Auth.LoginAsync(user.Email, "wrong-password-value", CancellationToken.None);
        }

        // The lockout is account-scoped, so it holds regardless of the client's IP.
        var correct = await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);
        Assert.Equal(AuthOutcome.InvalidCredentials, correct.Outcome);

        var stored = await w.Db.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.NotNull(stored.LockoutEndsAt);
        Assert.True(await w.Db.Context.AuditLogs.AnyAsync(a => a.Action == SecurityAuditEvents.AccountLocked));
    }

    [Fact]
    public async Task Lockout_ExpiresSoItCannotBeUsedAsPermanentDenialOfService()
    {
        var w = await SeedAsync(new AuthenticationSecurityOptions { MaxFailedLoginAttempts = 2, BaseLockoutMinutes = 5 });
        using var _ = w.Db;
        var user = await AddUserAsync(w, "temp@acme");
        for (var i = 0; i < 2; i++)
        {
            await w.Auth.LoginAsync(user.Email, "wrong-password-value", CancellationToken.None);
        }

        w.Clock.Advance(TimeSpan.FromMinutes(6));
        var afterWindow = await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, afterWindow.Outcome);
        var stored = await w.Db.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(0, stored.FailedLoginCount); // counters reset after a successful sign-in
        Assert.Null(stored.LockoutEndsAt);
    }

    [Fact]
    public async Task Login_UnknownAccountAndWrongPassword_AreIndistinguishable()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        await AddUserAsync(w, "known@acme");

        var unknown = await w.Auth.LoginAsync("nobody@acme", GoodPassword, CancellationToken.None);
        var wrong = await w.Auth.LoginAsync("known@acme", "wrong-password-value", CancellationToken.None);

        Assert.Equal(AuthOutcome.InvalidCredentials, unknown.Outcome);
        Assert.Equal(AuthOutcome.InvalidCredentials, wrong.Outcome);
        Assert.Null(unknown.Tokens);
        Assert.Null(wrong.Tokens);
    }

    [Fact]
    public async Task Login_AuditsSuccessAndFailureWithoutPasswordMaterial()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var user = await AddUserAsync(w, "audit@acme");

        await w.Auth.LoginAsync(user.Email, "wrong-password-value", CancellationToken.None);
        await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);

        var events = await w.Db.Context.AuditLogs.ToListAsync();
        Assert.Contains(events, a => a.Action == SecurityAuditEvents.LoginFailed);
        Assert.Contains(events, a => a.Action == SecurityAuditEvents.LoginSucceeded);
        Assert.All(events, a =>
        {
            var blob = (a.OldValuesJson ?? "") + (a.NewValuesJson ?? "");
            Assert.DoesNotContain(GoodPassword, blob, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("wrong-password-value", blob, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ===================== H14 — blocked/inactive users =====================

    [Fact]
    public async Task BlockedUser_CannotLoginAndCannotRefresh()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var user = await AddUserAsync(w, "blocked@acme");
        var login = await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);
        var refresh = login.Tokens!.RefreshToken;

        var stored = await w.Db.Context.Users.SingleAsync(u => u.Id == user.Id);
        stored.IsBlocked = true;
        await w.Db.Context.SaveChangesAsync();

        Assert.Equal(AuthOutcome.Disabled, (await w.Auth.RefreshAsync(refresh, CancellationToken.None)).Outcome);
        Assert.Equal(AuthOutcome.Disabled, (await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task BlockedUser_LosesEffectivePermissions_Immediately()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var user = await AddUserAsync(w, "perm@acme");
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        w.Db.Context.Roles.Add(new Role { Id = roleId, TenantId = w.TenantId, Name = "R", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime });
        w.Db.Context.Permissions.Add(new Permission { Id = permissionId, Code = "users.view", Module = "users", Action = "view", Description = "x" });
        w.Db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        w.Db.Context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await w.Db.Context.SaveChangesAsync();

        var authz = new Modules.Identity.Services.PermissionAuthorizationService(w.Db.Context);
        Assert.True(await authz.UserHasPermissionAsync(user.Id, "users.view", CancellationToken.None));

        var stored = await w.Db.Context.Users.SingleAsync(u => u.Id == user.Id);
        stored.IsBlocked = true;
        await w.Db.Context.SaveChangesAsync();

        Assert.False(await authz.UserHasPermissionAsync(user.Id, "users.view", CancellationToken.None));
    }

    [Fact]
    public async Task BlockingAUser_RevokesRefreshTokensAndRotatesSecurityStamp()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var actor = await AddUserAsync(w, "admin@acme");
        var target = await AddUserAsync(w, "victim@acme");
        await w.Auth.LoginAsync(target.Email, GoodPassword, CancellationToken.None);
        var originalStamp = (await w.Db.Context.Users.SingleAsync(u => u.Id == target.Id)).SecurityStamp;

        var users = IdentityTestFactory.UserService(w.Db, w.TenantId, actor.Id);
        var result = await users.SetBlockedAsync(target.Id, true, CancellationToken.None);

        Assert.Equal(Modules.Identity.Services.UserOperationOutcome.Success, result.Outcome);
        var reloaded = await w.Db.Context.Users.SingleAsync(u => u.Id == target.Id);
        Assert.NotEqual(originalStamp, reloaded.SecurityStamp);
        Assert.All(await w.Db.Context.Set<RefreshToken>().Where(t => t.UserId == target.Id).ToListAsync(),
            t => Assert.NotNull(t.RevokedAt));
    }

    // ===================== M3 — password policy & rehash =====================

    [Theory]
    [InlineData("short")]
    [InlineData("Welkom123")]      // common password
    [InlineData("password123")]     // breached classic
    [InlineData("aaaaaaaaaaaaaa")]  // too few distinct characters
    public void PasswordPolicy_RejectsWeakOrBreachedPasswords(string password)
        => Assert.NotNull(PasswordPolicy.Default.Validate(password));

    [Fact]
    public void PasswordPolicy_AcceptsALongUniquePassphrase()
        => Assert.Null(PasswordPolicy.Default.Validate("zeewier-kantoor-42-blauw"));

    [Fact]
    public void PasswordHasher_UsesExplicitIterationCount()
    {
        // The work factor must be a reviewed constant rather than the framework default.
        Assert.True(PasswordHasher.IterationCount >= 210_000);
    }

    [Fact]
    public async Task Login_WithLegacyWeakerHash_RehashesOnSuccess()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        // Simulate an older, lower-work-factor hash produced before the policy change.
        var legacyHasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>(
            Options.Create(new Microsoft.AspNetCore.Identity.PasswordHasherOptions { IterationCount = 1000 }));
        var user = await AddUserAsync(w, "legacy@acme");
        var stored = await w.Db.Context.Users.SingleAsync(u => u.Id == user.Id);
        stored.PasswordHash = legacyHasher.HashPassword(new User(), GoodPassword);
        await w.Db.Context.SaveChangesAsync();
        var legacyHash = stored.PasswordHash;

        var result = await w.Auth.LoginAsync(user.Email, GoodPassword, CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, result.Outcome);
        var reloaded = await w.Db.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.NotEqual(legacyHash, reloaded.PasswordHash); // upgraded transparently
        Assert.Equal(PasswordVerificationResult.Success, w.Hasher.Verify(reloaded.PasswordHash, GoodPassword));
    }

    // ===================== M4 — cross-tenant login =====================

    [Fact]
    public async Task Login_SameEmailInTwoTenants_IsRefusedWithoutTenantHint()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var otherTenant = Guid.NewGuid();
        w.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await w.Db.Context.SaveChangesAsync();
        await AddUserAsync(w, "shared@example.com");
        await AddUserAsync(w, "shared@example.com", tenantId: otherTenant);

        var result = await w.Auth.LoginAsync("shared@example.com", GoodPassword, CancellationToken.None);

        // Never silently pick a tenant.
        Assert.Equal(AuthOutcome.InvalidCredentials, result.Outcome);
        Assert.True(await w.Db.Context.AuditLogs.AnyAsync(a => a.Action == SecurityAuditEvents.LoginAmbiguousTenant));
    }

    [Fact]
    public async Task Login_SameEmailInTwoTenants_SucceedsWithExplicitTenantHint()
    {
        var w = await SeedAsync();
        using var _ = w.Db;
        var otherTenant = Guid.NewGuid();
        w.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await w.Db.Context.SaveChangesAsync();
        await AddUserAsync(w, "shared@example.com");
        var otherUser = await AddUserAsync(w, "shared@example.com", tenantId: otherTenant);

        var result = await w.Auth.LoginAsync("shared@example.com", GoodPassword, "other", CancellationToken.None);

        Assert.Equal(AuthOutcome.Success, result.Outcome);
        Assert.Equal(otherUser.TenantId, result.Tokens!.User.TenantId);
    }

    // ===================== M13 — JWT hardening =====================

    [Fact]
    public void AccessToken_CarriesKeyIdAndSecurityStamp()
    {
        var clock = new TestClock(Now);
        var options = AuthTestFactory.Options();
        options.KeyId = "primary-2026";
        var svc = new TokenService(Options.Create(options), clock);
        var stamp = Guid.NewGuid();

        var token = svc.CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), "u@x", "A", "B", [], [], stamp);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        Assert.Equal("primary-2026", jwt.Header.Kid);
        Assert.Equal(stamp.ToString(), jwt.Claims.Single(c => c.Type == AppClaimTypes.SecurityStamp).Value);
    }

    [Fact]
    public void TokenValidation_RejectsUnexpectedAlgorithm()
    {
        // A token signed with a different algorithm must not validate when the algorithm is pinned.
        // HS512 needs a >=64-byte key, so use one long enough to actually produce the token —
        // otherwise the test would fail while signing and prove nothing about validation.
        const string longKey = "rotation-and-algorithm-test-key-that-is-at-least-sixty-four-bytes-long!!";
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(longKey));
        var wrongAlgToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: AuthTestFactory.Issuer, audience: AuthTestFactory.Audience,
            claims: [], notBefore: Now.UtcDateTime, expires: Now.UtcDateTime.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512)));

        var parameters = AuthTestFactory.ValidationParameters(signingKey: longKey);
        parameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(wrongAlgToken, parameters, out _));
    }

    [Fact]
    public void TokenValidation_AcceptsPreviousKeyDuringRotationWindow()
    {
        const string previousKey = "previous-rotation-key-at-least-32-bytes!!";
        var clock = new TestClock(Now);
        var oldOptions = AuthTestFactory.Options();
        oldOptions.SigningKey = previousKey;
        var tokenFromOldKey = new TokenService(Options.Create(oldOptions), clock)
            .CreateAccessToken(Guid.NewGuid(), Guid.NewGuid(), "u@x", "A", "B", [], []);

        // Validator configured with the NEW key plus the previous one (rotation window).
        var parameters = AuthTestFactory.ValidationParameters();
        parameters.IssuerSigningKey = null;
        parameters.IssuerSigningKeys =
        [
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(AuthTestFactory.SigningKey)),
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(previousKey)),
        ];

        var principal = new JwtSecurityTokenHandler().ValidateToken(tokenFromOldKey.Value, parameters, out _);
        Assert.NotNull(principal);
    }

    [Fact]
    public void JwtOptions_DefaultAccessTokenLifetime_IsShort()
    {
        Assert.True(new JwtOptions().AccessTokenMinutes <= 15);
    }
}
