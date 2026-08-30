using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// Phase 7 (master-data wave 2026-08-05): order stops freeze a snapshot of their master
/// location at order time. Editing the location afterwards must never rewrite historical
/// orders; snapshots survive the wholesale stop rebuild on update; an explicit refresh
/// re-copies and is audited; access codes are permission-gated in order DTOs.
/// </summary>
public class StopSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, TransportOrderService Sut, Guid TenantId, Guid CustomerId, Guid LocationId);

    private static async Task<Harness> SeedAsync(
        Modules.Identity.Services.IPermissionAuthorizationService? permissions = null)
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
            Id = locationId, TenantId = tenantId, Code = "LOC-1", Name = "Magazijn Antwerpen",
            Street = "Noorderlaan", HouseNumber = "10", PostalCode = "2030", City = "Antwerpen", CountryCode = "be",
            Type = LocationType.Warehouse, IsActive = true,
            ContactName = "Magazijnier Piet", ContactPhone = "+32 3 123 45 67",
            ContactMobile = "+32 470 11 22 33", ContactEmail = "piet@haven.be",
            Gate = "Poort B", AccessCode = "1234#", Dock = "Kade 7", RouteDescription = "Via de Noorderlaan, tweede poort",
            DefaultLoadingMinutes = 30, DefaultUnloadingMinutes = 45,
            AppointmentRequired = true,
            DriverInstructions = "Aanmelden bij de weegbrug",
            AccessInstructions = "Alfapass verplicht",
            LoadingInstructions = "Laden enkel aan dok 5",
            UnloadingInstructions = "Lossen achteraan",
            OpeningIntervals =
            [
                new LocationOpeningInterval { Id = Guid.NewGuid(), TenantId = tenantId, LocationId = locationId, DayOfWeek = 1, FromTime = new(8, 0), ToTime = new(12, 0) },
                new LocationOpeningInterval { Id = Guid.NewGuid(), TenantId = tenantId, LocationId = locationId, DayOfWeek = 1, FromTime = new(13, 0), ToTime = new(17, 0) },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TransportOrderService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new TestClock(Now),
            currentUser: permissions is null ? null : new DevCurrentUserContext(Guid.NewGuid()),
            permissionService: permissions);
        return new Harness(db, sut, tenantId, customerId, locationId);
    }

    private static TransportOrderStopInput Stop(
        StopType type, Guid? locationId = null, string? city = null,
        DateTime? from = null, DateTime? to = null) =>
        new(type, locationId, null, null, null, city, locationId is null ? "BE" : null, from, to, null, null);

    private static CreateTransportOrderRequest Request(Guid customerId, params TransportOrderStopInput[] stops) => new(
        customerId, "PO-777", new DateOnly(2026, 7, 20), "20 paletten bouwmateriaal",
        20, "paletten", 12500, null, 20, false, false, 1450m, null, stops);

    private static UpdateTransportOrderRequest UpdateFrom(TransportOrderDetailDto d, IReadOnlyList<TransportOrderStopInput> stops) => new(
        d.CustomerId, d.CustomerReference, d.OrderDate, d.GoodsDescription, d.Quantity,
        d.QuantityUnit, d.WeightKg, d.VolumeM3, d.PalletCount, d.AdrRequired, d.CraneRequired,
        d.AgreedPrice, d.Notes, stops, QuantityUnitCode: d.QuantityUnitCode);

    /// <summary>Maps a detail stop back into the input the frontend would echo (id included).</summary>
    private static TransportOrderStopInput EchoStop(TransportOrderStopDto s) => new(
        s.StopType, s.LocationId, s.LocationName, s.Address, s.PostalCode, s.City, s.CountryCode,
        s.PlannedFrom, s.PlannedTo, s.Reference, s.Instructions,
        s.RequestedFrom, s.RequestedTo, s.ConfirmedFrom, s.ConfirmedTo,
        s.EarliestAllowed, s.LatestAllowed, s.AppointmentRequired, s.AppointmentReference,
        s.AccessInstructions, s.LoadingInstructions, s.UnloadingInstructions,
        s.TimeRequirement, s.TimeRequirementFrom, s.TimeRequirementTo, s.IncludedTimeMinutesOverride,
        Id: s.Id);

    [Fact]
    public async Task Create_MasterLocationStop_TakesFullSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")),
            CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
        var entity = await h.Db.Context.TransportOrderStops
            .SingleAsync(s => s.LocationId == h.LocationId);
        Assert.Equal("Magazijn Antwerpen", entity.LocationName);
        Assert.Equal("Noorderlaan 10", entity.Address);
        Assert.Equal("2030", entity.PostalCode);
        Assert.Equal("Antwerpen", entity.City);
        Assert.Equal("BE", entity.CountryCode);
        Assert.Equal("Magazijnier Piet", entity.ContactName);
        Assert.Equal("+32 3 123 45 67", entity.ContactPhone);
        Assert.Equal("+32 470 11 22 33", entity.ContactMobile);
        Assert.Equal("piet@haven.be", entity.ContactEmail);
        Assert.Equal("Ma 08:00–12:00, 13:00–17:00", entity.OpeningHoursSummary);
        Assert.Equal("Poort B", entity.Gate);
        Assert.Equal("1234#", entity.AccessCode);
        Assert.Equal("Kade 7", entity.Dock);
        Assert.Equal("Via de Noorderlaan, tweede poort", entity.RouteDescription);
        Assert.Equal(30, entity.DefaultLoadingMinutes);
        Assert.Equal(45, entity.DefaultUnloadingMinutes);
        Assert.True(entity.AppointmentRequired); // OR-ed in from the location.
        Assert.Equal(Now.UtcDateTime, entity.SnapshotAt);
        // Instructions copied where the input left them empty.
        Assert.Equal("Aanmelden bij de weegbrug", entity.Instructions);
        Assert.Equal("Alfapass verplicht", entity.AccessInstructions);
        Assert.Equal("Laden enkel aan dok 5", entity.LoadingInstructions);
        Assert.Equal("Lossen achteraan", entity.UnloadingInstructions);

        // Detail DTO renders the snapshot (access code stays null without the permission).
        var stopDto = created.Order!.Stops.Single(s => s.LocationId == h.LocationId);
        Assert.Equal("Magazijn Antwerpen", stopDto.LocationName);
        Assert.Equal("Noorderlaan 10", stopDto.Address);
        Assert.Equal("Poort B", stopDto.Gate);
        Assert.Equal("Kade 7", stopDto.Dock);
        Assert.Equal("Ma 08:00–12:00, 13:00–17:00", stopDto.OpeningHoursSummary);
        Assert.Null(stopDto.AccessCode);
        Assert.NotNull(stopDto.SnapshotAt);
    }

    [Fact]
    public async Task Create_InputInstructions_WinOverLocationInstructions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var stop = Stop(StopType.Loading, h.LocationId) with
        {
            Instructions = "Eigen instructie",
            LoadingInstructions = "Eigen laadinstructie",
        };
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, stop, Stop(StopType.Unloading, city: "Gent")), CancellationToken.None);

        var entity = await h.Db.Context.TransportOrderStops.SingleAsync(s => s.LocationId == h.LocationId);
        Assert.Equal("Eigen instructie", entity.Instructions);
        Assert.Equal("Eigen laadinstructie", entity.LoadingInstructions);
        // Fields the input left empty still receive the location's value.
        Assert.Equal("Alfapass verplicht", entity.AccessInstructions);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
    }

    [Fact]
    public async Task EditLocation_AfterOrderCreated_DoesNotRewriteOrderDetail()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")),
            CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        // Master edit AFTER the order exists.
        var location = await h.Db.Context.Locations.SingleAsync(l => l.Id == h.LocationId);
        location.Name = "Magazijn Rotterdam";
        location.Street = "Havenweg";
        location.HouseNumber = "99";
        location.City = "Rotterdam";
        location.Gate = "Poort Z";
        location.LoadingInstructions = "Gewijzigde instructie";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var detail = await h.Sut.GetByIdAsync(created.Order!.Id, CancellationToken.None);
        var stop = detail!.Stops.Single(s => s.LocationId == h.LocationId);
        Assert.Equal("Magazijn Antwerpen", stop.LocationName);
        Assert.Equal("Noorderlaan 10", stop.Address);
        Assert.Equal("Antwerpen", stop.City);
        Assert.Equal("Poort B", stop.Gate);
        Assert.Equal("Laden enkel aan dok 5", stop.LoadingInstructions);
    }

    /// <summary>
    /// Renamed in wave 1 (C-01): there is no rebuild any more — an echoed id now identifies the
    /// stop and it is updated IN PLACE. The carry-over rule this test guards is unchanged: an
    /// unchanged master-location stop keeps its frozen snapshot instead of silently re-copying
    /// live master data.
    /// </summary>
    [Fact]
    public async Task Update_EchoedStopId_KeepsTheSnapshotOfThePreservedStop()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")),
            CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        // Master edit between create and the (unrelated) order update.
        var location = await h.Db.Context.Locations.SingleAsync(l => l.Id == h.LocationId);
        location.Name = "Magazijn Rotterdam";
        location.Gate = "Poort Z";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var update = UpdateFrom(created.Order!, created.Order!.Stops.Select(EchoStop).ToList())
            with { Notes = "Alleen notities gewijzigd" };
        var updated = await h.Sut.UpdateAsync(created.Order!.Id, update, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var stop = updated.Order!.Stops.Single(s => s.LocationId == h.LocationId);
        // Rows were rebuilt (fresh ids) but the snapshot rode along — NOT re-copied live.
        Assert.Equal("Magazijn Antwerpen", stop.LocationName);
        Assert.Equal("Poort B", stop.Gate);
        Assert.Equal(Now.UtcDateTime, stop.SnapshotAt);
    }

    [Fact]
    public async Task Update_RefreshSnapshot_CopiesCurrentMasterData_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")),
            CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var location = await h.Db.Context.Locations.SingleAsync(l => l.Id == h.LocationId);
        location.Name = "Magazijn Rotterdam";
        location.Gate = "Poort Z";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var stops = created.Order!.Stops
            .Select(s => s.LocationId == h.LocationId
                ? EchoStop(s) with { RefreshSnapshot = true }
                : EchoStop(s))
            .ToList();
        var updated = await h.Sut.UpdateAsync(created.Order!.Id, UpdateFrom(created.Order!, stops), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var stop = updated.Order!.Stops.Single(s => s.LocationId == h.LocationId);
        Assert.Equal("Magazijn Rotterdam", stop.LocationName);
        Assert.Equal("Poort Z", stop.Gate);

        var audit = await h.Db.Context.AuditLogs
            .SingleAsync(a => a.EntityType == "TransportOrder" && a.Action == "StopSnapshotRefreshed");
        Assert.Contains("Magazijn Rotterdam", audit.NewValuesJson);
    }

    [Fact]
    public async Task Update_ChangedLocationId_TakesFreshSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherLocationId = Guid.NewGuid();
        h.Db.Context.Locations.Add(new Location
        {
            Id = otherLocationId, TenantId = h.TenantId, Code = "LOC-2", Name = "Depot Gent",
            Street = "Dokstraat", HouseNumber = "5", PostalCode = "9000", City = "Gent", CountryCode = "BE",
            Type = LocationType.Depot, IsActive = true, Gate = "Ingang 3",
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, Stop(StopType.Loading, h.LocationId), Stop(StopType.Unloading, city: "Gent")),
            CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var stops = created.Order!.Stops
            .Select(s => s.LocationId == h.LocationId
                ? EchoStop(s) with { LocationId = otherLocationId }
                : EchoStop(s))
            .ToList();
        var updated = await h.Sut.UpdateAsync(created.Order!.Id, UpdateFrom(created.Order!, stops), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        var stop = updated.Order!.Stops.Single(s => s.LocationId == otherLocationId);
        Assert.Equal("Depot Gent", stop.LocationName);
        Assert.Equal("Dokstraat 5", stop.Address);
        Assert.Equal("Ingang 3", stop.Gate);
    }

    [Fact]
    public async Task FreeAddressStop_GetsNoSnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var free = Stop(StopType.Unloading, city: "Gent") with { Address = "Veldstraat 1", LocationName = "Bouwwerf" };
        var created = await h.Sut.CreateAsync(
            Request(h.CustomerId, Stop(StopType.Loading, h.LocationId), free), CancellationToken.None);

        var entity = await h.Db.Context.TransportOrderStops.SingleAsync(s => s.LocationId == null);
        Assert.Equal("Bouwwerf", entity.LocationName);
        Assert.Equal("Veldstraat 1", entity.Address);
        Assert.Null(entity.ContactName);
        Assert.Null(entity.Gate);
        Assert.Null(entity.SnapshotAt);
        Assert.Equal(TransportOrderOperationOutcome.Success, created.Outcome);
    }

    [Fact]
    public async Task DetailDto_AccessCode_RequiresLocationsViewSensitive()
    {
        // Fail-closed: no permission service (or a denying one) → null.
        var denied = await SeedAsync(new InventoryTestFactory.DenyAllPermissionService());
        using (denied.Db)
        {
            var created = await denied.Sut.CreateAsync(
                Request(denied.CustomerId, Stop(StopType.Loading, denied.LocationId), Stop(StopType.Unloading, city: "Gent")),
                CancellationToken.None);
            Assert.Null(created.Order!.Stops.Single(s => s.LocationId == denied.LocationId).AccessCode);
        }

        var allowed = await SeedAsync(new InventoryTestFactory.AllowAllPermissionService());
        using (allowed.Db)
        {
            var created = await allowed.Sut.CreateAsync(
                Request(allowed.CustomerId, Stop(StopType.Loading, allowed.LocationId), Stop(StopType.Unloading, city: "Gent")),
                CancellationToken.None);
            Assert.Equal("1234#", created.Order!.Stops.Single(s => s.LocationId == allowed.LocationId).AccessCode);
        }
    }
}
