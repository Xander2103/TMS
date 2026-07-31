using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Notifications;

/// <summary>Dedupe/acknowledge/resolve/expiry semantics added by the inventory-tasks-notifications sprint.</summary>
public class NotificationLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 01, 12, 0, 0, TimeSpan.Zero);

    private static (SqliteTestDbContext Db, NotificationService Service, Guid UserId, TestClock Clock) Arrange()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "A", Slug = "a", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Users.Add(new User
        {
            Id = userId, TenantId = tenantId, Email = "me@a.be", PasswordHash = "x",
            FirstName = "Ik", LastName = "Zelf", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.SaveChanges();
        var clock = new TestClock(Now);
        var service = new NotificationService(db.Context, new DevTenantContext(tenantId), new DevCurrentUserContext(userId), clock);
        return (db, service, userId, clock);
    }

    [Fact]
    public async Task DedupeKey_SuppressesDuplicates_UntilResolved()
    {
        var (db, sut, userId, _) = Arrange();
        using var _ = db;
        var options = new NotificationOptions(DedupeKey: "inventory_status:artikel-1");

        await sut.NotifyAsync(userId, "inventory_low_stock", "Lage voorraad", "Artikel 1", null, CancellationToken.None, options);
        await sut.NotifyAsync(userId, "inventory_low_stock", "Lage voorraad", "Artikel 1 (dubbel)", null, CancellationToken.None, options);

        var mine = await sut.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        Assert.Single(mine);

        // Resolving the condition re-arms the key: the next occurrence notifies again.
        await sut.ResolveByDedupeKeyAsync("inventory_status:artikel-1", CancellationToken.None);
        await sut.NotifyAsync(userId, "inventory_low_stock", "Lage voorraad", "Artikel 1 (opnieuw)", null, CancellationToken.None, options);

        mine = await sut.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        Assert.Equal(2, mine.Count);
        Assert.Single(mine, n => n.ResolvedAt is not null);
    }

    [Fact]
    public async Task DedupeKey_WithinOneFanOutBatch_WritesOneRowPerRecipient()
    {
        var (db, sut, userId, _) = Arrange();
        using var _ = db;
        var options = new NotificationOptions(DedupeKey: "task_overdue:taak-1");

        // Tenant fan-out twice in a row: the second publish is fully suppressed.
        await sut.NotifyTenantAsync("inventory_low_stock", "T", "B", null, CancellationToken.None, options);
        await sut.NotifyTenantAsync("inventory_low_stock", "T", "B", null, CancellationToken.None, options);

        var mine = await sut.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        Assert.Single(mine);
    }

    [Fact]
    public async Task Acknowledge_MarksReadAndStampsAcknowledgedAt_Once()
    {
        var (db, sut, userId, clock) = Arrange();
        using var _ = db;
        await sut.NotifyAsync(userId, "test", "Bevestig mij", "B", null, CancellationToken.None,
            new NotificationOptions(RequiresAcknowledgement: true));
        var id = (await sut.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None)).Single().Id;

        Assert.True(await sut.AcknowledgeAsync(id, CancellationToken.None));
        var first = (await sut.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None)).Single();
        Assert.True(first.IsRead);
        Assert.NotNull(first.AcknowledgedAt);

        // Acknowledging again never moves the original timestamp.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(await sut.AcknowledgeAsync(id, CancellationToken.None));
        var second = (await sut.ListMineAsync(new NotificationQuery(false, null, false, 10), CancellationToken.None)).Single();
        Assert.Equal(first.AcknowledgedAt, second.AcknowledgedAt);
    }

    [Fact]
    public async Task Acknowledge_ForeignNotification_ReturnsFalse()
    {
        var (db, sut, userId, _) = Arrange();
        using var _ = db;
        Assert.False(await sut.AcknowledgeAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredNotifications_DropOutOfListAndUnreadCount()
    {
        var (db, sut, userId, clock) = Arrange();
        using var _ = db;
        await sut.NotifyAsync(userId, "test", "Vluchtig", "B", null, CancellationToken.None,
            new NotificationOptions(ExpiresAt: Now.UtcDateTime.AddHours(1)));
        await sut.NotifyAsync(userId, "test", "Blijvend", "B", null, CancellationToken.None);

        Assert.Equal(2, await sut.UnreadCountAsync(CancellationToken.None));

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, await sut.UnreadCountAsync(CancellationToken.None));
        var visible = await sut.ListMineAsync(new NotificationQuery(false, null, false, 50), CancellationToken.None);
        Assert.Equal("Blijvend", Assert.Single(visible).Title);

        // The archived view still shows the expired one (history stays inspectable).
        var all = await sut.ListMineAsync(new NotificationQuery(false, null, true, 50), CancellationToken.None);
        Assert.Equal(2, all.Count);
    }
}
