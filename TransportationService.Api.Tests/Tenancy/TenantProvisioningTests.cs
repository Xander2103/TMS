using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Messaging.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Provisioning;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Tenancy;

/// <summary>
/// B3: the explicit first-tenant provisioning command — exact rows, idempotency, no development
/// shortcuts or demo data, and the first administrator activated by e-mail without the
/// one-time link ever reaching the command output.
/// </summary>
public class TenantProvisioningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private const string BaseUrl = "https://tms.peeters.example";

    private static TenantProvisioningService Service(
        SqliteTestDbContext db, string environment = "Production", string? baseUrl = BaseUrl) =>
        new(db.Context, new PasswordHasher(), new TestClock(Now),
            new HostingEnvironment { EnvironmentName = environment },
            new ConfigurationBuilder()
                .AddInMemoryCollection(baseUrl is null ? [] : new Dictionary<string, string?> { ["Frontend:BaseUrl"] = baseUrl })
                .Build(),
            NullLoggerFactory.Instance);

    private static TenantProvisioningRequest Request(
        string slug = "transport-peeters", string email = "Jan@Peeters.example", bool resend = false) =>
        new("Transport Peeters BV", slug, email, "Jan", "Peeters",
            VatNumber: "BE 0123.456.749", ResendActivation: resend);

    [Fact]
    public async Task FreshDatabase_CreatesExactlyTheExpectedRows()
    {
        using var db = new SqliteTestDbContext();

        var result = await Service(db).ProvisionAsync(Request(), CancellationToken.None);

        Assert.True(result.TenantCreated);
        Assert.True(result.AdminCreated);
        var tenant = await db.Context.Tenants.SingleAsync();
        Assert.Equal(("transport-peeters", "Transport Peeters BV", true), (tenant.Slug, tenant.Name, tenant.IsActive));

        var settings = await db.Context.TenantSettings.SingleAsync();
        Assert.Equal(tenant.Id, settings.TenantId);
        Assert.Equal("Europe/Brussels", settings.Timezone);
        Assert.Equal("nl", settings.DefaultLanguage);
        Assert.Equal(("KL-", "ORD-", "FAC-", "MED-"),
            (settings.CustomerNumberPrefix, settings.OrderNumberPrefix, settings.InvoiceNumberPrefix, settings.EmployeeNumberPrefix));

        // System Administrator = the full catalogue; the 12 role templates at the current version.
        var adminRole = await db.Context.Roles.SingleAsync(r => r.IsSystemRole);
        Assert.Equal("Administrator", adminRole.Name);
        Assert.Equal(PermissionCodes.All.Count, await db.Context.RolePermissions.CountAsync(rp => rp.RoleId == adminRole.Id));
        Assert.Equal(DefaultRoleDefinitions.All.Count, await db.Context.Roles.CountAsync(r => r.TemplateCode != null));
        Assert.Equal(12, DefaultRoleDefinitions.All.Count);
        Assert.Equal(1 + DefaultRoleDefinitions.All.Count, await db.Context.Roles.CountAsync());
        Assert.Equal(DefaultRoleUpgrades.CurrentVersion,
            (await db.Context.RoleTemplateStates.SingleAsync(s => s.TenantId == tenant.Id)).AppliedVersion);

        // Exactly one user: the administrator, without a password, activation pending.
        var admin = await db.Context.Users.SingleAsync();
        Assert.Equal(result.AdminUserId, admin.Id);
        Assert.Equal("jan@peeters.example", admin.NormalizedEmail);
        Assert.Null(admin.PasswordHash);
        Assert.True(admin.MustChangePassword);
        Assert.Equal(adminRole.Id, (await db.Context.UserRoles.SingleAsync()).RoleId);
        var token = await db.Context.UserSecurityTokens.SingleAsync();
        Assert.Equal(UserSecurityTokenKind.Activation, token.Kind);
        Assert.Equal(Now.UtcDateTime.AddHours(72), token.ExpiresAt);

        // Legal entity from the given company data; reference data, policies and templates.
        var entity = await db.Context.LegalEntities.SingleAsync();
        Assert.True(entity.IsDefault);
        Assert.Equal(("Transport Peeters BV", "BE0123456749", "BE"), (entity.LegalName, entity.VatNumber, entity.CountryCode));
        Assert.Equal(8, await db.Context.ExpiryReminderPolicies.CountAsync());
        Assert.Equal(20, await db.Context.IssuedItemTemplates.CountAsync());
        Assert.Equal(QualificationTypeSeeder.Codes.Count, await db.Context.QualificationTypes.CountAsync());
        Assert.True(await db.Context.Departments.AnyAsync(d => d.TenantId == tenant.Id));
        Assert.True(await db.Context.Countries.AnyAsync());

        // One activation mail, queued for the administrator, carrying an absolute /activeren link.
        var mail = await db.Context.OutboxMessages.SingleAsync();
        Assert.Equal(MessageKinds.UserActivation, mail.Kind);
        Assert.Equal(OutboxStatus.Pending, mail.Status);
        Assert.Equal("Jan@Peeters.example", mail.RecipientAddress);
        Assert.Contains($"{BaseUrl}/activeren?token={result.Activation!.Token}", mail.Body);
        Assert.Contains("Transport Peeters BV", mail.Subject);
        Assert.DoesNotContain(result.Activation.Token, mail.IdempotencyKey);
    }

    [Fact]
    public async Task Provisioning_CreatesNoDevelopmentAdmin_AndNoDemoData()
    {
        using var db = new SqliteTestDbContext();

        await Service(db).ProvisionAsync(Request(), CancellationToken.None);

        Assert.False(await db.Context.Users.AnyAsync(u => u.NormalizedEmail == DevAdminSeeder.Email));
        Assert.False(await db.Context.Tenants.AnyAsync(t => t.Slug == "dev"));
        Assert.Equal(0, await db.Context.Customers.CountAsync());
        Assert.Equal(0, await db.Context.Employees.CountAsync());
        Assert.Equal(0, await db.Context.Drivers.CountAsync());
        Assert.Equal(0, await db.Context.Vehicles.CountAsync());
        Assert.Equal(0, await db.Context.Trailers.CountAsync());
        Assert.Equal(0, await db.Context.Locations.CountAsync());
        Assert.Equal(0, await db.Context.TransportOrders.CountAsync());
        Assert.Equal(0, await db.Context.Trips.CountAsync());
        Assert.Equal(0, await db.Context.Invoices.CountAsync());
        Assert.Single(await db.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task Rerun_WithTheSameSlug_DuplicatesNothing_AndReportsWhatExists()
    {
        using var db = new SqliteTestDbContext();
        await Service(db).ProvisionAsync(Request(), CancellationToken.None);
        var before = await SnapshotAsync(db.Context);

        var again = await Service(db).ProvisionAsync(Request(), CancellationToken.None);

        Assert.False(again.TenantCreated);
        Assert.False(again.AdminCreated);
        Assert.Null(again.Activation); // no new link without --resend-activation
        Assert.Contains(again.Report, line => line.Contains("bestaat al"));
        Assert.Contains(again.Report, line => line.Contains("--resend-activation"));
        Assert.Equal(before, await SnapshotAsync(db.Context));
    }

    [Fact]
    public async Task ResendActivation_RevokesTheOldLink_AndQueuesANewMail()
    {
        using var db = new SqliteTestDbContext();
        var first = await Service(db).ProvisionAsync(Request(), CancellationToken.None);

        var resent = await Service(db).ProvisionAsync(Request(resend: true), CancellationToken.None);

        Assert.NotNull(resent.Activation);
        Assert.NotEqual(first.Activation!.Token, resent.Activation!.Token);
        Assert.Equal(1, await db.Context.UserSecurityTokens.CountAsync(t => t.RevokedAt == null && t.UsedAt == null));
        Assert.Equal(2, await db.Context.OutboxMessages.CountAsync(m => m.Kind == MessageKinds.UserActivation));
        Assert.Single(await db.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task ResendActivation_ForAnActivatedAdmin_IsRefused()
    {
        using var db = new SqliteTestDbContext();
        await Service(db).ProvisionAsync(Request(), CancellationToken.None);
        var admin = await db.Context.Users.SingleAsync();
        admin.PasswordHash = new PasswordHasher().Hash("Al-Geactiveerd1!");
        await db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TenantProvisioningException>(
            () => Service(db).ProvisionAsync(Request(resend: true), CancellationToken.None));
        Assert.Contains("al geactiveerd", ex.Message);
    }

    [Fact]
    public async Task ASecondAdministrator_ForAnExistingTenant_IsRefused()
    {
        using var db = new SqliteTestDbContext();
        await Service(db).ProvisionAsync(Request(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<TenantProvisioningException>(
            () => Service(db).ProvisionAsync(Request(email: "iemand.anders@peeters.example"), CancellationToken.None));
        Assert.Contains("heeft al een beheerder", ex.Message);
        Assert.Single(await db.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task AnAdminEmail_ThatBelongsToAnotherTenant_IsRefused()
    {
        using var db = new SqliteTestDbContext();
        await Service(db).ProvisionAsync(Request(), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<TenantProvisioningException>(
            () => Service(db).ProvisionAsync(Request(slug: "tweede-bedrijf", email: "JAN@peeters.example"), CancellationToken.None));
        Assert.Contains("andere tenant", ex.Message);
        Assert.Single(await db.Context.Users.ToListAsync());
        // The transaction rolled the half-provisioned second tenant back.
        Assert.False(await db.Context.Tenants.AnyAsync(t => t.Slug == "tweede-bedrijf"));
    }

    [Fact]
    public async Task OutsideDevelopment_WithoutAnAbsoluteFrontendBaseUrl_NothingIsCreated()
    {
        using var db = new SqliteTestDbContext();

        var ex = await Assert.ThrowsAsync<TenantProvisioningException>(
            () => Service(db, baseUrl: null).ProvisionAsync(Request(), CancellationToken.None));

        Assert.Contains("Frontend:BaseUrl", ex.Message);
        Assert.Equal(0, await db.Context.Tenants.CountAsync());
    }

    [Theory]
    [InlineData("Hoofdletters")]
    [InlineData("met spatie")]
    [InlineData("-begint-met-streep")]
    public async Task InvalidSlug_IsRefused(string slug)
    {
        using var db = new SqliteTestDbContext();
        await Assert.ThrowsAsync<TenantProvisioningException>(
            () => Service(db).ProvisionAsync(Request(slug: slug), CancellationToken.None));
    }

    // --- Command output ---------------------------------------------------------------------

    [Fact]
    public async Task CommandOutput_NeverContainsTheActivationToken()
    {
        using var db = new SqliteTestDbContext();
        var output = new StringWriter();
        var errors = new StringWriter();

        var exitCode = await TenantProvisioningCommand.ExecuteAsync(
            Service(db), Request(), printActivationLink: false, output, errors);

        Assert.Equal(0, exitCode);
        var mailBody = (await db.Context.OutboxMessages.SingleAsync()).Body;
        var rawToken = ExtractToken(mailBody);
        var text = output + errors.ToString();
        Assert.DoesNotContain(rawToken, text);
        Assert.DoesNotContain("token=", text);
        Assert.DoesNotContain("/activeren", text);
        Assert.Contains("activatielink per e-mail", text);
        Assert.Contains((await db.Context.Users.SingleAsync()).Id.ToString(), text);
        Assert.Contains("outbox-bericht", text);
    }

    [Fact]
    public async Task CommandOutput_PrintsTheLink_OnlyWithTheExplicitFlag()
    {
        using var db = new SqliteTestDbContext();
        var output = new StringWriter();

        var exitCode = await TenantProvisioningCommand.ExecuteAsync(
            Service(db), Request(), printActivationLink: true, output, new StringWriter());

        Assert.Equal(0, exitCode);
        var rawToken = ExtractToken((await db.Context.OutboxMessages.SingleAsync()).Body);
        Assert.Contains($"{BaseUrl}/activeren?token={rawToken}", output.ToString());
        Assert.Contains("WAARSCHUWING", output.ToString());
    }

    [Fact]
    public async Task CommandRefusal_ReturnsNonZero_WithAReason()
    {
        using var db = new SqliteTestDbContext();
        await Service(db).ProvisionAsync(Request(), CancellationToken.None);
        var errors = new StringWriter();

        var exitCode = await TenantProvisioningCommand.ExecuteAsync(
            Service(db), Request(email: "nog.een@peeters.example"), false, new StringWriter(), errors);

        Assert.Equal(1, exitCode);
        Assert.Contains("Provisioning geweigerd", errors.ToString());
    }

    [Fact]
    public void Arguments_AreParsed_AndKeptOutOfTheHostConfiguration()
    {
        string[] args =
        [
            "--provision-tenant", "--name", "Transport Peeters BV", "--slug=transport-peeters",
            "--admin-email", "jan@peeters.example", "--vat", "BE0123456749", "--print-activation-link",
            "--environment", "Production",
        ];

        Assert.True(TenantProvisioningCommand.IsRequested(args));
        Assert.True(TenantProvisioningCommand.TryParse(args, out var request, out var print, out var error), error);
        Assert.True(print);
        Assert.Equal(("Transport Peeters BV", "transport-peeters", "jan@peeters.example", "BE0123456749"),
            (request!.Name, request.Slug, request.AdminEmail, request.VatNumber));
        Assert.False(request.ResendActivation);
        Assert.Equal(["--environment", "Production"], TenantProvisioningCommand.HostArguments(args));

        Assert.False(TenantProvisioningCommand.TryParse(["--provision-tenant", "--name", "X"], out _, out _, out var missing));
        Assert.Contains("--slug", missing);
        Assert.Equal(["--seed-demo"], TenantProvisioningCommand.HostArguments(["--seed-demo"]));
    }

    // --- Startup sync for existing tenants ----------------------------------------------------

    [Fact]
    public async Task StartupReferenceSync_FillsExistingTenants_WithoutUsersDemoDataOrPricedStarterLists()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Bestaand", Slug = "bestaand", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, CompanyLegalName = "Bestaand NV" });
        await db.Context.SaveChangesAsync();

        await TenantReferenceDataSync.SyncAsync(db.Context, NullLogger.Instance);
        var afterFirst = await SnapshotAsync(db.Context);
        await TenantReferenceDataSync.SyncAsync(db.Context, NullLogger.Instance);

        Assert.Equal(afterFirst, await SnapshotAsync(db.Context)); // idempotent
        Assert.Equal(QualificationTypeSeeder.Codes.Count, await db.Context.QualificationTypes.CountAsync());
        Assert.Equal(8, await db.Context.ExpiryReminderPolicies.CountAsync(p => p.TenantId == tenantId));
        Assert.True(await db.Context.Set<Modules.Reference.Entities.ContractType>().AnyAsync(c => c.Code == "FLEXI"));
        Assert.Equal("Bestaand NV", (await db.Context.LegalEntities.SingleAsync()).LegalName);
        // Never users, roles, demo records or priced/curated starter catalogues for a live tenant.
        Assert.Equal(0, await db.Context.Users.CountAsync());
        Assert.Equal(0, await db.Context.Customers.CountAsync());
        Assert.Equal(0, await db.Context.Employees.CountAsync());
        Assert.Equal(0, await db.Context.ServiceOptions.CountAsync());
        Assert.Equal(0, await db.Context.IssuedItemTemplates.CountAsync());
        Assert.Equal(0, await db.Context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task StartupReferenceSync_NeverRecreatesWhatATenantDeleted()
    {
        using var db = new SqliteTestDbContext();
        await Service(db).ProvisionAsync(Request(), CancellationToken.None);
        var flexi = await db.Context.Set<Modules.Reference.Entities.ContractType>().SingleAsync(c => c.Code == "FLEXI");
        db.Context.Remove(flexi); // soft delete via the interceptor
        foreach (var department in await db.Context.Departments.ToListAsync())
        {
            db.Context.Remove(department);
        }

        var policy = await db.Context.ExpiryReminderPolicies.FirstAsync();
        db.Context.Remove(policy);
        await db.Context.SaveChangesAsync();

        await TenantReferenceDataSync.SyncAsync(db.Context, NullLogger.Instance);

        Assert.False(await db.Context.Set<Modules.Reference.Entities.ContractType>().AnyAsync(c => c.Code == "FLEXI"));
        Assert.Equal(0, await db.Context.Departments.CountAsync());
        Assert.Equal(7, await db.Context.ExpiryReminderPolicies.CountAsync());
    }

    private static string ExtractToken(string body)
    {
        var start = body.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = body.IndexOf('&', start);
        return body[start..end];
    }

    private static async Task<string> SnapshotAsync(TransportationDbContext context) => string.Join("|",
        await context.Tenants.CountAsync(),
        await context.TenantSettings.CountAsync(),
        await context.Roles.CountAsync(),
        await context.RolePermissions.CountAsync(),
        await context.RoleTemplateStates.CountAsync(),
        await context.Users.CountAsync(),
        await context.UserRoles.CountAsync(),
        await context.UserSecurityTokens.CountAsync(),
        await context.LegalEntities.IgnoreQueryFilters().CountAsync(),
        await context.Departments.IgnoreQueryFilters().CountAsync(),
        await context.UnitTypes.IgnoreQueryFilters().CountAsync(),
        await context.Set<Modules.Reference.Entities.ContractType>().IgnoreQueryFilters().CountAsync(),
        await context.ServiceOptions.IgnoreQueryFilters().CountAsync(),
        await context.ExpiryReminderPolicies.IgnoreQueryFilters().CountAsync(),
        await context.IssuedItemTemplates.IgnoreQueryFilters().CountAsync(),
        await context.QualificationTypes.CountAsync(),
        await context.Countries.CountAsync(),
        await context.OutboxMessages.CountAsync());
}
