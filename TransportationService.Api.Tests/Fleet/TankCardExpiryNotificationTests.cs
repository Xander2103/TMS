using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

/// <summary>
/// HR maturity wave, task 8: staged tank-card expiry reminders (90/30/7 days) via
/// <see cref="ExpiryNotificationProducer"/>. Mirrors the harness in
/// Notifications/NotificationExpansionTests.cs (fleet-document expiry branch).
/// </summary>
public class TankCardExpiryNotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 06, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid Viewer);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var viewer = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = viewer, TenantId = tenantId, Email = "vloot@acme.be", PasswordHash = "x",
            FirstName = "Vloot", LastName = "Beheer", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });

        // The viewer holds tank_cards.view, so tank-card expiry events reach them.
        var roleId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        db.Context.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Vloot", IsActive = true });
        db.Context.Permissions.Add(new Permission
        {
            Id = permissionId, Code = "tank_cards.view", Module = "tank_cards", Action = "view",
        });
        db.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        db.Context.UserRoles.Add(new UserRole { UserId = viewer, RoleId = roleId });

        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, viewer);
    }

    private static TankCard MakeCard(Guid tenantId, int daysUntilExpiry, bool isBlocked = false, string? internalName = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        CardNumber = "1234 5678 9012 3456",
        Provider = "DKV",
        InternalName = internalName,
        ValidUntil = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(daysUntilExpiry),
        IsBlocked = isBlocked,
    };

    [Fact]
    public async Task Stage90_FiresAlone_AndDedupesOnRerun()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var card = MakeCard(h.TenantId, daysUntilExpiry: 89);
        h.Db.Context.TankCards.Add(card);
        await h.Db.Context.SaveChangesAsync();

        var producer = new ExpiryNotificationProducer(h.Db.Context, new TestClock(Now));
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        var notifications = h.Db.Context.Notifications.Where(n => n.Type == MessageKinds.TankCardExpiry).ToList();
        Assert.Single(notifications);
        Assert.Equal("Tankkaart vervalt binnenkort", notifications[0].Title);
        Assert.Contains(card.ValidUntil!.Value.ToString("dd-MM-yyyy"), notifications[0].Message);
        var logs = h.Db.Context.ReminderDispatchLogs.Where(l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:")).ToList();
        Assert.Single(logs);
        Assert.Equal($"tankcard_expiry:{card.Id}:90", logs[0].DedupeKey);

        // Re-running the sweep on the same day must not duplicate the stage-90 notification.
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        Assert.Equal(1, h.Db.Context.Notifications.Count(n => n.Type == MessageKinds.TankCardExpiry));
        Assert.Equal(1, h.Db.Context.ReminderDispatchLogs.Count(l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:")));
    }

    [Fact]
    public async Task CardEnteringAtSixDays_ClaimsAllThreeStages_ButSendsOnlyTheTightest()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var card = MakeCard(h.TenantId, daysUntilExpiry: 6, internalName: "Vrachtwagen 3 - reserve");
        h.Db.Context.TankCards.Add(card);
        await h.Db.Context.SaveChangesAsync();

        var producer = new ExpiryNotificationProducer(h.Db.Context, new TestClock(Now));
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        // Exactly one notification (the most urgent, 1-week stage) even though 90/30/7 were all
        // simultaneously due on first sight of this card — the quieter UX choice for task 8.
        var notifications = h.Db.Context.Notifications.Where(n => n.Type == MessageKinds.TankCardExpiry).ToList();
        Assert.Single(notifications);
        Assert.Contains("Vrachtwagen 3 - reserve", notifications[0].Message);

        // All three stage keys are claimed/logged so none of them fire again later.
        var logs = h.Db.Context.ReminderDispatchLogs.Where(l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:")).ToList();
        Assert.Equal(3, logs.Count);
        Assert.Contains($"tankcard_expiry:{card.Id}:90", logs.Select(l => l.DedupeKey));
        Assert.Contains($"tankcard_expiry:{card.Id}:30", logs.Select(l => l.DedupeKey));
        Assert.Contains($"tankcard_expiry:{card.Id}:7", logs.Select(l => l.DedupeKey));

        // Re-running produces nothing new: every stage for this card is already claimed.
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        Assert.Equal(1, h.Db.Context.Notifications.Count(n => n.Type == MessageKinds.TankCardExpiry));
        Assert.Equal(3, h.Db.Context.ReminderDispatchLogs.Count(l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:")));
    }

    [Fact]
    public async Task StagePromotion_NarrowerStageFiresLater_WithoutReclaimingTheWiderOne()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var card = MakeCard(h.TenantId, daysUntilExpiry: 89);
        h.Db.Context.TankCards.Add(card);
        await h.Db.Context.SaveChangesAsync();

        var clock = new TestClock(Now);
        var producer = new ExpiryNotificationProducer(h.Db.Context, clock);
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        var afterFirstRun = h.Db.Context.Notifications.Where(n => n.Type == MessageKinds.TankCardExpiry).ToList();
        Assert.Single(afterFirstRun);
        Assert.Contains("3 maanden", afterFirstRun[0].Message);
        var logsAfterFirstRun = h.Db.Context.ReminderDispatchLogs
            .Where(l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:")).ToList();
        Assert.Single(logsAfterFirstRun);
        Assert.Equal($"tankcard_expiry:{card.Id}:90", logsAfterFirstRun[0].DedupeKey);

        // Advance 60 days: the card is now 29 days from expiry, so stage 30 newly applies. Stage
        // 90 must NOT be pre-claimed while only stage 90 was due — this proves the "quieter"
        // multi-stage collapse only auto-claims stages that are ALREADY due at claim time, never
        // stages that become due only later.
        clock.Advance(TimeSpan.FromDays(60));
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        var afterSecondRun = h.Db.Context.Notifications.Where(n => n.Type == MessageKinds.TankCardExpiry).ToList();
        Assert.Equal(2, afterSecondRun.Count);
        var newNotification = afterSecondRun.Except(afterFirstRun).Single();
        Assert.Contains("1 maand", newNotification.Message);
        Assert.DoesNotContain("1 maand", afterFirstRun[0].Message);

        var logsAfterSecondRun = h.Db.Context.ReminderDispatchLogs
            .Where(l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:")).ToList();
        Assert.Equal(2, logsAfterSecondRun.Count);
        Assert.Contains($"tankcard_expiry:{card.Id}:90", logsAfterSecondRun.Select(l => l.DedupeKey));
        Assert.Contains($"tankcard_expiry:{card.Id}:30", logsAfterSecondRun.Select(l => l.DedupeKey));
    }

    [Fact]
    public async Task BlockedCard_NeverFires()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var card = MakeCard(h.TenantId, daysUntilExpiry: 6, isBlocked: true);
        h.Db.Context.TankCards.Add(card);
        await h.Db.Context.SaveChangesAsync();

        var producer = new ExpiryNotificationProducer(h.Db.Context, new TestClock(Now));
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.DoesNotContain(h.Db.Context.Notifications, n => n.Type == MessageKinds.TankCardExpiry);
        Assert.DoesNotContain(h.Db.Context.ReminderDispatchLogs, l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:"));
    }

    [Fact]
    public async Task CardWithoutValidUntil_NeverFires()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var card = MakeCard(h.TenantId, daysUntilExpiry: 6);
        card.ValidUntil = null;
        h.Db.Context.TankCards.Add(card);
        await h.Db.Context.SaveChangesAsync();

        var producer = new ExpiryNotificationProducer(h.Db.Context, new TestClock(Now));
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.DoesNotContain(h.Db.Context.Notifications, n => n.Type == MessageKinds.TankCardExpiry);
        Assert.DoesNotContain(h.Db.Context.ReminderDispatchLogs, l => l.DedupeKey.StartsWith($"tankcard_expiry:{card.Id}:"));
    }
}
