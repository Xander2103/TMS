using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers;
using TransportationService.Api.Modules.Dossiers.Dtos;
using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// PERF-B4 (performance/concurrency final remediation 2026-10-21): two edits of one dossier that both
/// passed the in-code version check used to BOTH update — the audit saw 10 of 10 races end 200/200
/// with one edit silently lost. <c>TransportDossier.Version</c> is an EF concurrency token now: the
/// second UPDATE matches no row, the loser gets the 409 rebase contract with the CURRENT state and
/// the winner's change survives. A caller that sends no version at all is protected the same way.
/// </summary>
public class DossierConcurrencyTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, CompetingWriterInterceptor Race, Guid TenantId, Guid UserId, Guid CustomerId)
    {
        public DossierService Sut(TransportationDbContext? context = null)
        {
            var db = context ?? Db.Context;
            var tenant = new DevTenantContext(TenantId);
            return new DossierService(db, tenant, new AuditService(db, tenant, new DevCurrentUserContext(UserId)), new TestClock(Now));
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
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "p@acme.be", FirstName = "Piet", LastName = "Planner", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Klant BV" });
        await db.Context.SaveChangesAsync();
        return new Harness(db, race, tenantId, userId, customerId);
    }

    private static async Task<DossierDetailDto> RaceOnAsync(Harness h, Guid? loserVersion)
    {
        var created = await h.Sut().CreateAsync(new SaveDossierRequest("Project", CustomerId: h.CustomerId), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        // Colleague B saves between A's version check and A's UPDATE — with the version A also holds.
        h.Race.Arm(
            context => context.ChangeTracker.Entries<TransportDossier>().Any(e => e.Entity.Id == created.Id && e.State == EntityState.Modified),
            async cancellationToken =>
            {
                await using var other = h.Db.CreateContextForTenant(h.TenantId);
                var winner = await h.Sut(other).UpdateAsync(created.Id,
                    new SaveDossierRequest("Titel van collega B", CustomerId: h.CustomerId, Version: created.Version), cancellationToken);
                Assert.Equal("Titel van collega B", winner!.Title);
            });

        var conflict = await Assert.ThrowsAsync<DossierVersionConflictException>(() => h.Sut().UpdateAsync(created.Id,
            new SaveDossierRequest("Titel van gebruiker A", CustomerId: h.CustomerId, Version: loserVersion), CancellationToken.None));

        Assert.Equal(1, h.Race.Fired);
        // The 409 body is the CURRENT state (rebase contract), not the loser's stale copy.
        Assert.Equal("Titel van collega B", conflict.Current.Title);
        Assert.NotEqual(created.Version, conflict.Current.Version);

        var stored = await h.Db.Context.TransportDossiers.AsNoTracking().SingleAsync(d => d.Id == created.Id);
        Assert.Equal("Titel van collega B", stored.Title);
        Assert.Equal(conflict.Current.Version, stored.Version);
        // The loser wrote nothing: one "Updated" audit row (the winner's), not two.
        Assert.Single(await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "TransportDossier" && a.EntityId == created.Id.ToString() && a.Action == "Updated")
            .ToListAsync());
        return created;
    }

    [Fact]
    public async Task ConcurrentEdit_ThatPassedTheVersionCheck_IsRefusedWith409_AndTheWinnerSurvives()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await RaceOnAsync(h, loserVersion: null);

        // The rebase works: with the current version the edit goes through.
        h.Db.Context.ChangeTracker.Clear();
        var current = (await h.Sut().GetAsync(created.Id, CancellationToken.None))!;
        var rebased = await h.Sut().UpdateAsync(created.Id,
            new SaveDossierRequest("Titel van gebruiker A", CustomerId: h.CustomerId, Version: current.Version), CancellationToken.None);
        Assert.Equal("Titel van gebruiker A", rebased!.Title);
    }

    [Fact]
    public async Task ConcurrentEdit_WithTheSameVersion_IsRefusedWith409()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut().CreateAsync(new SaveDossierRequest("Project", CustomerId: h.CustomerId), CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        h.Race.Arm(
            context => context.ChangeTracker.Entries<TransportDossier>().Any(e => e.Entity.Id == created.Id && e.State == EntityState.Modified),
            async cancellationToken =>
            {
                await using var other = h.Db.CreateContextForTenant(h.TenantId);
                await h.Sut(other).UpdateAsync(created.Id,
                    new SaveDossierRequest("Titel van collega B", CustomerId: h.CustomerId, Version: created.Version), cancellationToken);
            });

        // Both requests carried the SAME (then current) version: the code check passes for both,
        // the database token decides.
        var conflict = await Assert.ThrowsAsync<DossierVersionConflictException>(() => h.Sut().UpdateAsync(created.Id,
            new SaveDossierRequest("Titel van gebruiker A", CustomerId: h.CustomerId, Version: created.Version), CancellationToken.None));
        Assert.Equal("Titel van collega B", conflict.Current.Title);
    }
}
