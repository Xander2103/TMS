using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Dtos;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Orders.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Dossiers;

/// <summary>
/// One-page dossier intake (2026-09): POST /api/transport-orders with an explicit
/// <c>ActivityTypeId</c> creates the order + stops + cargo AND a wrapper dossier whose first
/// activity carries the CHOSEN transport type (Distributie/Kraantransport/…) instead of the
/// system default — all in the existing single atomic save.
/// </summary>
public class OrderIntakeActivityTypeTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 02, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        public TransportOrderService Orders(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new TransportOrderService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        }
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DossierNumberPrefix = "DOS-", DossierNumberNextValue = 1,
            OrderNumberPrefix = "ORD-", OrderNumberNextValue = 1,
        });
        db.Context.LegalEntities.Add(new LegalEntity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Transport BV", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Van Caudenberg BV", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId);
    }

    private static async Task<Guid> TypeIdAsync(Harness h, string code) =>
        (await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == code)).Id;

    private static CreateTransportOrderRequest IntakeRequest(Harness h, Guid? activityTypeId) => new(
        h.CustomerId, "REF-INTAKE", new DateOnly(2026, 9, 3), "12 paletten bouwmateriaal",
        null, null, null, null, null, false, false, null, null,
        Stops:
        [
            new TransportOrderStopInput(StopType.Loading, null, "Magazijn Hoeilaart", "Waversesteenweg 78", "1560", "Hoeilaart", "BE",
                new DateTime(2026, 9, 3, 8, 0, 0), new DateTime(2026, 9, 3, 10, 0, 0), null, null),
            new TransportOrderStopInput(StopType.Unloading, null, "Werf Gent", "Dok Noord 4", "9000", "Gent", "BE",
                new DateTime(2026, 9, 3, 13, 0, 0), new DateTime(2026, 9, 3, 15, 0, 0), null, null),
        ],
        CargoItems: [new CargoItemInput("Bouwmateriaal", null, 12, "Pallets", null)],
        ActivityTypeId: activityTypeId);

    [Fact]
    public async Task Intake_WithDistributionType_WrapsDossierWithThatActivityType_AndPersistsStopsAndCargo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var distributieId = await TypeIdAsync(h, "DISTRIBUTIE");

        var result = await h.Orders().CreateAsync(IntakeRequest(h, distributieId), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.NotNull(result.Order!.DossierId);
        var wrapper = await h.Db.Context.TransportDossiers.SingleAsync(d => d.OriginTransportOrderId == result.Order.Id);
        Assert.Equal("REF-INTAKE", wrapper.CustomerReference);
        Assert.Equal(new DateOnly(2026, 9, 3), wrapper.DossierDate);
        var activity = await h.Db.Context.DossierActivities.SingleAsync(a => a.DossierId == wrapper.Id);
        Assert.Equal(distributieId, activity.ActivityTypeId);
        Assert.Equal(result.Order.Id, activity.LinkedTransportOrderId);
        // The intake really created the operational data — no empty shell dossier.
        Assert.Equal(2, result.Order.Stops.Count);
        Assert.Contains(result.Order.Stops, s => s.City == "Gent");
        Assert.Single(result.Order.CargoItems);
    }

    [Fact]
    public async Task Intake_WithoutActivityType_KeepsSystemDefaultTransportType()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Orders().CreateAsync(IntakeRequest(h, null), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        var wrapper = await h.Db.Context.TransportDossiers.SingleAsync(d => d.OriginTransportOrderId == result.Order!.Id);
        var activity = await h.Db.Context.DossierActivities.SingleAsync(a => a.DossierId == wrapper.Id);
        var type = await h.Db.Context.ActivityTypes.SingleAsync(t => t.Id == activity.ActivityTypeId);
        Assert.True(type.IsSystemDefaultTransport);
        Assert.Equal("DIRECT_TRANSPORT", type.Code);
    }

    [Fact]
    public async Task Intake_WithTypeWithoutStops_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var opslagId = await TypeIdAsync(h, "OPSLAG"); // HasStops = false in the seed

        var result = await h.Orders().CreateAsync(IntakeRequest(h, opslagId), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("transporttype", result.Error!);
        // Nothing was half-created.
        Assert.Empty(await h.Db.Context.TransportOrders.Where(o => o.TenantId == h.TenantId).ToListAsync());
        Assert.Empty(await h.Db.Context.TransportDossiers.Where(d => d.TenantId == h.TenantId).ToListAsync());
    }

    [Fact]
    public async Task Intake_WithInactiveOrUnknownType_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var distributie = await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == "DISTRIBUTIE");
        distributie.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        var inactive = await h.Orders().CreateAsync(IntakeRequest(h, distributie.Id), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidReference, inactive.Outcome);

        var unknown = await h.Orders().CreateAsync(IntakeRequest(h, Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.InvalidReference, unknown.Outcome);
    }

    [Fact]
    public async Task Intake_CraneWithDuration_StoresDurationOnWrapperActivity()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var kraanId = await TypeIdAsync(h, "KRAANTRANSPORT"); // HasStops + AllowsDuration in the seed

        var result = await h.Orders().CreateAsync(
            IntakeRequest(h, kraanId) with { ActivityDurationHours = 4.5m, CraneRequired = true }, CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.Success, result.Outcome);
        Assert.True(result.Order!.CraneRequired);
        var wrapper = await h.Db.Context.TransportDossiers.SingleAsync(d => d.OriginTransportOrderId == result.Order.Id);
        var activity = await h.Db.Context.DossierActivities.SingleAsync(a => a.DossierId == wrapper.Id);
        Assert.Equal(kraanId, activity.ActivityTypeId);
        // Duration lands on the REAL activity — never duplicated onto the order.
        Assert.Equal(4.5m, activity.DurationHours);
    }

    [Fact]
    public async Task Intake_DurationWithoutIt_StaysNull_AndDurationOnNonDurationType_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var kraanId = await TypeIdAsync(h, "KRAANTRANSPORT");
        var directId = await TypeIdAsync(h, "DIRECT_TRANSPORT"); // AllowsDuration = false

        var withoutDuration = await h.Orders().CreateAsync(IntakeRequest(h, kraanId), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, withoutDuration.Outcome);
        var wrapper = await h.Db.Context.TransportDossiers.SingleAsync(d => d.OriginTransportOrderId == withoutDuration.Order!.Id);
        Assert.Null((await h.Db.Context.DossierActivities.SingleAsync(a => a.DossierId == wrapper.Id)).DurationHours);

        var wrongType = await h.Orders().CreateAsync(
            IntakeRequest(h, directId) with { ActivityDurationHours = 2m }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, wrongType.Outcome);
        Assert.Contains("duur", wrongType.Error!);

        var negative = await h.Orders().CreateAsync(
            IntakeRequest(h, kraanId) with { ActivityDurationHours = -1m }, CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.ValidationFailed, negative.Outcome);
        // Same rule set as DossierActivityService.ValidateScalars — and NOTHING half-created.
        Assert.Single(await h.Db.Context.TransportOrders.Where(o => o.TenantId == h.TenantId).ToListAsync());
        Assert.Single(await h.Db.Context.TransportDossiers.Where(d => d.TenantId == h.TenantId).ToListAsync());
    }

    [Fact]
    public async Task Intake_ActivityTypeOfAnotherTenant_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(h.Db.Context, new DevTenantContext(otherTenantId)).EnsureSeededAsync(CancellationToken.None);
        var foreignDistributieId = (await h.Db.Context.ActivityTypes
            .SingleAsync(t => t.TenantId == otherTenantId && t.Code == "DISTRIBUTIE")).Id;

        var result = await h.Orders().CreateAsync(IntakeRequest(h, foreignDistributieId), CancellationToken.None);

        Assert.Equal(TransportOrderOperationOutcome.InvalidReference, result.Outcome);
    }
}
