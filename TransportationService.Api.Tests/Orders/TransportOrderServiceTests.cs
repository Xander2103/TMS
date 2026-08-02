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

    /// <summary>
    /// Maps a detail DTO back into an update request, carrying stops AND cargo items (reused by
    /// later tasks). Cargo stop links are re-resolved from ids to indexes via the detail DTO's
    /// stop list order, since CargoItemInput addresses stops by index.
    /// </summary>
    private static UpdateTransportOrderRequest BuildUpdateFrom(TransportOrderDetailDto d)
    {
        var stopIndexById = d.Stops
            .Select((s, i) => (s.Id, Index: i))
            .ToDictionary(x => x.Id, x => x.Index);

        return new UpdateTransportOrderRequest(
            d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
            d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
            d.AgreedPrice, d.Notes,
            d.Stops.Select(s => new TransportOrderStopInput(
                    s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
                    s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions))
                .ToList(),
            CargoItems: d.CargoItems.Select(c => new CargoItemInput(
                    c.Description, c.Barcode, c.ExpectedQuantity, c.QuantityUnit, c.Notes,
                    c.UnitType, c.UnitTypeLabel, c.TotalWeightKg, c.WeightPerUnitKg,
                    c.LengthMeters, c.WidthMeters, c.HeightMeters, c.VolumeM3, c.VolumeIsManual,
                    c.AdrRequired, c.AdrDetails, c.Stackable, c.Reference,
                    c.LoadingStopId is { } lid && stopIndexById.TryGetValue(lid, out var li) ? li : null,
                    c.UnloadingStopId is { } uid && stopIndexById.TryGetValue(uid, out var ui) ? ui : null,
                    c.QuantityUnitCode, Id: c.Id))
                .ToList(),
            QuantityUnitCode: d.QuantityUnitCode);
    }

    [Fact]
    public async Task Update_ChangedCargoUnit_RoundTripsThroughDetailDto()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        };
        var created = await h.Sut.CreateAsync(create, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Equal("EUROPALLET", Assert.Single(created.Order!.CargoItems).QuantityUnitCode);

        h.Db.Context.ChangeTracker.Clear();
        var update = BuildUpdateFrom(created.Order!) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "COLLI")],
        };
        var updated = await h.Sut.UpdateAsync(created.Order!.Id, update, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal("COLLI", Assert.Single(updated.Order!.CargoItems).QuantityUnitCode);

        h.Db.Context.ChangeTracker.Clear();
        var reloaded = await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None);
        Assert.Equal("COLLI", Assert.Single(reloaded!.CargoItems).QuantityUnitCode);
    }

    [Fact]
    public async Task Update_MatchingCargoId_UpdatesInPlace_KeepsGuid_AndAuditsUnitChange()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        };
        var created = await h.Sut.CreateAsync(create, CancellationToken.None);
        var lineId = created.Order!.CargoItems.Single().Id;
        h.Db.Context.ChangeTracker.Clear();

        var update = BuildUpdateFrom(created.Order!) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null,
                QuantityUnitCode: "COLLI", Id: lineId)],
        };
        var updated = await h.Sut.UpdateAsync(created.Order!.Id, update, CancellationToken.None);

        Assert.Equal(lineId, Assert.Single(updated.Order!.CargoItems).Id); // id preserved
        var entity = await h.Db.Context.CargoItems.SingleAsync(c => c.Id == lineId);
        Assert.Equal("COLLI", entity.QuantityUnitCode);

        var audit = await h.Db.Context.AuditLogs
            .Where(a => a.EntityType == "TransportOrder" && a.Action == "Updated")
            .OrderByDescending(a => a.Id).FirstAsync();
        Assert.Contains("EUROPALLET", audit.OldValuesJson);
        Assert.Contains("COLLI", audit.NewValuesJson);
    }

    [Fact]
    public async Task Update_NullCargoItems_LeavesExistingLinesUntouched()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        };
        var created = await h.Sut.CreateAsync(create, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Order!.Id,
            BuildUpdateFrom(created.Order!) with { CargoItems = null }, CancellationToken.None);
        Assert.Single(updated.Order!.CargoItems); // not wiped
    }

    [Fact]
    public async Task Update_EmptyCargoItems_ClearsLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        };
        var created = await h.Sut.CreateAsync(create, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Order!.Id,
            BuildUpdateFrom(created.Order!) with { CargoItems = [] }, CancellationToken.None);
        Assert.Empty(updated.Order!.CargoItems);
    }

    [Fact]
    public async Task Update_DuplicateCargoId_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        };
        var created = await h.Sut.CreateAsync(create, CancellationToken.None);
        var lineId = created.Order!.CargoItems.Single().Id;
        h.Db.Context.ChangeTracker.Clear();

        var update = BuildUpdateFrom(created.Order!) with
        {
            CargoItems =
            [
                new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET", Id: lineId),
                new CargoItemInput("Andere lijn", null, 1, null, null, QuantityUnitCode: "COLLI", Id: lineId),
            ],
        };
        var result = await h.Sut.UpdateAsync(created.Order!.Id, update, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        // The rejected request must never touch the existing line.
        var entity = await h.Db.Context.CargoItems.SingleAsync(c => c.Id == lineId);
        Assert.Equal("EUROPALLET", entity.QuantityUnitCode);
    }

    [Fact]
    public async Task Priority_DefaultsToNormal_InlineChangeIsAuditedAndGuarded()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        Assert.Equal(OrderPriority.Normal, created.Order!.Priority);

        var changed = await h.Sut.ChangePriorityAsync(created.Order.Id, OrderPriority.Urgent, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, changed.Outcome);
        Assert.Equal(OrderPriority.Urgent, changed.Order!.Priority);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.Action == "PriorityChanged" && a.EntityId == created.Order.Id.ToString());

        // Final statuses refuse a priority change.
        await h.Sut.CancelAsync(created.Order.Id, "Klant heeft geannuleerd.", CancellationToken.None);
        var refused = await h.Sut.ChangePriorityAsync(created.Order.Id, OrderPriority.Low, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidState, refused.Outcome);
    }

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
    public async Task Create_DeactivatedCustomer_IsRejected_ButExistingOrderStaysEditable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var existing = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, existing.Outcome);

        var customer = await h.Db.Context.Customers.FindAsync(h.CustomerId);
        customer!.IsActive = false;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var rejected = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, rejected.Outcome);
        Assert.Contains("gedeactiveerd", rejected.Error);

        // Updating an existing order for the SAME (now inactive) customer stays possible.
        h.Db.Context.ChangeTracker.Clear();
        var updated = await h.Sut.UpdateAsync(existing.Order!.Id, new UpdateTransportOrderRequest(
            h.CustomerId, "PO-777", new DateOnly(2026, 7, 20), "20 paletten bouwmateriaal",
            20, "paletten", 12500, null, 20, false, false, 1450m, null,
            [Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Brugge")]), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
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
    public async Task Delete_SoftDeletesCargoItems()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "COLLI")],
        };
        var created = await h.Sut.CreateAsync(create, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        await h.Sut.DeleteAsync(created.Order!.Id, CancellationToken.None);

        var cargo = await h.Db.Context.CargoItems.IgnoreQueryFilters()
            .SingleAsync(c => c.TransportOrderId == created.Order!.Id);
        Assert.True(cargo.IsDeleted);
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

    [Fact]
    public async Task Create_PersistsWindowAppointmentAndInstructionFields()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var stop = new TransportOrderStopInput(
            StopType.Loading, h.LocationId, null, null, null, null, null,
            PlannedFrom: new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc),
            PlannedTo: new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc),
            Reference: "DOSSIER-1", Instructions: "Melden bij portier",
            RequestedFrom: new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc),
            RequestedTo: new DateTime(2026, 7, 21, 11, 0, 0, DateTimeKind.Utc),
            EarliestAllowed: new DateTime(2026, 7, 21, 6, 0, 0, DateTimeKind.Utc),
            LatestAllowed: new DateTime(2026, 7, 21, 14, 0, 0, DateTimeKind.Utc),
            AppointmentRequired: true, AppointmentReference: "SLOT-9",
            AccessInstructions: "Alfapass", LoadingInstructions: "Dok 3", UnloadingInstructions: null);

        var result = await h.Sut.CreateAsync(Request(h.CustomerId, stop, Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var dto = result.Order!.Stops[0];
        Assert.Equal(new DateTime(2026, 7, 21, 9, 0, 0, DateTimeKind.Utc), dto.RequestedFrom);
        Assert.Equal(new DateTime(2026, 7, 21, 14, 0, 0, DateTimeKind.Utc), dto.LatestAllowed);
        Assert.True(dto.AppointmentRequired);
        Assert.Equal("SLOT-9", dto.AppointmentReference);
        Assert.Equal("Alfapass", dto.AccessInstructions);
        Assert.Equal("Dok 3", dto.LoadingInstructions);
    }

    [Fact]
    public async Task Create_InvalidWindowPairs_FailValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var requestedReversed = new TransportOrderStopInput(
            StopType.Loading, h.LocationId, null, null, null, null, null, null, null, null, null,
            RequestedFrom: new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc),
            RequestedTo: new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc));
        var earliestAfterLatest = new TransportOrderStopInput(
            StopType.Loading, h.LocationId, null, null, null, null, null, null, null, null, null,
            EarliestAllowed: new DateTime(2026, 7, 21, 16, 0, 0, DateTimeKind.Utc),
            LatestAllowed: new DateTime(2026, 7, 21, 6, 0, 0, DateTimeKind.Utc));

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed,
            (await h.Sut.CreateAsync(Request(h.CustomerId, requestedReversed), CancellationToken.None)).Outcome);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed,
            (await h.Sut.CreateAsync(Request(h.CustomerId, earliestAfterLatest), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task UpdateStopExecutionPlan_ConfirmsWindow_ValidatesAndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var orderId = order.Order!.Id;
        var stopId = order.Order.Stops[0].Id;

        // The confirmed window can be set even after the order left the editable statuses.
        await h.Sut.ChangeStatusAsync(orderId, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(orderId, TransportOrderStatus.InProgress, CancellationToken.None);

        var reversed = await h.Sut.UpdateStopExecutionPlanAsync(orderId, stopId, new UpdateStopExecutionPlanRequest(
            new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc),
            null, null, false, null, null, null, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, reversed.Outcome);

        var result = await h.Sut.UpdateStopExecutionPlanAsync(orderId, stopId, new UpdateStopExecutionPlanRequest(
            new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc),
            null, null, true, "SLOT-42", "Poort B", null, null), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var dto = result.Order!.Stops.Single(s => s.Id == stopId);
        Assert.Equal(new DateTime(2026, 7, 21, 8, 0, 0, DateTimeKind.Utc), dto.ConfirmedFrom);
        Assert.Equal("SLOT-42", dto.AppointmentReference);
        Assert.True(dto.AppointmentRequired);
        Assert.Equal("Poort B", dto.AccessInstructions);

        Assert.Contains(h.Db.Context.AuditLogs,
            a => a.EntityType == "TransportOrder" && a.Action == "StopExecutionPlanUpdated");

        var unknownStop = await h.Sut.UpdateStopExecutionPlanAsync(orderId, Guid.NewGuid(),
            new UpdateStopExecutionPlanRequest(null, null, null, null, false, null, null, null, null), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.NotFound, unknownStop.Outcome);
    }

    [Fact]
    public async Task Create_WithCargoItems_RoundTripsAndSequences()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems =
            [
                new CargoItemInput("Pallet bouwstenen", "BC-1", 10, "paletten", null),
                new CargoItemInput("Pallet cement", null, 5, "paletten", "Breekbaar"),
            ],
        };

        var result = await h.Sut.CreateAsync(request, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.Equal(2, result.Order!.CargoItems.Count);
        Assert.Equal(1, result.Order.CargoItems[0].Sequence);
        Assert.Equal("BC-1", result.Order.CargoItems[0].Barcode);
        Assert.Null(result.Order.CargoItems[1].Barcode);
        Assert.Equal(5, result.Order.CargoItems[1].ExpectedQuantity);
    }

    [Fact]
    public async Task Create_DuplicateBarcodeWithinOrder_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems =
            [
                new CargoItemInput("Item 1", "BC-DUP", 1, null, null),
                new CargoItemInput("Item 2", "bc-dup", 1, null, null),
            ],
        };

        var result = await h.Sut.CreateAsync(request, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task UpdateStopExecutionPlan_OnCancelledOrder_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        await h.Sut.CancelAsync(order.Order!.Id, "Geannuleerd door klant", CancellationToken.None);

        var result = await h.Sut.UpdateStopExecutionPlanAsync(order.Order.Id, order.Order.Stops[0].Id,
            new UpdateStopExecutionPlanRequest(null, null, null, null, false, null, null, null, null), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }

    /// <summary>
    /// Was "Create_WithoutGoodsDescription_Succeeds": an order with no description anywhere
    /// (general blank, no cargo lines) used to be accepted. This is the wave's one deliberate
    /// behavior change — see Create_NoDescriptionAnywhere_IsRejected below — so this test now
    /// carries one described cargo line to keep proving the general field stays optional.
    /// </summary>
    [Fact]
    public async Task Create_WithoutGoodsDescription_ButWithDescribedLine_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            GoodsDescription = "  ",
            CargoItems = [new CargoItemInput("Onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        },
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        Assert.Null(created.Order!.GoodsDescription);
    }

    [Fact]
    public async Task Create_LineDescriptionOnly_Succeeds_GeneralOptional()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            GoodsDescription = null,
            CargoItems = [new CargoItemInput("2 europallets onderdelen", null, 2, null, null, QuantityUnitCode: "EUROPALLET")],
        },
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task Create_GeneralDescriptionOnly_WithDescriptionlessLine_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            GoodsDescription = "Gemengde goederen",
            CargoItems = [new CargoItemInput(null, null, 4, null, null, QuantityUnitCode: "COLLI")],
        },
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task Create_NoDescriptionAnywhere_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            GoodsDescription = null,
            CargoItems = [new CargoItemInput(null, null, 4, null, null, QuantityUnitCode: "COLLI")],
        },
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("omschrijving", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatusChanges_AreRecordedInImmutableHistory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = order.Order!.Id;

        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Sut.CancelAsync(id, "Klant heeft afgezegd", CancellationToken.None);

        var history = h.Db.Context.TransportOrderStatusHistories
            .Where(x => x.TransportOrderId == id).OrderBy(x => x.ChangedAt).ThenBy(x => x.ToStatus).ToList();
        Assert.Equal(2, history.Count);
        Assert.Contains(history, x => x.FromStatus == TransportOrderStatus.Draft && x.ToStatus == TransportOrderStatus.Confirmed && x.Reason == null);
        Assert.Contains(history, x => x.ToStatus == TransportOrderStatus.Cancelled && x.Reason == "Klant heeft afgezegd" && !x.IsCorrection);
    }

    [Fact]
    public async Task CorrectStatus_RequiresReason_FollowsCorrectiveMap_AndRecordsCorrection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = order.Order!.Id;
        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.InProgress, CancellationToken.None);
        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Completed, CancellationToken.None);

        var withoutReason = await h.Sut.CorrectStatusAsync(id, TransportOrderStatus.InProgress, " ", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, withoutReason.Outcome);

        // Completed → Draft is not a defined correction.
        var invalidTarget = await h.Sut.CorrectStatusAsync(id, TransportOrderStatus.Draft, "Vergissing", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidState, invalidTarget.Outcome);

        var corrected = await h.Sut.CorrectStatusAsync(id, TransportOrderStatus.InProgress, "Verkeerd afgevinkt", CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, corrected.Outcome);
        Assert.Equal(TransportOrderStatus.InProgress, corrected.Order!.Status);

        var correction = h.Db.Context.TransportOrderStatusHistories
            .Single(x => x.TransportOrderId == id && x.IsCorrection);
        Assert.Equal(TransportOrderStatus.Completed, correction.FromStatus);
        Assert.Equal(TransportOrderStatus.InProgress, correction.ToStatus);
        Assert.Equal("Verkeerd afgevinkt", correction.Reason);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.Action == "StatusCorrected");
    }

    [Fact]
    public async Task CorrectStatus_FromCancelled_ReactivatesToDraft_AndClearsCancellationReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        await h.Sut.CancelAsync(order.Order!.Id, "Per ongeluk geannuleerd", CancellationToken.None);

        var corrected = await h.Sut.CorrectStatusAsync(order.Order.Id, TransportOrderStatus.Draft, "Annulatie was een vergissing", CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, corrected.Outcome);
        Assert.Equal(TransportOrderStatus.Draft, corrected.Order!.Status);
        Assert.Null(corrected.Order.CancellationReason);
        Assert.Contains(TransportOrderStatus.Confirmed, corrected.Order.AllowedTransitions);
    }

    [Fact]
    public async Task Cargo_RichLine_RoundTrips_WithDerivedVolume_AndExplicitStopLinks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent"), Stop(StopType.Unloading, city: "Brugge")) with
        {
            CargoItems =
            [
                new CargoItemInput("Europalletten bouwmateriaal", "PAL-1", 10, "paletten", null,
                    UnitType: Modules.Packages.Entities.PackageUnitType.EuroPallet,
                    TotalWeightKg: 8000, WeightPerUnitKg: 800,
                    LengthMeters: 1.2m, WidthMeters: 0.8m, HeightMeters: 1.5m,
                    AdrRequired: true, AdrDetails: "UN 1263, klasse 3", Stackable: false, Reference: "LIJN-1",
                    LoadingStopIndex: 0, UnloadingStopIndex: 2),
            ],
        }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var line = Assert.Single(created.Order!.CargoItems);
        Assert.Equal(Modules.Packages.Entities.PackageUnitType.EuroPallet, line.UnitType);
        Assert.Equal(1.44m, line.VolumeM3);
        Assert.False(line.VolumeIsManual);
        Assert.True(line.AdrRequired);
        Assert.False(line.Stackable);
        Assert.Equal(created.Order.Stops[0].Id, line.LoadingStopId);
        Assert.Equal(created.Order.Stops[2].Id, line.UnloadingStopId);
    }

    [Fact]
    public async Task Cargo_OmittedStopLinks_AutoLink_WhenUnambiguous()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")) with
        {
            CargoItems = [new CargoItemInput("Colli", null, 5, "colli", null)],
        }, CancellationToken.None);

        var line = Assert.Single(created.Order!.CargoItems);
        Assert.Equal(created.Order.Stops[0].Id, line.LoadingStopId);
        Assert.Equal(created.Order.Stops[1].Id, line.UnloadingStopId);
    }

    [Fact]
    public async Task Cargo_InvalidStopLinks_AndNegativeWeights_AreRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var stops = new[] { Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent") };

        var wrongType = await h.Sut.CreateAsync(Request(h.CustomerId, stops) with
        {
            CargoItems = [new CargoItemInput("X", null, 1, null, null, UnloadingStopIndex: 0)],
        }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, wrongType.Outcome);

        var wrongOrder = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Unloading, city: "Gent"), Stop(StopType.Loading, h.LocationId)) with
        {
            CargoItems = [new CargoItemInput("X", null, 1, null, null, LoadingStopIndex: 1, UnloadingStopIndex: 0)],
        }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, wrongOrder.Outcome);

        var negative = await h.Sut.CreateAsync(Request(h.CustomerId, stops) with
        {
            CargoItems = [new CargoItemInput("X", null, 1, null, null, TotalWeightKg: -5)],
        }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, negative.Outcome);
    }

    [Fact]
    public async Task Timeline_MergesAuditAndStatusHistory_Chronologically()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var id = order.Order!.Id;
        await h.Sut.ChangeStatusAsync(id, TransportOrderStatus.Confirmed, CancellationToken.None);
        await h.Sut.CancelAsync(id, "Klant heeft afgezegd", CancellationToken.None);

        var timeline = new TransportOrderTimelineService(h.Db.Context, new DevTenantContext(h.TenantId));
        var events = await timeline.GetTimelineAsync(id, CancellationToken.None);

        Assert.NotNull(events);
        Assert.Contains(events!, e => e.Category == "order" && e.Title == "Opdracht aangemaakt");
        Assert.Contains(events!, e => e.Category == "status" && e.Title.Contains("Concept → Bevestigd"));
        Assert.Contains(events!, e => e.Category == "status" && e.Title.Contains("Geannuleerd") && e.Detail == "Klant heeft afgezegd");
        // Chronological (ascending) and free of the duplicate raw StatusChanged audit rows.
        Assert.True(events!.Zip(events.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
        Assert.DoesNotContain(events, e => e.Title == "StatusChanged");

        Assert.Null(await timeline.GetTimelineAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CorrectStatus_OnInvoicedOrder_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var order = await h.Sut.CreateAsync(Request(h.CustomerId,
            Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);
        var stored = await h.Db.Context.TransportOrders.FindAsync(order.Order!.Id);
        stored!.Status = TransportOrderStatus.Invoiced;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var result = await h.Sut.CorrectStatusAsync(order.Order.Id, TransportOrderStatus.Completed, "Toch niet klaar", CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidState, result.Outcome);
    }
}
