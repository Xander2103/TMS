using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Incidents.Dtos;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Incidents.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Incidents;

/// <summary>
/// Performance/concurrency final remediation 2026-10-21, concurrency matrix #10: two simultaneous edits
/// of the SAME incident section both answered 200 and the second silently overwrote the first (PERF-M5).
/// <c>Incident.Version</c> is an EF concurrency token now: the second UPDATE matches no row and the
/// loser gets a <see cref="DbUpdateConcurrencyException"/> (409 through the shared filter), while the
/// winner's edit survives. Section-by-section merging of NON-simultaneous edits (EX-01) is unchanged.
/// </summary>
public class IncidentConcurrencyTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, CompetingWriterInterceptor Race, Guid TenantId, Guid UserId, Guid CustomerId)
    {
        public IncidentService Sut(TransportationDbContext? context = null)
        {
            var db = context ?? Db.Context;
            var tenant = new DevTenantContext(TenantId);
            var user = new DevCurrentUserContext(UserId);
            var clock = new TestClock(Now);
            return new IncidentService(db, tenant, new AuditService(db, tenant, user), new NotificationService(db, tenant, user, clock), clock);
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var race = new CompetingWriterInterceptor();
        var db = new SqliteTestDbContext(null, race);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "d@acme.be", FirstName = "Dirk", LastName = "Dispatcher", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        await db.Context.SaveChangesAsync();
        return new Harness(db, race, tenantId, userId, customerId);
    }

    private static UpdateIncidentRequest Report(string title) => new(
        Report: new IncidentReportSection(title, "Bij het lossen is een pallet omgevallen.", "Damage", "High"));

    [Fact]
    public async Task SimultaneousEditsOfTheSameSection_SecondSaveIsRefused_WinnerSurvives()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut().CreateAsync(new SaveIncidentRequest("Pallet beschadigd", "Bij het lossen is een pallet omgevallen.", "Damage", "High"), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var versionBefore = (await h.Db.Context.Incidents.AsNoTracking().SingleAsync(i => i.Id == created.Id)).Version;

        // Colleague B saves the same section between A's read and A's UPDATE.
        h.Race.Arm(
            context => context.ChangeTracker.Entries<Incident>().Any(e => e.Entity.Id == created.Id && e.State == EntityState.Modified),
            async cancellationToken =>
            {
                await using var other = h.Db.CreateContextForTenant(h.TenantId);
                var winner = await h.Sut(other).UpdateAsync(created.Id, Report("Titel van collega B"), cancellationToken);
                Assert.Equal("Titel van collega B", winner!.Title);
            });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => h.Sut().UpdateAsync(created.Id, Report("Titel van gebruiker A"), CancellationToken.None));
        Assert.Equal(1, h.Race.Fired);

        var stored = await h.Db.Context.Incidents.AsNoTracking().SingleAsync(i => i.Id == created.Id);
        Assert.Equal("Titel van collega B", stored.Title);
        Assert.NotEqual(versionBefore, stored.Version);
        // Exactly one "Updated" audit row: the loser never got to write history.
        var updates = await h.Db.Context.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == created.Id.ToString() && a.Action == "Updated");
        Assert.Equal(1, updates);
    }

    [Fact]
    public async Task SequentialEditsOfDifferentSections_StillMerge()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut().CreateAsync(new SaveIncidentRequest("Pallet beschadigd", "Bij het lossen is een pallet omgevallen.", "Damage", "High"), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        await h.Sut().UpdateAsync(created.Id, Report("Rapport aangepast"), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();
        var merged = await h.Sut().UpdateAsync(created.Id,
            new UpdateIncidentRequest(Impact: new IncidentImpactSection("Klant wacht", null, null, null, null)), CancellationToken.None);

        Assert.Equal("Rapport aangepast", merged!.Title);
        Assert.Equal("Klant wacht", merged.CustomerImpact);
    }
}
