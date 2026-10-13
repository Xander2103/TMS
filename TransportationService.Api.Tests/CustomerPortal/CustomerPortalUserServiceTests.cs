using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Entities;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

/// <summary>
/// Security-focused: every entry point is scoped exclusively by the CALLER's own linked
/// customer. A foreign customer's user must be indistinguishable from a non-existent one, an
/// unlinked (internal) caller must never manage portal users, and the resulting portal accounts
/// must never carry any permission outside the klantportaal_* template family.
/// </summary>
public class CustomerPortalUserServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>A stand-in for a live SMTP/SendGrid provider — anything that is NOT
    /// <see cref="DevelopmentSinkProvider"/> — used to prove the raw-token guard actually flips.</summary>
    private sealed class FakeLiveEmailProvider : IEmailProvider
    {
        public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record Harness(
        SqliteTestDbContext Db, TestClock Clock, Guid TenantId, Guid CustomerId, Guid OtherCustomerId,
        Guid CallerUserId, Guid OtherCustomerCallerUserId, Guid UnlinkedUserId)
    {
        public CustomerPortalUserService For(Guid callerUserId, IEmailProvider? emailProvider = null)
        {
            var tenant = new DevTenantContext(TenantId);
            var currentUser = new DevCurrentUserContext(callerUserId);
            var audit = new AuditService(Db.Context, tenant, currentUser);
            var accountFlows = new UserAccountFlowService(
                Db.Context, tenant, new PasswordHasher(), audit, Clock, new TestHostEnvironment());
            var outbox = new MessageOutboxService(Db.Context, tenant, Clock);
            var configuration = new ConfigurationBuilder().Build();
            var provider = emailProvider
                ?? new DevelopmentSinkProvider(Path.Combine(Path.GetTempPath(), "portal-user-tests-" + Guid.NewGuid().ToString("N")));
            return new CustomerPortalUserService(
                Db.Context, tenant, currentUser, accountFlows, outbox, provider, audit, Clock, configuration);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var callerUserId = Guid.NewGuid();
        var otherCallerUserId = Guid.NewGuid();
        var unlinkedUserId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = callerUserId, TenantId = tenantId, Email = "beheer@haven.be", FirstName = "Bea", LastName = "Heer", CustomerId = customerId, IsActive = true },
            new User { Id = otherCallerUserId, TenantId = tenantId, Email = "beheer@andere.be", FirstName = "An", LastName = "Der", CustomerId = otherCustomerId, IsActive = true },
            new User { Id = unlinkedUserId, TenantId = tenantId, Email = "los@acme.be", FirstName = "Los", LastName = "Zonder", IsActive = true });
        await db.Context.SaveChangesAsync();

        // Realistic role/permission catalog — including the four klantportaal_* templates —
        // exactly as production startup seeds it.
        await PermissionCatalogSeeder.SyncAsync(db.Context);
        await DefaultRoleSeeder.SyncAsync(db.Context);

        return new Harness(db, new TestClock(Now), tenantId, customerId, otherCustomerId, callerUserId, otherCallerUserId, unlinkedUserId);
    }

    private static PortalInviteUserRequest Invite(string email, PortalUserGrantsDto? grants = null) =>
        new("Nieuwe", "Klant", email, grants ?? new PortalUserGrantsDto(false, false, false));

    [Fact]
    public async Task Invite_CreatesUser_ScopedToCallersOwnCustomer_WithBaseRoleAndOutboxMail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.CallerUserId).InviteAsync(Invite("nieuw@haven.be"), CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.ActivationToken));

        // There is no client-suppliable customer id anywhere on the request — the server can
        // ONLY ever create the new user under the CALLER's own linked customer.
        var stored = h.Db.Context.Users.Single(u => u.Email == "nieuw@haven.be");
        Assert.Equal(h.CustomerId, stored.CustomerId);
        Assert.Equal(h.TenantId, stored.TenantId);
        Assert.True(stored.MustChangePassword);

        var roleNames = h.Db.Context.UserRoles.Where(ur => ur.UserId == stored.Id)
            .Join(h.Db.Context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.TemplateCode).ToList();
        Assert.Contains("klantportaal", roleNames);
        Assert.DoesNotContain("klantportaal_documenten", roleNames);

        var mail = h.Db.Context.OutboxMessages.Single(m => m.Kind == MessageKinds.PortalUserInvited);
        Assert.Equal("nieuw@haven.be", mail.RecipientAddress);
        Assert.Contains("/activeren", mail.Body);
    }

    [Fact]
    public async Task Invite_IdempotencyKey_ContainsNoRawTokenMaterial()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.CallerUserId).InviteAsync(Invite("hygiene@haven.be"), CancellationToken.None);
        var token = result.Value!.ActivationToken!;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Unique per issuance, but via a one-way reference — no window of the raw token may
        // appear in the durable key (C3).
        var mail = h.Db.Context.OutboxMessages.Single(m => m.Kind == MessageKinds.PortalUserInvited);
        for (var i = 0; i + 12 <= token.Length; i++)
        {
            Assert.DoesNotContain(token.Substring(i, 12), mail.IdempotencyKey);
        }
    }

    [Fact]
    public async Task Invite_WithALiveEmailProviderRegistered_NeverReturnsTheRawToken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.CallerUserId, new FakeLiveEmailProvider())
            .InviteAsync(Invite("live-provider@haven.be"), CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, result.Outcome);
        Assert.Null(result.Value!.ActivationToken);
        // The token itself still exists server-side (activation still works) — only the API
        // response withholds it.
        var stored = h.Db.Context.Users.Single(u => u.Email == "live-provider@haven.be");
        Assert.True(h.Db.Context.UserSecurityTokens.Any(t =>
            t.UserId == stored.Id && t.Kind == UserSecurityTokenKind.Activation && t.UsedAt == null && t.RevokedAt == null));
    }

    [Fact]
    public async Task ResendInvite_WithALiveEmailProviderRegistered_NeverReturnsTheRawToken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Invited under the dev sink (so the initial token IS returned)...
        await h.For(h.CallerUserId).InviteAsync(Invite("resend-live@haven.be"), CancellationToken.None);
        var target = h.Db.Context.Users.Single(u => u.Email == "resend-live@haven.be");

        // ...but a resend under a live provider must withhold the new one too.
        var result = await h.For(h.CallerUserId, new FakeLiveEmailProvider())
            .ResendInviteAsync(target.Id, CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, result.Outcome);
        Assert.Null(result.Value!.ActivationToken);
    }

    [Fact]
    public async Task Invite_WithGrants_AssignsOnlyTheRequestedOptionalTemplates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.CallerUserId).InviteAsync(
            Invite("doc-fact@haven.be", new PortalUserGrantsDto(Documents: true, Invoices: true, ManageUsers: false)),
            CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, result.Outcome);
        var grants = result.Value!.User.Grants;
        Assert.True(grants.Documents);
        Assert.True(grants.Invoices);
        Assert.False(grants.ManageUsers);
    }

    [Fact]
    public async Task Invite_DuplicateEmailWithinTenant_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.CallerUserId);
        await sut.InviteAsync(Invite("dup@haven.be"), CancellationToken.None);

        var second = await sut.InviteAsync(Invite("dup@haven.be"), CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.ValidationFailed, second.Outcome);
        Assert.Equal(1, h.Db.Context.Users.Count(u => u.Email == "dup@haven.be"));
    }

    [Fact]
    public async Task Invite_ByUnlinkedInternalCaller_IsRejected_NoUserCreated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.For(h.UnlinkedUserId).InviteAsync(Invite("moetnietbestaan@haven.be"), CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.ValidationFailed, result.Outcome);
        Assert.False(h.Db.Context.Users.Any(u => u.Email == "moetnietbestaan@haven.be"));
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnCustomersPortalUsers_NeverForeignOrInternal()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.CallerUserId).InviteAsync(Invite("eigen@haven.be"), CancellationToken.None);
        await h.For(h.OtherCustomerCallerUserId).InviteAsync(Invite("vreemd@andere.be"), CancellationToken.None);

        var list = await h.For(h.CallerUserId).ListAsync(CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.Success, list.Outcome);
        var emails = list.Value!.Select(u => u.Email).ToList();
        Assert.Contains("eigen@haven.be", emails);
        Assert.Contains("beheer@haven.be", emails); // the caller itself belongs to its own customer
        Assert.DoesNotContain("vreemd@andere.be", emails);
        Assert.DoesNotContain("beheer@andere.be", emails);
        Assert.DoesNotContain("los@acme.be", emails); // internal, unlinked account
    }

    [Fact]
    public async Task List_ByUnlinkedCaller_ReturnsNoCustomerLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var list = await h.For(h.UnlinkedUserId).ListAsync(CancellationToken.None);

        Assert.Equal(PortalOutcomeKind.NoCustomerLink, list.Outcome);
    }

    [Fact]
    public async Task Deactivate_ForeignCustomersUser_ReturnsNotFound_AndLeavesItUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.OtherCustomerCallerUserId).InviteAsync(Invite("target@andere.be"), CancellationToken.None);
        var targetId = h.Db.Context.Users.Single(u => u.Email == "target@andere.be").Id;

        var result = await h.For(h.CallerUserId).DeactivateAsync(targetId, CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.NotFound, result.Outcome);
        Assert.True(h.Db.Context.Users.Single(u => u.Id == targetId).IsActive);
    }

    [Fact]
    public async Task Deactivate_OwnUser_SetsInactive_AndRevokesEveryRefreshToken()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.CallerUserId).InviteAsync(Invite("actief@haven.be"), CancellationToken.None);
        var target = h.Db.Context.Users.Single(u => u.Email == "actief@haven.be");
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = target.Id, TenantId = h.TenantId, TokenHash = "hash-1",
            CreatedAt = Now.UtcDateTime, ExpiresAt = Now.UtcDateTime.AddDays(14),
        };
        h.Db.Context.Add(refreshToken);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.For(h.CallerUserId).DeactivateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, result.Outcome);
        Assert.False(h.Db.Context.Users.Single(u => u.Id == target.Id).IsActive);
        var storedToken = h.Db.Context.Set<RefreshToken>().Single(t => t.Id == refreshToken.Id);
        Assert.NotNull(storedToken.RevokedAt);
    }

    [Fact]
    public async Task Reactivate_OwnUser_SetsActiveAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.CallerUserId).InviteAsync(Invite("heractiveer@haven.be"), CancellationToken.None);
        var target = h.Db.Context.Users.Single(u => u.Email == "heractiveer@haven.be");
        await h.For(h.CallerUserId).DeactivateAsync(target.Id, CancellationToken.None);

        var result = await h.For(h.CallerUserId).ReactivateAsync(target.Id, CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, result.Outcome);
        Assert.True(h.Db.Context.Users.Single(u => u.Id == target.Id).IsActive);
    }

    [Fact]
    public async Task ResendInvite_InvalidatesThePreviousToken_AndIssuesADifferentOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.For(h.CallerUserId).InviteAsync(Invite("opnieuw@haven.be"), CancellationToken.None);
        var target = h.Db.Context.Users.Single(u => u.Email == "opnieuw@haven.be");
        var firstTokenHash = h.Db.Context.UserSecurityTokens
            .Single(t => t.UserId == target.Id && t.Kind == UserSecurityTokenKind.Activation).TokenHash;

        var second = await h.For(h.CallerUserId).ResendInviteAsync(target.Id, CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.Success, second.Outcome);
        Assert.NotEqual(first.Value!.ActivationToken, second.Value!.ActivationToken);
        var oldToken = h.Db.Context.UserSecurityTokens.Single(t => t.TokenHash == firstTokenHash);
        Assert.NotNull(oldToken.RevokedAt);
        var openTokens = h.Db.Context.UserSecurityTokens
            .Count(t => t.UserId == target.Id && t.Kind == UserSecurityTokenKind.Activation
                && t.UsedAt == null && t.RevokedAt == null);
        Assert.Equal(1, openTokens);
    }

    [Fact]
    public async Task ResendInvite_ForeignCustomersUser_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.OtherCustomerCallerUserId).InviteAsync(Invite("vreemd2@andere.be"), CancellationToken.None);
        var targetId = h.Db.Context.Users.Single(u => u.Email == "vreemd2@andere.be").Id;

        var result = await h.For(h.CallerUserId).ResendInviteAsync(targetId, CancellationToken.None);

        Assert.Equal(PortalUserOperationOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task SetGrants_TogglesOptionalRoles_AndNeverTouchesAForeignCustomersUser()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.For(h.CallerUserId).InviteAsync(Invite("grants@haven.be"), CancellationToken.None);
        var target = h.Db.Context.Users.Single(u => u.Email == "grants@haven.be");

        var granted = await h.For(h.CallerUserId).SetGrantsAsync(
            target.Id, new PortalUserGrantsDto(true, true, true), CancellationToken.None);
        Assert.Equal(PortalUserOperationOutcome.Success, granted.Outcome);
        Assert.True(granted.Value!.Grants.Documents);
        Assert.True(granted.Value.Grants.Invoices);
        Assert.True(granted.Value.Grants.ManageUsers);

        var revoked = await h.For(h.CallerUserId).SetGrantsAsync(
            target.Id, new PortalUserGrantsDto(false, false, false), CancellationToken.None);
        Assert.False(revoked.Value!.Grants.Documents);
        Assert.False(revoked.Value.Grants.Invoices);
        Assert.False(revoked.Value.Grants.ManageUsers);

        // A caller can never adjust grants for a user outside their own customer.
        var foreignTarget = (await h.For(h.OtherCustomerCallerUserId)
            .InviteAsync(Invite("vreemd3@andere.be"), CancellationToken.None)).Value!.User.Id;
        var foreignAttempt = await h.For(h.CallerUserId).SetGrantsAsync(
            foreignTarget, new PortalUserGrantsDto(true, true, true), CancellationToken.None);
        Assert.Equal(PortalUserOperationOutcome.NotFound, foreignAttempt.Outcome);
    }

    [Fact]
    public async Task InvitedPortalUser_EffectivePermissionSet_ContainsOnlyGrantedPortalPermissions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var invited = await h.For(h.CallerUserId).InviteAsync(
            Invite("volledig@haven.be", new PortalUserGrantsDto(true, true, true)), CancellationToken.None);
        var targetId = invited.Value!.User.Id;

        var roleIds = h.Db.Context.UserRoles.Where(ur => ur.UserId == targetId).Select(ur => ur.RoleId).ToList();
        var permissionCodes = h.Db.Context.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId))
            .Join(h.Db.Context.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .Distinct()
            .ToList();

        Assert.NotEmpty(permissionCodes);
        // No permission beyond the customer_portal.* family ever reaches a portal user, no
        // matter how many optional grant templates are attached.
        Assert.All(permissionCodes, code => Assert.StartsWith("customer_portal.", code));
        Assert.Contains("customer_portal.view", permissionCodes);
        Assert.Contains("customer_portal.messages", permissionCodes);
        Assert.Contains("customer_portal.view_documents", permissionCodes);
        Assert.Contains("customer_portal.view_invoices", permissionCodes);
        Assert.Contains("customer_portal.manage_users", permissionCodes);
    }
}
