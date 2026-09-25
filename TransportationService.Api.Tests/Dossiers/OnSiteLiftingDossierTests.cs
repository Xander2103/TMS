using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Dossiers.Dtos;
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
/// Master sprint 2026-09-21 (D2), dossier side: the <c>SupportsOnSiteWork</c> capability flag, the
/// dossier flow (activity → draft order → the order BECOMES on-site work on its PUT), the activity
/// duration as the source of the site stop's planned end, and the readiness gate.
/// </summary>
public class OnSiteLiftingDossierTests
{
    private static readonly DateTimeOffset Now = new(2026, 09, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid CustomerId)
    {
        private AuditService Audit(DevTenantContext tenant) => new(Db.Context, tenant, new DevCurrentUserContext(null));

        public TransportOrderService Orders(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new TransportOrderService(Db.Context, tenant, Audit(tenant), new TestClock(Now));
        }

        public DossierService Dossiers(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierService(Db.Context, tenant, Audit(tenant), new TestClock(Now));
        }

        public DossierActivityService Activities(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new DossierActivityService(Db.Context, tenant, Audit(tenant), Dossiers(tenantId), Orders(tenantId), new TestClock(Now));
        }

        public ActivityTypeService Types(Guid? tenantId = null)
        {
            var tenant = new DevTenantContext(tenantId ?? TenantId);
            return new ActivityTypeService(Db.Context, tenant, new ActivityTypeSeeder(Db.Context, tenant), Audit(tenant));
        }

        public DossierReadinessService Readiness() => new(Db.Context, new DevTenantContext(TenantId));
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
            Id = Guid.NewGuid(), TenantId = tenantId, LegalName = "Acme Kraanwerken BV", IsActive = true, IsDefault = true,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Bouwbedrijf Peeters", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        await new ActivityTypeSeeder(db.Context, new DevTenantContext(tenantId)).EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, customerId);
    }

    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, DateTimeKind.Utc);

    private static async Task<Guid> TypeIdAsync(Harness h, string code) =>
        (await h.Db.Context.ActivityTypes.SingleAsync(t => t.TenantId == h.TenantId && t.Code == code)).Id;

    /// <summary>The dossier flow: a dossier, an activity of the given type with its linked draft order.</summary>
    private static async Task<(Guid DossierId, Guid ActivityId, Guid OrderId)> ActivityWithOrderAsync(
        Harness h, string typeCode, decimal? durationHours)
    {
        var dossier = await h.Dossiers().CreateAsync(new SaveDossierRequest("Kraanwerk Dok Noord", CustomerId: h.CustomerId), CancellationToken.None);
        var detail = await h.Activities().AddAsync(dossier.Id,
            new SaveDossierActivityRequest(await TypeIdAsync(h, typeCode), "Dakspanten", DurationHours: durationHours, CreateLinkedOrder: true),
            CancellationToken.None);
        var activity = Assert.Single(detail!.Activities);
        return (dossier.Id, activity.Id, activity.LinkedTransportOrderId!.Value);
    }

    private static UpdateTransportOrderRequest ToOnSite(TransportOrderDetailDto order, DateTime? siteStart, bool manualEnd = false, DateTime? end = null) => new(
        order.CustomerId, order.CustomerReference, order.OrderDate, GoodsDescription: null,
        null, null, null, null, null, false, true, null, null,
        Stops:
        [
            new TransportOrderStopInput(StopType.Site, null, "Werf Dok Noord", "Dok Noord 4", "9000", "Gent", "BE",
                siteStart, end, null, null, Id: order.Stops.FirstOrDefault()?.Id, PlannedToIsManual: manualEnd),
        ],
        CraneJobKind: CraneJobKind.OnSiteLifting, WorkDescription: "Dakspanten plaatsen");

    [Fact]
    public async Task Seeder_FlagsKraantransportOnly_AndTheServiceRoundTripsTheFlag()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var types = await h.Types().ListAsync(includeInactive: false, CancellationToken.None);
        Assert.Equal(["KRAANTRANSPORT"], types.Where(t => t.SupportsOnSiteWork).Select(t => t.Code).ToArray());

        // A tenant's own type can carry the capability…
        var created = await h.Types().CreateAsync(
            new SaveActivityTypeRequest("MONTAGE", "Montage op locatie", HasStops: true, AllowsDuration: true, SupportsOnSiteWork: true),
            CancellationToken.None);
        Assert.True(created.SupportsOnSiteWork);

        // …a client that does not know the flag (null) never clears it; an explicit false does.
        var untouched = await h.Types().UpdateAsync(created.Id,
            new SaveActivityTypeRequest("MONTAGE", "Montage", HasStops: true, AllowsDuration: true), CancellationToken.None);
        Assert.True(untouched!.SupportsOnSiteWork);
        var cleared = await h.Types().UpdateAsync(created.Id,
            new SaveActivityTypeRequest("MONTAGE", "Montage", HasStops: true, AllowsDuration: true, SupportsOnSiteWork: false),
            CancellationToken.None);
        Assert.False(cleared!.SupportsOnSiteWork);
    }

    [Fact]
    public async Task DossierFlow_OrderBecomesOnSiteWork_OnlyWhenItsActivityTypeAllowsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var (dossierId, _, craneOrderId) = await ActivityWithOrderAsync(h, "KRAANTRANSPORT", durationHours: 4m);
        var craneOrder = await h.Orders().GetByIdAsync(craneOrderId, CancellationToken.None);
        Assert.True(craneOrder!.ActivitySupportsOnSiteWork);
        var updated = await h.Orders().UpdateAsync(craneOrderId, ToOnSite(craneOrder, At(22, 8)), CancellationToken.None);
        Assert.Equal(TransportOrderOperationOutcome.Success, updated.Outcome);
        Assert.Equal(CraneJobKind.OnSiteLifting, updated.Order!.CraneJobKind);
        Assert.Equal(At(22, 12), updated.Order.Stops[0].PlannedTo); // 08:00 + the activity's 4 h

        var dossier = await h.Dossiers().GetAsync(dossierId, CancellationToken.None);
        Assert.True(Assert.Single(dossier!.Activities).SupportsOnSiteWork);

        var (_, _, distributionOrderId) = await ActivityWithOrderAsync(h, "DISTRIBUTIE", durationHours: null);
        var distributionOrder = await h.Orders().GetByIdAsync(distributionOrderId, CancellationToken.None);
        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Orders().UpdateAsync(distributionOrderId, ToOnSite(distributionOrder!, At(22, 8)), CancellationToken.None));
        Assert.True(error.FieldErrors!.ContainsKey("craneJobKind"));
    }

    [Fact]
    public async Task ActivityDurationChange_RecomputesTheNonManualSiteStopEnd()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, activityId, orderId) = await ActivityWithOrderAsync(h, "KRAANTRANSPORT", durationHours: 4m);
        var order = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        var onSite = await h.Orders().UpdateAsync(orderId, ToOnSite(order!, At(22, 22)), CancellationToken.None);
        Assert.Equal(At(23, 2), onSite.Order!.Stops[0].PlannedTo);
        var versionBefore = onSite.Order.Version;

        await h.Activities().UpdateAsync(dossierId, activityId,
            new SaveDossierActivityRequest(await TypeIdAsync(h, "KRAANTRANSPORT"), "Dakspanten", DurationHours: 1.5m), CancellationToken.None);

        var after = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        Assert.Equal(At(22, 23, 30), after!.Stops[0].PlannedTo);
        Assert.Equal(1.5m, after.ActivityDurationHours);
        // An open order form must rebase instead of writing the old end back.
        Assert.NotEqual(versionBefore, after.Version);
    }

    [Fact]
    public async Task ActivityDurationChange_NeverOverwritesAManualEnd()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, activityId, orderId) = await ActivityWithOrderAsync(h, "KRAANTRANSPORT", durationHours: 4m);
        var order = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        var onSite = await h.Orders().UpdateAsync(orderId, ToOnSite(order!, At(22, 8), manualEnd: true, end: At(22, 17)), CancellationToken.None);
        Assert.Equal(At(22, 17), onSite.Order!.Stops[0].PlannedTo);

        await h.Activities().UpdateAsync(dossierId, activityId,
            new SaveDossierActivityRequest(await TypeIdAsync(h, "KRAANTRANSPORT"), "Dakspanten", DurationHours: 2m), CancellationToken.None);

        var after = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        Assert.Equal(At(22, 17), after!.Stops[0].PlannedTo);
        Assert.True(after.Stops[0].PlannedToIsManual);
    }

    [Fact]
    public async Task Readiness_OnSiteWork_NeverAsksForALoadingOrUnloadingLocation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, _, orderId) = await ActivityWithOrderAsync(h, "KRAANTRANSPORT", durationHours: 4m);

        // The fresh draft is still an ordinary order: the existing route gate applies.
        var before = await h.Readiness().EvaluateAsync(dossierId, CancellationToken.None);
        Assert.Contains(before, i => i.Code == "order.confirm.stops");

        var order = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        await h.Orders().UpdateAsync(orderId, ToOnSite(order!, At(22, 8)), CancellationToken.None);

        var after = await h.Readiness().EvaluateAsync(dossierId, CancellationToken.None);
        Assert.DoesNotContain(after, i => i.Code is "order.confirm.stops" or "order.confirm.site");
    }

    [Fact]
    public async Task TenantIsolation_AnotherTenantCannotTouchTheActivityOrItsOrder()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var (dossierId, activityId, orderId) = await ActivityWithOrderAsync(h, "KRAANTRANSPORT", durationHours: 4m);
        var order = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        await h.Orders().UpdateAsync(orderId, ToOnSite(order!, At(22, 8)), CancellationToken.None);

        var otherTenantId = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();

        // Unknown dossier for the other tenant → null, and the site stop end is untouched.
        var result = await h.Activities(otherTenantId).UpdateAsync(dossierId, activityId,
            new SaveDossierActivityRequest(await TypeIdAsync(h, "KRAANTRANSPORT"), "Gekaapt", DurationHours: 9m), CancellationToken.None);
        Assert.Null(result);
        Assert.Null(await h.Orders(otherTenantId).GetByIdAsync(orderId, CancellationToken.None));
        var untouched = await h.Orders().GetByIdAsync(orderId, CancellationToken.None);
        Assert.Equal(At(22, 12), untouched!.Stops[0].PlannedTo);
    }
}
