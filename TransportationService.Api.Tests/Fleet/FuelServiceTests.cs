using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class FuelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, FuelService Sut, Guid TenantId, Guid VehicleId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1",
            OdometerKm = 80_000, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new FuelService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)));
        return new Harness(db, sut, tenantId, vehicleId);
    }

    private static CreateFuelTransactionRequest Fill(DateOnly date, decimal litres, decimal amount, int? odometer, bool fullTank = true) =>
        new(null, null, date, litres, amount, odometer, "Total Antwerpen", fullTank, null);

    [Fact]
    public async Task Create_FirstFill_HasNoConsumption_AndAdvancesVehicleOdometer()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);

        Assert.Equal(FuelOperationOutcome.Success, result.Outcome);
        Assert.Null(result.Transaction!.ConsumptionLPer100Km);
        Assert.Equal(1.5m, result.Transaction.PricePerLitre);

        var vehicle = await h.Db.Context.Vehicles.FindAsync(h.VehicleId);
        Assert.Equal(81_000, vehicle!.OdometerKm);
    }

    [Fact]
    public async Task Create_SecondFullTank_ComputesConsumptionBetweenOdometerReadings()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);

        var second = await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 8), 300m, 460m, 82_000), CancellationToken.None);

        // 300 litres over 1 000 km = 30.0 l/100km.
        Assert.Equal(30.0m, second.Transaction!.ConsumptionLPer100Km);
        Assert.Empty(second.Transaction.Warnings);
    }

    [Fact]
    public async Task Create_PartialFill_SkipsConsumption()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);

        var partial = await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 8), 100m, 150m, 82_000, fullTank: false), CancellationToken.None);

        Assert.Null(partial.Transaction!.ConsumptionLPer100Km);
    }

    [Fact]
    public async Task Create_OdometerLowerThanPrevious_IsFlagged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);

        var wrong = await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 8), 300m, 460m, 80_500), CancellationToken.None);

        Assert.Contains(FuelWarningCode.OdometerLowerThanPrevious, wrong.Transaction!.Warnings);
        Assert.Null(wrong.Transaction.ConsumptionLPer100Km);
    }

    [Fact]
    public async Task Overview_FlagsConsumptionOutlier_OnceEnoughSamplesExist()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Three normal fills (30 l/100km) and one runaway fill (60 l/100km).
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 6, 1), 300m, 450m, 81_000), CancellationToken.None);
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 6, 8), 300m, 450m, 82_000), CancellationToken.None);
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 6, 15), 300m, 450m, 83_000), CancellationToken.None);
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 6, 22), 300m, 450m, 84_000), CancellationToken.None);
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 6, 29), 600m, 900m, 85_000), CancellationToken.None);

        var overview = await h.Sut.ListForVehicleAsync(h.VehicleId, CancellationToken.None);

        var outlier = overview!.Items.Single(t => t.Litres == 600m);
        Assert.Contains(FuelWarningCode.ConsumptionAboveAverage, outlier.Warnings);
        Assert.DoesNotContain(overview.Items.Where(t => t.Litres == 300m),
            t => t.Warnings.Contains(FuelWarningCode.ConsumptionAboveAverage));
    }

    [Fact]
    public async Task Overview_ComputesAggregates_NewestFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 8), 300m, 460m, 82_000), CancellationToken.None);

        var overview = await h.Sut.ListForVehicleAsync(h.VehicleId, CancellationToken.None);

        Assert.Equal(600m, overview!.TotalLitres);
        Assert.Equal(910m, overview.TotalAmount);
        Assert.Equal(30.0m, overview.AverageConsumptionLPer100Km);
        Assert.Equal(new DateOnly(2026, 7, 8), overview.Items[0].TransactionDate);
    }

    [Fact]
    public async Task Overview_ForeignVehicle_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        Assert.Null(await h.Sut.ListForVehicleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Create_NonPositiveLitres_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 0m, 0m, null), CancellationToken.None);

        Assert.Equal(FuelOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Create_ForeignTankCard_ReturnsInvalidReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId,
            new CreateFuelTransactionRequest(null, Guid.NewGuid(), new(2026, 7, 1), 300m, 450m, null, null, true, null),
            CancellationToken.None);

        Assert.Equal(FuelOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task RecentWarnings_JoinsVehicle_AndIsolatesTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 8), 300m, 460m, 80_500), CancellationToken.None);

        // Foreign-tenant anomaly must stay invisible.
        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        h.Db.Context.FuelTransactions.AddRange(
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle, TransactionDate = new(2026, 7, 1), Litres = 100m, TotalAmount = 150m, OdometerKm = 50_000 },
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle, TransactionDate = new(2026, 7, 2), Litres = 100m, TotalAmount = 150m, OdometerKm = 40_000 });
        await h.Db.Context.SaveChangesAsync();

        var warnings = await h.Sut.ListRecentWarningsAsync(10, CancellationToken.None);

        var single = Assert.Single(warnings);
        Assert.Equal("VRT-0001", single.VehicleInternalNumber);
        Assert.Contains(FuelWarningCode.OdometerLowerThanPrevious, single.Warnings);
    }

    [Fact]
    public async Task Update_Correction_Reevaluates()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 1), 300m, 450m, 81_000), CancellationToken.None);
        var wrong = await h.Sut.CreateForVehicleAsync(h.VehicleId, Fill(new(2026, 7, 8), 300m, 460m, 80_500), CancellationToken.None);

        var corrected = await h.Sut.UpdateAsync(wrong.Transaction!.Id,
            new UpdateFuelTransactionRequest(null, null, new(2026, 7, 8), 300m, 460m, 82_000, "Total Antwerpen", true, null),
            CancellationToken.None);

        Assert.Empty(corrected.Transaction!.Warnings);
        Assert.Equal(30.0m, corrected.Transaction.ConsumptionLPer100Km);
    }
}
