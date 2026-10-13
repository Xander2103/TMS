using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Controllers;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Identity;

/// <summary>
/// Persoonlijke weergavevoorkeur (licht/donker/systeem): één catalogus, een self-scoped en
/// tenant-veilig endpoint op de eigen User-rij, en strikte isolatie tussen gebruikers — de
/// keuze van collega A mag nooit het scherm van collega B (zelfde tenant) veranderen.
/// </summary>
public class ThemePreferenceTests
{
    [Fact]
    public void ThemePreferences_NormalizeAndValidate()
    {
        Assert.Equal(["system", "light", "dark"], ThemePreferences.All);
        Assert.Equal("dark", ThemePreferences.Normalize(" DARK "));
        Assert.Equal("system", ThemePreferences.Normalize("sepia"));
        Assert.Equal("system", ThemePreferences.Normalize(null));
        Assert.Null(ThemePreferences.NormalizeOrNull("auto"));
        Assert.Equal("light", ThemePreferences.NormalizeOrNull("Light"));
        Assert.True(ThemePreferences.IsSupported("system"));
        Assert.False(ThemePreferences.IsSupported("black"));
    }

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId, Guid ColleagueId)
    {
        public MyThemeController Controller(Guid? userOverride = null, Guid? tenantOverride = null)
        {
            var tenant = new DevTenantContext(tenantOverride ?? TenantId);
            var current = new DevCurrentUserContext(userOverride ?? UserId);
            return new MyThemeController(Db.Context, current, tenant, new AuditService(Db.Context, tenant, current));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var colleagueId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "jan@acme.test", FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        db.Context.Users.Add(new User
        {
            Id = colleagueId, TenantId = tenantId, Email = "els@acme.test", FirstName = "Els", LastName = "Claes", IsActive = true,
            ThemePreference = "light",
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId, colleagueId);
    }

    [Fact]
    public async Task SetMyTheme_PersistsNormalized_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Controller().Set(new MyThemeController.SetMyThemeRequest(" Dark "), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("dark", Assert.IsType<MyThemeController.MyThemeDto>(ok.Value).Theme);
        Assert.Equal("dark", h.Db.Context.Users.Single(u => u.Id == h.UserId).ThemePreference);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "User" && a.Action == "ThemeChanged");
    }

    [Fact]
    public async Task SetMyTheme_IsIsolatedPerUser_WithinTheSameTenant()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Controller().Set(new MyThemeController.SetMyThemeRequest("dark"), CancellationToken.None);

        // Collega in dezelfde tenant houdt haar eigen keuze: het thema is geen tenantinstelling.
        Assert.Equal("dark", h.Db.Context.Users.Single(u => u.Id == h.UserId).ThemePreference);
        Assert.Equal("light", h.Db.Context.Users.Single(u => u.Id == h.ColleagueId).ThemePreference);
    }

    [Fact]
    public async Task SetMyTheme_RejectsUnsupported_WithStableCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Controller().Set(new MyThemeController.SetMyThemeRequest("sepia"), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("common.unsupported_theme", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        Assert.Null(h.Db.Context.Users.Single(u => u.Id == h.UserId).ThemePreference);
    }

    [Fact]
    public async Task SetMyTheme_IsTenantSafe_AndSelfScopedOnly()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Zelfde user-id maar andere tenantcontext ⇒ geen rij zichtbaar ⇒ geen schrijfactie.
        var result = await h.Controller(tenantOverride: Guid.NewGuid())
            .Set(new MyThemeController.SetMyThemeRequest("dark"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Null(h.Db.Context.Users.Single(u => u.Id == h.UserId).ThemePreference);
    }

    [Fact]
    public async Task SetMyTheme_SameValueTwice_DoesNotAuditAgain()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await h.Controller().Set(new MyThemeController.SetMyThemeRequest("light"), CancellationToken.None);
        await h.Controller().Set(new MyThemeController.SetMyThemeRequest("light"), CancellationToken.None);

        Assert.Single(h.Db.Context.AuditLogs, a => a.EntityType == "User" && a.Action == "ThemeChanged");
    }
}
