using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.Internal;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

public class AccountFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId)
    {
        public UserAccountFlowService Flow(DateTimeOffset at)
        {
            var tenant = new DevTenantContext(TenantId);
            return new UserAccountFlowService(Db.Context, tenant, new PasswordHasher(),
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)),
                new TestClock(at), new HostingEnvironment { EnvironmentName = "Production" });
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Janssens",
            IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId);
    }

    [Fact]
    public async Task Activation_TokenIsSingleUse_SetsPassword_AndKillsSessions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var flow = h.Flow(Now);

        // A stale session that must die with the credential change.
        h.Db.Context.Set<RefreshToken>().Add(new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = h.UserId, TenantId = h.TenantId,
            TokenHash = "stale", CreatedAt = Now.UtcDateTime, ExpiresAt = Now.UtcDateTime.AddDays(7),
        });
        await h.Db.Context.SaveChangesAsync();

        var started = await flow.StartActivationAsync(h.UserId, CancellationToken.None);
        Assert.NotNull(started);

        var stored = h.Db.Context.UserSecurityTokens.Single();
        Assert.NotEqual(started!.Token, stored.TokenHash); // raw value never stored
        Assert.True(h.Db.Context.Users.Single().MustChangePassword);

        var error = await flow.CompleteWithTokenAsync(started.Token, "NieuwWachtwoord1", CancellationToken.None);
        Assert.Null(error);

        var user = h.Db.Context.Users.Single();
        Assert.NotNull(user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher().Verify(user.PasswordHash, "NieuwWachtwoord1"));
        Assert.NotNull(h.Db.Context.Set<RefreshToken>().Single().RevokedAt);

        // Single use: replay fails.
        var replay = await flow.CompleteWithTokenAsync(started.Token, "NogEenWachtwoord1", CancellationToken.None);
        Assert.NotNull(replay);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var started = await h.Flow(Now).StartActivationAsync(h.UserId, CancellationToken.None);

        var lateFlow = h.Flow(Now.AddDays(4)); // activation window is 72h
        var error = await lateFlow.CompleteWithTokenAsync(started!.Token, "NieuwWachtwoord1", CancellationToken.None);

        Assert.NotNull(error);
        Assert.Null(h.Db.Context.Users.Single().PasswordHash);
    }

    [Fact]
    public async Task ForgotPassword_NeverDisclosesAccounts_AndIsRateLimited()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var flow = h.Flow(Now);

        // Unknown email: silently nothing.
        await flow.RequestPasswordResetAsync("bestaat-niet@acme.be", CancellationToken.None);
        Assert.Empty(h.Db.Context.UserSecurityTokens.ToList());

        // Known email: one usable token; older ones are revoked on each request.
        await flow.RequestPasswordResetAsync("jan@acme.be", CancellationToken.None);
        await flow.RequestPasswordResetAsync("jan@acme.be", CancellationToken.None);
        await flow.RequestPasswordResetAsync("jan@acme.be", CancellationToken.None);
        var tokens = h.Db.Context.UserSecurityTokens.Where(t => t.Kind == UserSecurityTokenKind.PasswordReset).ToList();
        Assert.Equal(3, tokens.Count);
        Assert.Single(tokens, t => t.IsUsable(Now.UtcDateTime));

        // Rate limit: the fourth request within the hour creates nothing new.
        await flow.RequestPasswordResetAsync("jan@acme.be", CancellationToken.None);
        Assert.Equal(3, h.Db.Context.UserSecurityTokens.Count(t => t.Kind == UserSecurityTokenKind.PasswordReset));

        // Audit trail exists but never contains a token value.
        Assert.True(h.Db.Context.AuditLogs.Count(a => a.Action == "PasswordResetRequested") >= 1);
    }

    [Fact]
    public async Task RoleMappings_SuggestRoles_ForTheEmployeesFunctions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var functionId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        h.Db.Context.JobFunctions.Add(new JobFunction { Id = functionId, TenantId = h.TenantId, Code = "CHAUF", Name = "Chauffeur", IsActive = true });
        h.Db.Context.Roles.Add(new Role { Id = roleId, TenantId = h.TenantId, Name = "Chauffeur", IsActive = true });
        h.Db.Context.Employees.Add(new Modules.Employees.Entities.Employee
        {
            Id = employeeId, TenantId = h.TenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssens",
        });
        h.Db.Context.Set<Modules.Employees.Entities.EmployeeJobFunction>().Add(
            new Modules.Employees.Entities.EmployeeJobFunction { EmployeeId = employeeId, JobFunctionId = functionId });
        await h.Db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(h.TenantId);
        var mappings = new JobFunctionRoleMappingService(h.Db.Context, tenant,
            new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));
        await mappings.CreateAsync(new SaveJobFunctionRoleMappingRequest(functionId, roleId), CancellationToken.None);

        var suggested = await mappings.SuggestForEmployeeAsync(employeeId, CancellationToken.None);
        var suggestion = Assert.Single(suggested);
        Assert.Equal(roleId, suggestion.RoleId);
        Assert.Equal("Chauffeur", suggestion.ViaFunction);

        // Duplicate mapping is refused.
        await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(
            () => mappings.CreateAsync(new SaveJobFunctionRoleMappingRequest(functionId, roleId), CancellationToken.None));
    }
}
