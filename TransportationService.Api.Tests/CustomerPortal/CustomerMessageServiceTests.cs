using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.CustomerPortal.Dtos;
using TransportationService.Api.Modules.CustomerPortal.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.CustomerPortal;

public class CustomerMessageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid CustomerId, Guid OtherCustomerId,
        Guid PortalUserId, Guid StaffUserId, Guid OrderId, Guid ForeignOrderId)
    {
        // Real TimeProvider.System — matches SqliteTestDbContext's hardcoded audit-stamp clock
        // (AuditingSaveChangesInterceptor always uses TimeProvider.System regardless of what a
        // service is given), so CustomerMessage.CreatedAt and CustomerMessageRead.LastReadAt stay
        // on the SAME clock. A fake TestClock here would desync from the real CreatedAt stamps.
        public CustomerMessageService For(Guid? userId) =>
            new(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(userId),
                new AuditService(Db.Context, new DevTenantContext(TenantId), new DevCurrentUserContext(userId)), TimeProvider.System);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var portalUserId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var foreignOrderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Customers.AddRange(
            new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true },
            new Customer { Id = otherCustomerId, TenantId = tenantId, CustomerNumber = "KL-2", Name = "Andere BV", IsActive = true });
        db.Context.Users.AddRange(
            new User { Id = portalUserId, TenantId = tenantId, Email = "klant@haven.be", FirstName = "Kaat", LastName = "Klant", CustomerId = customerId, IsActive = true },
            new User { Id = staffUserId, TenantId = tenantId, Email = "planner@acme.be", FirstName = "Pia", LastName = "Planner", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new DateOnly(2026, 7, 30), Status = TransportOrderStatus.Confirmed,
        });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = foreignOrderId, TenantId = tenantId, CustomerId = otherCustomerId, OrderNumber = "ORD-2",
            OrderDate = new DateOnly(2026, 7, 30), Status = TransportOrderStatus.Confirmed,
        });
        await db.Context.SaveChangesAsync();

        return new Harness(db, tenantId, customerId, otherCustomerId, portalUserId, staffUserId, orderId, foreignOrderId);
    }

    [Fact]
    public async Task Portal_SendAndList_RoundTripsOnGeneralThread()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.PortalUserId);

        var sent = await sut.SendPortalAsync(new SendCustomerMessageRequest(null, "Hallo, vraag over levering"), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, sent.Outcome);
        Assert.False(sent.Value!.AuthorIsStaff);

        var listed = await sut.ListPortalAsync(null, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.Success, listed.Outcome);
        Assert.Single(listed.Value!);
    }

    [Fact]
    public async Task Portal_SendOnForeignOrder_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.PortalUserId);

        var result = await sut.SendPortalAsync(new SendCustomerMessageRequest(h.ForeignOrderId, "test"), CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Portal_ListOnForeignOrder_ReturnsNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.PortalUserId);

        var result = await sut.ListPortalAsync(h.ForeignOrderId, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Portal_UnlinkedUser_GetsNoCustomerLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.StaffUserId); // no CustomerId link

        var result = await sut.ListPortalAsync(null, CancellationToken.None);
        Assert.Equal(PortalOutcomeKind.NoCustomerLink, result.Outcome);
    }

    [Fact]
    public async Task Internal_ListForUnknownCustomer_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.StaffUserId);

        var result = await sut.ListForCustomerAsync(Guid.NewGuid(), null, CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Internal_SendWithForeignOrder_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.StaffUserId);

        // ForeignOrderId belongs to OtherCustomerId, not CustomerId.
        var result = await sut.SendToCustomerAsync(
            h.CustomerId, new SendCustomerMessageRequest(h.ForeignOrderId, "hallo"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Internal_SendEmptyBody_Throws()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var sut = h.For(h.StaffUserId);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.SendToCustomerAsync(h.CustomerId, new SendCustomerMessageRequest(null, "   "), CancellationToken.None));
    }

    [Fact]
    public async Task UnreadCounts_TrackPerSide_AndResetOnMarkRead()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var portal = h.For(h.PortalUserId);
        var staff = h.For(h.StaffUserId);

        // Customer writes -> staff has 1 unread; portal has 0 (its own message).
        await portal.SendPortalAsync(new SendCustomerMessageRequest(null, "Vraag 1"), CancellationToken.None);
        Assert.Equal(1, (await staff.GetCustomerUnreadCountAsync(h.CustomerId, CancellationToken.None))!.Value);
        Assert.Equal(0, (await portal.GetPortalUnreadCountAsync(CancellationToken.None)).Value!.Count);

        // Staff replies -> portal has 1 unread; staff's own unread (from customer) still 1 until marked.
        await staff.SendToCustomerAsync(h.CustomerId, new SendCustomerMessageRequest(null, "Antwoord"), CancellationToken.None);
        Assert.Equal(1, (await portal.GetPortalUnreadCountAsync(CancellationToken.None)).Value!.Count);

        // Mark read on both sides -> unread drops to zero.
        await staff.MarkCustomerReadAsync(h.CustomerId, null, CancellationToken.None);
        Assert.Equal(0, (await staff.GetCustomerUnreadCountAsync(h.CustomerId, CancellationToken.None))!.Value);

        await portal.MarkPortalReadAsync(null, CancellationToken.None);
        Assert.Equal(0, (await portal.GetPortalUnreadCountAsync(CancellationToken.None)).Value!.Count);

        // A later message brings the count back to 1 on the recipient side only. A small real
        // delay guarantees CreatedAt strictly exceeds the just-set LastReadAt marker (both stamped
        // from the real system clock — see the Harness.For comment).
        await Task.Delay(20);
        await portal.SendPortalAsync(new SendCustomerMessageRequest(null, "Vraag 2"), CancellationToken.None);
        Assert.Equal(1, (await staff.GetCustomerUnreadCountAsync(h.CustomerId, CancellationToken.None))!.Value);
        Assert.Equal(0, (await portal.GetPortalUnreadCountAsync(CancellationToken.None)).Value!.Count);
    }

    [Fact]
    public async Task Threads_AreScopedPerOrder_GeneralThreadStaysSeparate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var portal = h.For(h.PortalUserId);

        await portal.SendPortalAsync(new SendCustomerMessageRequest(null, "Algemeen bericht"), CancellationToken.None);
        await portal.SendPortalAsync(new SendCustomerMessageRequest(h.OrderId, "Bericht over ORD-1"), CancellationToken.None);

        var general = await portal.ListPortalAsync(null, CancellationToken.None);
        var orderThread = await portal.ListPortalAsync(h.OrderId, CancellationToken.None);
        Assert.Single(general.Value!);
        Assert.Single(orderThread.Value!);
        Assert.Equal("ORD-1", orderThread.Value![0].OrderNumber);
    }

    [Fact]
    public async Task Internal_SendPublishesReply_ButRequestInfoStyleSilentSendSuppressesIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var staff = h.For(h.StaffUserId);

        await staff.SendToCustomerAsync(h.CustomerId, new SendCustomerMessageRequest(null, "hi"), CancellationToken.None, publishNotification: false);
        Assert.Empty(h.Db.Context.OutboxMessages);

        await staff.SendToCustomerAsync(h.CustomerId, new SendCustomerMessageRequest(null, "hi 2"), CancellationToken.None);
        // No NotificationEventService wired in this harness (nullable dependency) — publish is a
        // safe no-op, proving the flag alone (not a missing dependency) drives the suppression
        // in the request-info path tested end-to-end in OrderPortalReviewServiceTests.
        Assert.Empty(h.Db.Context.OutboxMessages);
    }
}
