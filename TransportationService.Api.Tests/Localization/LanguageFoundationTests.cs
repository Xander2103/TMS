using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Attendance.Controllers;
using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Controllers;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Localization;

/// <summary>
/// Fundament van de i18n-wave: één taalcatalogus (nl/fr/en), een self-scoped en
/// tenant-veilig intern taalendpoint, en stabiele machineleesbare foutcodes naast de
/// bestaande Nederlandse teksten (taal geeft nooit rechten, §61/§79).
/// </summary>
public class LanguageFoundationTests
{
    [Fact]
    public void SupportedLanguages_NormalizeAndValidate()
    {
        Assert.Equal(["nl", "fr", "en"], SupportedLanguages.All);
        Assert.Equal("fr", SupportedLanguages.Normalize(" FR "));
        Assert.Equal("nl", SupportedLanguages.Normalize("de"));
        Assert.Equal("nl", SupportedLanguages.Normalize(null));
        Assert.Null(SupportedLanguages.NormalizeOrNull("xx"));
        Assert.Equal("en", SupportedLanguages.NormalizeOrNull("EN"));
        Assert.True(SupportedLanguages.IsSupported("nl"));
        Assert.False(SupportedLanguages.IsSupported("es"));
    }

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid UserId)
    {
        public MyLanguageController Controller(Guid? userOverride = null, Guid? tenantOverride = null)
        {
            var tenant = new DevTenantContext(tenantOverride ?? TenantId);
            return new MyLanguageController(Db.Context, new DevCurrentUserContext(userOverride ?? UserId), tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(userOverride ?? UserId)));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "jan@acme.test", FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, userId);
    }

    [Fact]
    public async Task SetMyLanguage_PersistsNormalized_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Controller().Set(new MyLanguageController.SetMyLanguageRequest(" FR "), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("fr", h.Db.Context.Users.Single(u => u.Id == h.UserId).PreferredLanguageCode);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "User" && a.Action == "LanguageChanged");
    }

    [Fact]
    public async Task SetMyLanguage_RejectsUnsupported_WithStableCode()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Controller().Set(new MyLanguageController.SetMyLanguageRequest("de"), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("common.unsupported_language", System.Text.Json.JsonSerializer.Serialize(bad.Value));
        Assert.Null(h.Db.Context.Users.Single(u => u.Id == h.UserId).PreferredLanguageCode);
    }

    [Fact]
    public async Task SetMyLanguage_IsTenantSafe_AndSelfScopedOnly()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Zelfde user-id maar andere tenantcontext ⇒ geen rij zichtbaar ⇒ geen schrijfactie.
        var result = await h.Controller(tenantOverride: Guid.NewGuid())
            .Set(new MyLanguageController.SetMyLanguageRequest("fr"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Null(h.Db.Context.Users.Single(u => u.Id == h.UserId).PreferredLanguageCode);
    }

    [Fact]
    public void PunchOutcomes_MapToStableErrorCodes()
    {
        Assert.Equal("attendance.already_clocked_in",
            MyAttendanceController.PunchErrorCode(AttendancePunchOutcome.AlreadyClockedIn));
        Assert.Equal("attendance.not_clocked_in",
            MyAttendanceController.PunchErrorCode(AttendancePunchOutcome.NotClockedIn));
        Assert.Equal("attendance.break_already_active",
            MyAttendanceController.PunchErrorCode(AttendancePunchOutcome.BreakAlreadyActive));
        Assert.Equal("attendance.no_active_break",
            MyAttendanceController.PunchErrorCode(AttendancePunchOutcome.NoActiveBreak));
    }
}
