using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Orders;

public class TransportOrderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.Locations.Add(new Location
        {
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Terminal Links",
            City = "Antwerpen", CountryCode = "BE", Type = LocationType.Terminal, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TransportOrderService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new TestClock(Now));
        return new Harness(db, sut, tenantId, customerId, locationId);
    }

    private static TransportOrderStopInput Stop(
        StopType type, Guid? locationId = null, string? city = null,
        DateTime? from = null, DateTime? to = null) =>
        new(type, locationId, null, null, null, city, locationId is null ? "BE" : null, from, to, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, params TransportOrderStopInput[] stops) => new(
        customerId, "PO-777", new DateOnly(2026, 7, 20), "20 paletten bouwmateriaal",
        20, "paletten", 12500, null, 20, false, false, 1450m, null, stops);

    [Fact]
    public async Task Create_ClaimsSequentialNumber_AndStartsDraft()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var first = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var second = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, city: "Luik"), Stop(StopType.Unloading, city: "Brussel")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, first.Outcome);
        Assert.Equal("ORD-0001", first.Order!.OrderNumber);
        Assert.Equal("ORD-0002", second.Order!.OrderNumber);
        Assert.Equal(TransportOrderStatus.Draft, first.Order.Status);
        Assert.Equal("Haven BV", first.Order.CustomerName);
    }

    [Fact]
    public async Task Create_ResolvesStopLocation_AndSequences()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        var stops = result.Order!.Stops;
        Assert.Equal(2, stops.Count);
        Assert.Equal(1, stops[0].Sequence);
        Assert.Equal("Terminal Links", stops[0].LocationName);
        Assert.Equal("Antwerpen", stops[0].City);
        Assert.Equal("LOC-1", stops[0].LocationCode);
        Assert.Equal("Gent", stops[1].City);
    }

    [Fact]
    public async Task Cancel_RequiresReason_StoresIt_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        var withoutReason = await h.Sut.CancelAsync(created.Order!.Id, "  ", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, withoutReason.Outcome);

        var cancelled = await h.Sut.CancelAsync(created.Order.Id, "Klant heeft afgezegd", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, cancelled.Outcome);
        Assert.Equal(TransportOrderStatus.Cancelled, cancelled.Order!.Status);
        Assert.Equal("Klant heeft afgezegd", cancelled.Order.CancellationReason);
        Assert.False(cancelled.Order.CanCancel);

        var auditEntries = await h.Db.Context.AuditLogs.ToListAsync(CancellationToken.None);
        Assert.Contains(auditEntries, a => a.EntityType == "TransportOrder" && a.Action == "Cancelled");
    }

    [Fact]
    public async Task Cancel_CompletedOrder_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var order = await h.Db.Context.TransportOrders.FindAsync(created.Order!.Id);
        order!.Status = TransportOrderStatus.Completed;
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CancelAsync(created.Order.Id, "te laat", CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task ChangeStatus_CanNoLongerReachCancelled()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        var result = await h.Sut.ChangeStatusAsync(created.Order!.Id, TransportOrderStatus.Cancelled, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
        Assert.DoesNotContain(TransportOrderStatus.Cancelled, created.Order.AllowedTransitions);
    }

    [Fact]
    public async Task ListForExport_ReturnsFilteredRows()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, city: "Luik"), Stop(StopType.Unloading, city: "Brussel")), CancellationToken.None);

        var all = await h.Sut.ListForExportAsync(null, null, null, null, null, CancellationToken.None);
        var drafts = await h.Sut.ListForExportAsync(null, TransportOrderStatus.Draft, null, null, null, CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal(2, drafts.Count);
    }

    [Fact]
    public async Task Create_CustomerRequiringReference_RejectsMissingReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        customer!.CustomerReferenceRequired = true;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var withoutReference = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"))
            with { CustomerReference = "  " };
        var rejected = await h.Sut.CreateAsync(withoutReference, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, rejected.Outcome);

        var accepted = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, accepted.Outcome);
    }

    [Fact]
    public async Task Create_BlockedCustomer_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        customer!.IsBlocked = true;
        customer.BlockReason = "Openstaande facturen";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Create_ForeignCustomer_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(Guid.NewGuid(),
            Stop(StopType.Loading, city: "Antwerpen")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task Create_ForeignLocation_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, Guid.NewGuid())), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task Create_StopWithoutLocationOrCity_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            new TransportOrderStopInput(StopType.Loading, null, null, null, null, null, null, null, null, null, null)),
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Confirm_RequiresLoadingAndUnloadingStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var onlyLoading = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId)), CancellationToken.None);

        var refused = await h.Sut.ChangeStatusAsync(onlyLoading.Order!.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, refused.Outcome);

        var complete = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var confirmed = await h.Sut.ChangeStatusAsync(complete.Order!.Id, TransportOrderStatus.Confirmed, CancellationToken.None);
        Assert.Equal(TransportOrderStatus.Confirmed, confirmed.Order!.Status);
    }

    [Fact]
    public async Task StatusFlow_GuardsIllegalJumps()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = order.Order!.Id;

        // Draft cannot complete directly.
        Assert.Equal(TransportOrderOperationOutcome.InvalidState,
            (await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Completed, CancellationToken.None)).Outcome);

        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.InProgress, CancellationToken.None);
        var completed = await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Completed, CancellationToken.None);

        Assert.Equal(TransportOrderStatus.Completed, completed.Order!.Status);
        Assert.Empty(completed.Order.AllowedTransitions);

        // Completed is terminal.
        Assert.Equal(TransportOrderOperationOutcome.InvalidState,
            (await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Cancelled, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Update_ReplacesStops_AndLocksAfterProgress()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = order.Order!.Id;

        var update = new UpdateTransportOrderRequest(
            h.CustomerId, "PO-778", new DateOnly(2026, 7, 21), "25 paletten", 25, "paletten",
            14000, null, 25, true, false, 1600m, "Spoed",
            [Stop(StopType.Loading, city: "Luik"), Stop(StopType.Unloading, city: "Brussel"), Stop(StopType.Unloading, city: "Gent")]);
        var updated = await h.Sut.UpdateAsync(id, update, CancellationToken.None);

        Assert.Equal(3, updated.Order!.Stops.Count);
        Assert.Equal("Luik", updated.Order.Stops[0].City);
        Assert.True(updated.Order.AdrRequired);

        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.InProgress, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState,
            (await h.Sut.UpdateAsync(id, update, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Update_TimeWindowEndBeforeStart_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId,
                from: new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc),
                to: new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc))), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.NotNull(order.Order);
    }

    [Fact]
    public async Task Delete_OnlyDraftOrCancelled()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = order.Order!.Id;

        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Confirmed, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidState,
            (await h.Sut.DeleteAsync(id, CancellationToken.None)).Outcome);

        // Cancelling is a dedicated action (no longer reachable through ChangeStatus).
        await h.Sut.CancelAsync(id, "Test", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success,
            (await h.Sut.DeleteAsync(id, CancellationToken.None)).Outcome);
        Assert.Null(await h.Sut.GetByIdAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Search_FiltersAndSummarizesRoute_TenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        var foreignTenant = Guid.NewGuid();
        var foreignCustomer = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Customers.Add(new Customer { Id = foreignCustomer, TenantId = foreignTenant, CustomerNumber = "X", Name = "Spy", IsActive = true });
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, CustomerId = foreignCustomer,
            OrderNumber = "ORD-9999", OrderDate = new(2026, 7, 20), GoodsDescription = "geheim",
        });
        await h.Db.Context.SaveChangesAsync();

        var all = await h.Sut.SearchAsync(null, null, null, null, null, PageRequest.Of(1, 25), CancellationToken.None);
        var single = Assert.Single(all.Items);
        Assert.Equal("Antwerpen", single.FirstLoadingCity);
        Assert.Equal("Gent", single.LastUnloadingCity);
        Assert.Equal(2, single.StopCount);

        var byGoods = await h.Sut.SearchAsync("bouwmateriaal", null, null, null, null, PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(1, byGoods.TotalCount);

        var draftOnly = await h.Sut.SearchAsync(null, TransportOrderStatus.Completed, null, null, null, PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Equal(0, draftOnly.TotalCount);
    }
}
