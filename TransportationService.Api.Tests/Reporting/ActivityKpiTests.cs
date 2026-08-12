using TransportationService.Api.Modules.Dossiers.Entities;
using TransportationService.Api.Modules.Incidents.Entities;
using TransportationService.Api.Modules.Locations.Entities;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Reporting.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Modules.Warehousing.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Reporting;

/// <summary>
/// P11 activity-based KPIs. Core acceptance: one dossier containing a crane AND a plateau
/// activity contributes to BOTH rows independently; revenue follows the linked order's
/// AgreedPrice; redeliveries count by the redelivery order's OrderDate.
/// </summary>
public class ActivityKpiTests
{
    private static readonly DateOnly JulFrom = new(2026, 07, 20);
    private static readonly DateOnly JulTo = new(2026, 07, 24);

    private sealed record Harness(
        SqliteTestDbContext Db, ActivityKpiService Sut, Guid TenantId,
        Guid CraneTypeId, Guid PlateauTypeId, Guid CraneOrderId);

    /// <summary>
    /// Deterministic picture:
    /// - One dossier with a crane activity (planned 20/07, linked order €1000) and a plateau
    ///   activity (planned 21/07, no linked order). Both types share KpiCategory "Kraan".
    /// - One extra crane activity planned 20/08 (out of the July range) linked to a €400 order.
    /// - One incident with a redelivery order dated 22/07 (not linked to any activity).
    /// - One storage stay 20/07 10:00 → 21/07 12:00 → 2 started pallet-days in the July range.
    /// </summary>
    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var craneTypeId = Guid.NewGuid();
        var plateauTypeId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        var craneOrderId = Guid.NewGuid();
        var augustOrderId = Guid.NewGuid();
        var redeliveryOrderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant
        {
            Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow,
        });
        db.Context.Customers.Add(new Customer
        {
            Id = customerId, TenantId = tenantId, CustomerNumber = "KL-A", Name = "Alpha", IsActive = true,
        });

        db.Context.ActivityTypes.AddRange(
            new ActivityType
            {
                Id = craneTypeId, TenantId = tenantId, Code = "KRAANWERK", Name = "Kraanwerk",
                KpiCategory = "Kraan", SortOrder = 1, IsActive = true, AllowsDuration = true,
            },
            new ActivityType
            {
                Id = plateauTypeId, TenantId = tenantId, Code = "PLATEAU", Name = "Plateauwerk",
                KpiCategory = "Kraan", SortOrder = 2, IsActive = true,
            });

        db.Context.TransportOrders.AddRange(
            new TransportOrder
            {
                Id = craneOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-1",
                OrderDate = JulFrom, Status = TransportOrderStatus.Completed, GoodsDescription = "x",
                AgreedPrice = 1000m,
            },
            new TransportOrder
            {
                Id = augustOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-2",
                OrderDate = new DateOnly(2026, 08, 20), Status = TransportOrderStatus.Confirmed,
                GoodsDescription = "x", AgreedPrice = 400m,
            },
            new TransportOrder
            {
                Id = redeliveryOrderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-HER",
                OrderDate = new DateOnly(2026, 07, 22), Status = TransportOrderStatus.Confirmed,
                GoodsDescription = "herlevering", AgreedPrice = 150m,
            });

        db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = tenantId, DossierNumber = "DOS-1", Title = "Werf Nexans",
            CustomerId = customerId, DossierDate = JulFrom,
        });
        db.Context.DossierActivities.AddRange(
            new DossierActivity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId,
                ActivityTypeId = craneTypeId, Sequence = 1,
                PlannedDate = new DateOnly(2026, 07, 20), LinkedTransportOrderId = craneOrderId,
            },
            new DossierActivity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId,
                ActivityTypeId = plateauTypeId, Sequence = 2,
                PlannedDate = new DateOnly(2026, 07, 21),
            },
            new DossierActivity
            {
                Id = Guid.NewGuid(), TenantId = tenantId, DossierId = dossierId,
                ActivityTypeId = craneTypeId, Sequence = 3,
                PlannedDate = new DateOnly(2026, 08, 20), LinkedTransportOrderId = augustOrderId,
            });

        db.Context.Incidents.Add(new Incident
        {
            Id = Guid.NewGuid(), TenantId = tenantId, IncidentType = IncidentType.WrongDelivery,
            Title = "Mislukte levering", Description = "Klant gesloten",
            TransportOrderId = craneOrderId, LinkedRedeliveryOrderId = redeliveryOrderId,
        });

        var locationId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        db.Context.Locations.Add(new Location { Id = locationId, TenantId = tenantId, Name = "Depot", City = "Gent", IsActive = true });
        db.Context.Warehouses.Add(new Warehouse { Id = warehouseId, TenantId = tenantId, Name = "Magazijn A", LocationId = locationId, IsActive = true });
        db.Context.StorageStays.Add(new StorageStay
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PackageId = Guid.NewGuid(), WarehouseId = warehouseId,
            InAt = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            OutAt = new DateTime(2026, 07, 21, 12, 0, 0, DateTimeKind.Utc),
        });

        await db.Context.SaveChangesAsync();

        var sut = new ActivityKpiService(db.Context, new DevTenantContext(tenantId));
        return new Harness(db, sut, tenantId, craneTypeId, plateauTypeId, craneOrderId);
    }

    [Fact]
    public async Task OneDossier_CraneAndPlateau_ContributeToBothRows()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var report = await h.Sut.GetActivityKpisAsync(JulFrom, JulTo, CancellationToken.None);

        Assert.Equal(2, report.Rows.Count);

        var crane = report.Rows.Single(r => r.ActivityTypeId == h.CraneTypeId);
        Assert.Equal("KRAANWERK", crane.Code);
        Assert.Equal("Kraan", crane.KpiCategory);
        Assert.Equal(1, crane.ActivityCount); // the August activity is out of range
        Assert.Equal(1, crane.LinkedOrderCount);
        Assert.Equal(1000m, crane.Revenue);
        Assert.Equal(0, crane.RedeliveryCount); // redelivery order is not linked to an activity

        var plateau = report.Rows.Single(r => r.ActivityTypeId == h.PlateauTypeId);
        Assert.Equal(1, plateau.ActivityCount);
        Assert.Equal(0, plateau.LinkedOrderCount);
        Assert.Equal(0m, plateau.Revenue);

        // Row order follows the type's SortOrder.
        Assert.Equal(h.CraneTypeId, report.Rows[0].ActivityTypeId);
    }

    [Fact]
    public async Task Totals_Categories_Redeliveries_And_PalletDays()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var report = await h.Sut.GetActivityKpisAsync(JulFrom, JulTo, CancellationToken.None);

        Assert.Equal(2, report.Totals.ActivityCount);
        Assert.Equal(1, report.Totals.LinkedOrderCount);
        Assert.Equal(1000m, report.Totals.Revenue);
        Assert.Equal(1, report.Totals.RedeliveryCount); // redelivery order dated 22/07

        var category = Assert.Single(report.PerCategory);
        Assert.Equal("Kraan", category.KpiCategory);
        Assert.Equal(2, category.ActivityCount);
        Assert.Equal(1000m, category.Revenue);

        // Stay 20/07 10:00 → 21/07 12:00 = 26h → 2 started days.
        Assert.Equal(2m, report.PalletDays);
    }

    [Fact]
    public async Task PeriodFilter_ExcludesOutOfRangeActivitiesAndRedeliveries()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var report = await h.Sut.GetActivityKpisAsync(
            new DateOnly(2026, 08, 15), new DateOnly(2026, 08, 25), CancellationToken.None);

        var crane = Assert.Single(report.Rows);
        Assert.Equal(h.CraneTypeId, crane.ActivityTypeId);
        Assert.Equal(1, crane.ActivityCount);
        Assert.Equal(400m, crane.Revenue);
        Assert.Equal(0, report.Totals.RedeliveryCount); // redelivery order (22/07) out of range
        Assert.Null(report.PalletDays); // no stay overlaps August
    }

    [Fact]
    public async Task ActivityWithoutPlannedDate_FallsBackToCreationDate()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var typeId = Guid.NewGuid();
        var dossierId = Guid.NewGuid();
        h.Db.Context.ActivityTypes.Add(new ActivityType
        {
            Id = typeId, TenantId = h.TenantId, Code = "TRANSPORT", Name = "Transport", IsActive = true,
        });
        h.Db.Context.TransportDossiers.Add(new TransportDossier
        {
            Id = dossierId, TenantId = h.TenantId, DossierNumber = "DOS-2", Title = "Zonder datum",
        });
        h.Db.Context.DossierActivities.Add(new DossierActivity
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, DossierId = dossierId,
            ActivityTypeId = typeId, Sequence = 1, PlannedDate = null, // CreatedAt is stamped "now"
        });
        await h.Db.Context.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var report = await h.Sut.GetActivityKpisAsync(
            today.AddDays(-1), today.AddDays(1), CancellationToken.None);

        var transport = report.Rows.Single(r => r.ActivityTypeId == typeId);
        Assert.Equal(1, transport.ActivityCount);
    }

    [Fact]
    public async Task ActivityKpis_AreTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreign = new ActivityKpiService(h.Db.Context, new DevTenantContext(Guid.NewGuid()));

        var report = await foreign.GetActivityKpisAsync(JulFrom, JulTo, CancellationToken.None);

        Assert.Empty(report.Rows);
        Assert.Equal(0, report.Totals.ActivityCount);
        Assert.Equal(0, report.Totals.RedeliveryCount);
        Assert.Null(report.PalletDays);
        Assert.Empty(report.PerCategory);
    }
}
