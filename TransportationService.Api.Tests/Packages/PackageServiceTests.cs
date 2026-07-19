using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Packages.Dtos;
using TransportationService.Api.Modules.Packages.Entities;
using TransportationService.Api.Modules.Packages.Services;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Packages;

public class PackageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, PackageService Sut, Guid TenantId, Guid OrderId, Guid LoadStopId, Guid UnloadStopId)
    {
        public PackageService ForTenant(Guid tenantId)
        {
            var tenant = new DevTenantContext(tenantId);
            var user = new DevCurrentUserContext(null);
            var clock = new TestClock(Now);
            return new PackageService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, user),
                new PackageBarcodeService(Db.Context, tenant, user, clock),
                new PackageEventWriter(Db.Context, tenant, user, clock));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var loadStopId = Guid.NewGuid();
        var unloadStopId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageNumberPrefix = "PKG-", PackageNumberNextValue = 1,
        });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
            OrderDate = new(2026, 7, 22), Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
            Stops =
            [
                new TransportOrderStop { Id = loadStopId, TenantId = tenantId, Sequence = 1, StopType = StopType.Loading, City = "Antwerpen" },
                new TransportOrderStop { Id = unloadStopId, TenantId = tenantId, Sequence = 2, StopType = StopType.Unloading, City = "Rotterdam" },
            ],
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        var clock = new TestClock(Now);
        var sut = new PackageService(db.Context, tenant,
            new AuditService(db.Context, tenant, user),
            new PackageBarcodeService(db.Context, tenant, user, clock),
            new PackageEventWriter(db.Context, tenant, user, clock));
        return new Harness(db, sut, tenantId, orderId, loadStopId, unloadStopId);
    }

    private static CreatePackageRequest Request(string description = "Doos gereedschap") => new(description);

    [Fact]
    public async Task Create_ClaimsNumber_Barcode_RegistryRow_AndCustodyEvent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(h.OrderId, Request() with
        {
            LoadingStopId = h.LoadStopId, DeliveryStopId = h.UnloadStopId,
            ExternalBarcode = "CUST-777-ABC", WeightKg = 12.5m, UnitType = PackageUnitType.Pallet,
        }, CancellationToken.None);

        Assert.Equal(PackageOutcome.Success, result.Outcome);
        var dto = result.Package!;
        Assert.Equal("PKG-00001", dto.PackageNumber);
        Assert.StartsWith("PKG-00001-", dto.BarcodeValue);
        Assert.Equal("PKG-00001-".Length + 8, dto.BarcodeValue.Length); // number + 8-char random suffix
        Assert.Equal("CUST-777-ABC", dto.ExternalBarcode);
        Assert.Equal(PackageLifecycleStatus.Created, dto.Status);

        var barcodes = await h.Sut.ListBarcodesAsync(dto.Id, CancellationToken.None);
        Assert.Equal(2, barcodes.Count); // internal + external, both active
        Assert.All(barcodes, b => Assert.True(b.IsActive));

        var custody = h.Db.Context.PackageEvents.Single(e => e.PackageId == dto.Id);
        Assert.Equal(PackageEventType.Created, custody.EventType);
        Assert.Equal(PackageLifecycleStatus.Created, custody.NewStatus);
    }

    [Fact]
    public async Task Numbering_SurvivesStaleCounter_Race()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(h.OrderId, Request("Eerste"), CancellationToken.None);
        Assert.Equal("PKG-00001", first.Package!.PackageNumber);

        // A concurrent request advances the counter behind the tracked settings' back.
        await h.Db.Context.Database.ExecuteSqlRawAsync("UPDATE tenant_settings SET \"PackageNumberNextValue\" = 9");

        var second = await h.Sut.CreateAsync(h.OrderId, Request("Tweede"), CancellationToken.None);

        Assert.Equal(PackageOutcome.Success, second.Outcome);
        Assert.Equal("PKG-00009", second.Package!.PackageNumber);
        Assert.StartsWith("PKG-00009-", second.Package.BarcodeValue); // barcode re-claimed with the number
    }

    [Fact]
    public async Task DuplicateExternalBarcode_IsRefusedByConstraint()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(h.OrderId, Request("A") with { ExternalBarcode = "DUP-1" }, CancellationToken.None);

        var duplicate = await h.Sut.CreateAsync(h.OrderId, Request("B") with { ExternalBarcode = "DUP-1" }, CancellationToken.None);

        Assert.Equal(PackageOutcome.DuplicateBarcode, duplicate.Outcome);
        Assert.Equal(1, h.Db.Context.Packages.Count(p => p.TenantId == h.TenantId));
    }

    [Fact]
    public async Task Relabel_RetiresOldValue_KeepsHistory_AndNumberImmutable()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(h.OrderId, Request(), CancellationToken.None);
        var oldBarcode = created.Package!.BarcodeValue;

        var relabelled = await h.Sut.RelabelAsync(created.Package.Id, new RelabelPackageRequest("Etiket beschadigd"), CancellationToken.None);

        Assert.Equal(PackageOutcome.Success, relabelled.Outcome);
        Assert.NotEqual(oldBarcode, relabelled.Package!.BarcodeValue);
        Assert.Equal("PKG-00001", relabelled.Package.PackageNumber); // immutable

        var barcodes = await h.Sut.ListBarcodesAsync(created.Package.Id, CancellationToken.None);
        var retired = barcodes.Single(b => b.Value == oldBarcode);
        Assert.False(retired.IsActive);
        Assert.Equal("Etiket beschadigd", retired.RetireReason);
        Assert.NotNull(retired.RetiredAt);
        Assert.True(barcodes.Single(b => b.Value == relabelled.Package.BarcodeValue).IsActive);

        Assert.Contains(h.Db.Context.PackageEvents.Where(e => e.PackageId == created.Package.Id).ToList(),
            e => e.EventType == PackageEventType.Relabelled && e.Notes!.Contains(oldBarcode));
    }

    [Fact]
    public async Task StopPins_MustBelongToOrder_AndMatchType()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var wrongType = await h.Sut.CreateAsync(h.OrderId,
            Request() with { LoadingStopId = h.UnloadStopId }, CancellationToken.None);
        Assert.Equal(PackageOutcome.ValidationFailed, wrongType.Outcome);

        var foreignStop = await h.Sut.CreateAsync(h.OrderId,
            Request() with { DeliveryStopId = Guid.NewGuid() }, CancellationToken.None);
        Assert.Equal(PackageOutcome.ValidationFailed, foreignStop.Outcome);
    }

    [Fact]
    public async Task Cancel_RequiresReason_FollowsMachine_AndAppendsEvent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(h.OrderId, Request(), CancellationToken.None);

        var noReason = await h.Sut.CancelAsync(created.Package!.Id, new CancelPackageRequest(" "), CancellationToken.None);
        Assert.Equal(PackageOutcome.ValidationFailed, noReason.Outcome);

        var cancelled = await h.Sut.CancelAsync(created.Package.Id, new CancelPackageRequest("Klant annuleert"), CancellationToken.None);
        Assert.Equal(PackageLifecycleStatus.Cancelled, cancelled.Package!.Status);

        // Terminal: no further mutation.
        var again = await h.Sut.CancelAsync(created.Package.Id, new CancelPackageRequest("nogmaals"), CancellationToken.None);
        Assert.Equal(PackageOutcome.InvalidState, again.Outcome);
        var update = await h.Sut.UpdateAsync(created.Package.Id, new UpdatePackageRequest(
            "X", 1, PackageUnitType.Colli, null, null, null, null, null, null, null, null, null, null,
            true, false, false, false, null), CancellationToken.None);
        Assert.Equal(PackageOutcome.InvalidState, update.Outcome);
    }

    [Fact]
    public async Task Custody_IsAppendOnly_EventsAccumulate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(h.OrderId, Request(), CancellationToken.None);
        await h.Sut.RelabelAsync(created.Package!.Id, new RelabelPackageRequest("r1"), CancellationToken.None);
        await h.Sut.CancelAsync(created.Package.Id, new CancelPackageRequest("klaar"), CancellationToken.None);

        var events = h.Db.Context.PackageEvents
            .Where(e => e.PackageId == created.Package.Id)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.CreatedAt)
            .Select(e => e.EventType)
            .ToList();

        Assert.Equal(
            new[] { PackageEventType.Created, PackageEventType.Relabelled, PackageEventType.Cancelled },
            events);
    }

    [Fact]
    public async Task Packages_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(h.OrderId, Request(), CancellationToken.None);
        var foreign = h.ForTenant(Guid.NewGuid());

        Assert.Null(await foreign.GetByIdAsync(created.Package!.Id, CancellationToken.None));
        Assert.Empty(await foreign.ListForOrderAsync(h.OrderId, CancellationToken.None));
        Assert.Equal(PackageOutcome.NotFound,
            (await foreign.CancelAsync(created.Package.Id, new CancelPackageRequest("x"), CancellationToken.None)).Outcome);
    }
}
