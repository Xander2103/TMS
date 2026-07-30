using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

public class PortalAnnouncementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, PortalAnnouncementService Sut, TestClock Clock);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var sut = new PortalAnnouncementService(db.Context, tenant, new AuditService(db.Context, tenant, user), clock);
        return new Harness(db, tenantId, sut, clock);
    }

    [Fact]
    public async Task ListActive_ExcludesInactive_AndOutOfWindow_IncludesInWindow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var alwaysOn = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Altijd zichtbaar", "Body", null, null, true), CancellationToken.None);
        var inactive = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Inactief", "Body", null, null, false), CancellationToken.None);
        var future = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Toekomst", "Body", Now.UtcDateTime.AddDays(1), null, true), CancellationToken.None);
        var expired = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Verlopen", "Body", null, Now.UtcDateTime.AddDays(-1), true), CancellationToken.None);
        var currentWindow = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest(
                "In venster", "Body", Now.UtcDateTime.AddHours(-1), Now.UtcDateTime.AddHours(1), true), CancellationToken.None);
        // Boundary: ActiveUntil exactly now should still be included (>=).
        var boundary = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Grens", "Body", null, Now.UtcDateTime, true), CancellationToken.None);

        var active = await h.Sut.ListActiveAsync(CancellationToken.None);
        var activeIds = active.Select(a => a.Id).ToHashSet();

        Assert.Contains(alwaysOn!.Id, activeIds);
        Assert.Contains(currentWindow!.Id, activeIds);
        Assert.Contains(boundary!.Id, activeIds);
        Assert.DoesNotContain(inactive!.Id, activeIds);
        Assert.DoesNotContain(future!.Id, activeIds);
        Assert.DoesNotContain(expired!.Id, activeIds);

        var all = await h.Sut.ListAllAsync(CancellationToken.None);
        Assert.Equal(6, all.Count);
    }

    [Fact]
    public async Task Update_And_Delete_RoundTrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Titel", "Body", null, null, true), CancellationToken.None);
        var updated = await h.Sut.UpdateAsync(created!.Id,
            new SavePortalAnnouncementRequest("Nieuwe titel", "Nieuwe body", null, null, false), CancellationToken.None);
        Assert.Equal("Nieuwe titel", updated!.Title);
        Assert.False(updated.IsActive);

        Assert.True(await h.Sut.DeleteAsync(created.Id, CancellationToken.None));
        Assert.False(await h.Sut.DeleteAsync(created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Create_InvalidWindow_Throws()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Titel", "Body", Now.UtcDateTime, Now.UtcDateTime.AddDays(-1), true),
            CancellationToken.None));
    }

    /// <summary>Fix round 1 (Important #2): over-length Title/Body must fail as a clean
    /// validation error, never reach SaveChanges and hit the varchar column as an unhandled 500.</summary>
    [Fact]
    public async Task Create_OverLengthTitle_ThrowsWithFieldPath()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var tooLong = new string('x', 201);
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest(tooLong, "Body", null, null, true), CancellationToken.None));
        Assert.Contains("title", exception.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_OverLengthBody_ThrowsWithFieldPath()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var tooLong = new string('x', 4001);
        var exception = await Assert.ThrowsAsync<DomainValidationException>(() => h.Sut.CreateAsync(
            new SavePortalAnnouncementRequest("Titel", tooLong, null, null, true), CancellationToken.None));
        Assert.Contains("body", exception.FieldErrors!.Keys);
    }
}
