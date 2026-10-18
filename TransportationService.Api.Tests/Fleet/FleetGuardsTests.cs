using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Controllers;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

/// <summary>
/// Fleet remediation guards: FL-B1 delete guard + historical identity, FL-M1 identifiers,
/// FL-M5 negatives, FL-M6 odometer integrity, FL-M7 damage trip link/attachment, FL-M11 KPI
/// shaping, FL-M13 replace-vehicle, FL-M12/Wagenparkbeheerder role templates.
/// </summary>
public class FleetGuardsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TripDate = new(2026, 10, 21);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, VehicleService Vehicles, TrailerService Trailers, TripService Trips,
        Guid DriverId, Guid OrderId, Guid VehicleId, Guid SpareVehicleId, Guid TrailerId)
    {
        public TransportationDbContext Context => Db.Context;
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var spareId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumberPrefix = "RIT-", TripNumberNextValue = 1,
            VehicleNumberPrefix = "VRT-", VehicleNumberNextValue = 10, TrailerNumberPrefix = "OPL-", TrailerNumberNextValue = 10,
            QualificationExpiryWarningDays = 30,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.AddRange(
            new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-TFT-001", OdometerKm = 125_400, IsActive = true },
            new Vehicle { Id = spareId, TenantId = tenantId, InternalNumber = "VRT-0002", LicensePlate = "1-TFT-002", IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "Q-OPL-001", IsActive = true });
        db.Context.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });
        db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = tenantId, CustomerId = customerId, OrderNumber = "ORD-0001",
            OrderDate = TripDate, Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var trips = new TripService(db.Context, tenant, audit,
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new NotificationService(db.Context, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(db.Context, tenant),
            CostingTestFactory.Create(db.Context, tenant, clock),
            TripPackageTestFactory.Create(db.Context, tenant, clock));
        return new Harness(db, tenantId,
            new VehicleService(db.Context, tenant, audit, clock),
            new TrailerService(db.Context, tenant, audit, clock),
            trips, driverId, orderId, vehicleId, spareId, trailerId);
    }

    private static async Task<Guid> PlannedTripAsync(Harness h, Guid? trailerId = null)
    {
        var created = await h.Trips.CreateAsync(
            new CreateTripRequest(TripDate, h.DriverId, h.VehicleId, trailerId, null, null, null, [h.OrderId]), CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, created.Outcome);
        var planned = await h.Trips.ChangeStatusAsync(created.Trip!.Id, TripStatus.Planned, false, false, null, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.Success, planned.Outcome);
        return created.Trip.Id;
    }

    private static CreateVehicleRequest CreateRequest(string plate, string? vin = null, decimal? payload = 10_000m, int odometer = 1000) => new(
        plate, vin, null, "DAF", "XF", 2022, null, FuelType.Diesel, null,
        18_000m, payload, null, null, null, null, odometer, null, false, false, false, false,
        VehicleOwnershipType.Owned, null, null, null);

    private static UpdateVehicleRequest UpdateRequest(VehicleDetailDto v, int odometer, string? reason = null) => new(
        v.LicensePlate, v.Vin, v.CategoryId, v.Brand, v.Model, v.Year, v.FirstRegistrationDate, v.FuelType, v.EmissionClass,
        v.GrossVehicleWeightKg, v.PayloadKg, v.LengthMeters, v.WidthMeters, v.HeightMeters, v.VolumeM3, odometer,
        v.ConsumptionLPer100Km, v.HasCrane, v.HasRefrigeration, v.HasTailLift, v.AdrSuitable, v.OwnershipType,
        v.OperationalStatus, v.IsActive, v.Notes, v.StatusReason, OdometerCorrectionReason: reason);

    // ---------------- FL-B1 ----------------

    [Fact]
    public async Task Delete_UnusedVehicle_IsAllowed()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Vehicles.DeleteAsync(h.SpareVehicleId, CancellationToken.None);

        Assert.Equal(FleetDeleteOutcome.Deleted, result.Outcome);
        Assert.Null(await h.Vehicles.GetByIdAsync(h.SpareVehicleId, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_VehicleWithPlannedTrip_IsRefused_WithReferencesAndMessage()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tripId = await PlannedTripAsync(h);
        h.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, Description = "Groot onderhoud",
            Status = MaintenanceStatus.Planned, ScheduledDate = TripDate,
        });
        await h.Context.SaveChangesAsync();

        var result = await h.Vehicles.DeleteAsync(h.VehicleId, CancellationToken.None);

        Assert.Equal(FleetDeleteOutcome.Refused, result.Outcome);
        Assert.Equal(1, result.References!.PlannedTrips);
        Assert.Equal(1, result.References.Maintenance);
        Assert.Equal(tripId, Assert.Single(result.References.UpcomingTrips).Id);
        Assert.Contains("kan niet worden verwijderd", result.Message);
        Assert.Contains("1 geplande rit", result.Message);
        Assert.Contains("1 onderhoudsbeurt", result.Message);
        Assert.Contains("Deactiveer", result.Message);
        Assert.NotNull(await h.Vehicles.GetByIdAsync(h.VehicleId, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_VehicleWithOnlyHistory_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Context.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0099", VehicleId = h.VehicleId,
            TripDate = new DateOnly(2026, 9, 1), Status = TripStatus.Completed,
        });
        await h.Context.SaveChangesAsync();

        var result = await h.Vehicles.DeleteAsync(h.VehicleId, CancellationToken.None);

        Assert.Equal(FleetDeleteOutcome.Refused, result.Outcome);
        Assert.Equal(1, result.References!.CompletedTrips);
        Assert.Contains("1 afgeronde rit", result.Message);
    }

    [Fact]
    public async Task Delete_TrailerWithPlannedTrip_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await PlannedTripAsync(h, h.TrailerId);

        var result = await h.Trailers.DeleteAsync(h.TrailerId, CancellationToken.None);

        Assert.Equal(FleetDeleteOutcome.Refused, result.Outcome);
        Assert.StartsWith("Deze oplegger kan niet worden verwijderd", result.Message);
    }

    [Fact]
    public async Task References_CountEveryFleetRecord()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Context.FleetDocuments.Add(new FleetDocument { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, DocumentType = FleetDocumentType.Insurance, DocumentNumber = "P-1" });
        h.Context.DamageReports.Add(new DamageReport { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, Description = "Kras", IncidentDate = TripDate });
        h.Context.FuelTransactions.Add(new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = TripDate, Litres = 10, TotalAmount = 15, OdometerKm = 125_500 });
        await h.Context.SaveChangesAsync();

        var refs = await h.Vehicles.GetReferencesAsync(h.VehicleId, CancellationToken.None);

        Assert.NotNull(refs);
        Assert.Equal((1, 1, 1), (refs!.Documents, refs.Damage, refs.Fuel));
        Assert.True(refs.HasReferences);
        Assert.Null(await h.Vehicles.GetReferencesAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SoftDeletedVehicle_StaysIdentifiableOnItsTrip()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tripId = await PlannedTripAsync(h);
        // A vehicle deleted before the guard existed (legacy soft delete).
        var vehicle = await h.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId);
        vehicle.IsDeleted = true;
        await h.Context.SaveChangesAsync();
        h.Context.ChangeTracker.Clear();

        var trip = await h.Trips.GetByIdAsync(tripId, CancellationToken.None);

        Assert.Equal("VRT-0001", trip!.VehicleNumber);
    }

    // ---------------- FL-M1 ----------------

    [Theory]
    [InlineData("1 tft 001")]
    [InlineData("1TFT001")]
    [InlineData("1-tft-001")]
    public async Task Create_SamePlateWithOtherSeparators_IsRefused_NamingTheExistingVehicle(string plate)
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Vehicles.CreateAsync(CreateRequest(plate), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.DuplicateLicensePlate, result.Outcome);
        Assert.Contains("1-TFT-001", result.Message);
        Assert.Contains("VRT-0001", result.Message);
    }

    [Fact]
    public async Task Create_DuplicateVin_IsRefused_ButUnknownVinsDoNotCollide()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Vehicles.CreateAsync(CreateRequest("1-NEW-001", vin: "xlr test vin 01"), CancellationToken.None);
        Assert.Equal(VehicleOperationOutcome.Success, first.Outcome);
        Assert.Equal("XLRTESTVIN01", first.Vehicle!.Vin);

        var dup = await h.Vehicles.CreateAsync(CreateRequest("1-NEW-002", vin: "XLRTESTVIN01"), CancellationToken.None);
        Assert.Equal(VehicleOperationOutcome.DuplicateVin, dup.Outcome);
        Assert.Contains(first.Vehicle.InternalNumber, dup.Message);

        var noVinA = await h.Vehicles.CreateAsync(CreateRequest("1-NEW-003"), CancellationToken.None);
        var noVinB = await h.Vehicles.CreateAsync(CreateRequest("1-NEW-004", vin: "  "), CancellationToken.None);
        Assert.Equal(VehicleOperationOutcome.Success, noVinA.Outcome);
        Assert.Equal(VehicleOperationOutcome.Success, noVinB.Outcome);
    }

    [Fact]
    public async Task Update_ToAnotherVehiclesPlate_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var spare = (await h.Vehicles.GetByIdAsync(h.SpareVehicleId, CancellationToken.None))!;

        var result = await h.Vehicles.UpdateAsync(h.SpareVehicleId,
            UpdateRequest(spare with { LicensePlate = "1tft001" }, spare.OdometerKm), CancellationToken.None);

        Assert.Equal(VehicleOperationOutcome.DuplicateLicensePlate, result.Outcome);
    }

    [Fact]
    public async Task Search_FindsNormalisedPlate_AndVin()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Vehicles.CreateAsync(CreateRequest("1-ABC-777", vin: "WSMVIN123"), CancellationToken.None);

        var byPlate = await h.Vehicles.SearchAsync("1tft001", null, null, null, PageRequest.Of(1, 25), CancellationToken.None);
        var byVin = await h.Vehicles.SearchAsync("wsmvin", null, null, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal("VRT-0001", Assert.Single(byPlate.Items).InternalNumber);
        Assert.Equal("1-ABC-777", Assert.Single(byVin.Items).LicensePlate);
    }

    [Fact]
    public async Task Trailer_SamePlateWithOtherSeparators_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Trailers.CreateAsync(new CreateTrailerRequest(
            "qopl001", null, null, null, null, null, null, 27_000m, null, null, null, null, false, false,
            VehicleOwnershipType.Owned, null), CancellationToken.None);

        Assert.Equal(TrailerOperationOutcome.DuplicateLicensePlate, result.Outcome);
        Assert.Contains("OPL-0001", result.Message);
    }

    // ---------------- FL-M5 ----------------

    [Fact]
    public async Task Create_NegativePayloadOrOdometer_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var payload = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Vehicles.CreateAsync(CreateRequest("1-NEG-001", payload: -5m), CancellationToken.None));
        var odometer = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Vehicles.CreateAsync(CreateRequest("1-NEG-002", odometer: -10), CancellationToken.None));

        Assert.Contains("laadvermogen mag niet negatief", payload.Message);
        Assert.Contains("kilometerstand mag niet negatief", odometer.Message);
        Assert.False(await h.Context.Vehicles.AnyAsync(v => v.LicensePlate.StartsWith("1-NEG")));
    }

    // ---------------- FL-M6 ----------------

    [Fact]
    public async Task Update_LowerOdometer_WithoutReason_IsRefused_WithReason_IsAuditedCorrection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vehicle = (await h.Vehicles.GetByIdAsync(h.VehicleId, CancellationToken.None))!;

        var refused = await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Vehicles.UpdateAsync(h.VehicleId, UpdateRequest(vehicle, 100_000), CancellationToken.None));
        Assert.Contains("kan niet lager zijn dan de huidige stand", refused.Message);

        var corrected = await h.Vehicles.UpdateAsync(h.VehicleId,
            UpdateRequest(vehicle, 100_000, "Teller vervangen na defect"), CancellationToken.None);
        Assert.Equal(VehicleOperationOutcome.Success, corrected.Outcome);
        Assert.Equal(100_000, corrected.Vehicle!.OdometerKm);
        var audit = await h.Context.AuditLogs.Where(a => a.EntityType == "Vehicle" && a.Action == "Updated").ToListAsync();
        Assert.Contains(audit, a => a.NewValuesJson!.Contains("Teller vervangen na defect"));

        var higher = await h.Vehicles.UpdateAsync(h.VehicleId, UpdateRequest(corrected.Vehicle, 101_000), CancellationToken.None);
        Assert.Equal(VehicleOperationOutcome.Success, higher.Outcome);
    }

    [Fact]
    public async Task MaintenanceCompletion_AdvancesOdometer_ButNeverLowersIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var maintenance = new MaintenanceService(h.Context, tenant, new AuditService(h.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        var request = new CreateMaintenanceRequest(MaintenanceType.Repair, null, "Turbo", TripDate, null, "Garage", null, null, null);
        var newer = await maintenance.CreateForVehicleAsync(h.VehicleId, request, CancellationToken.None);
        var older = await maintenance.CreateForVehicleAsync(h.VehicleId, request, CancellationToken.None);

        await maintenance.CompleteAsync(newer.Record!.Id, new CompleteMaintenanceRequest(TripDate, 126_000, "Turbo", null, 1000m, null), CancellationToken.None);
        Assert.Equal(126_000, (await h.Context.Vehicles.AsNoTracking().SingleAsync(v => v.Id == h.VehicleId)).OdometerKm);

        await maintenance.CompleteAsync(older.Record!.Id, new CompleteMaintenanceRequest(TripDate, 125_800, "Nazicht", null, 100m, null), CancellationToken.None);
        Assert.Equal(126_000, (await h.Context.Vehicles.AsNoTracking().SingleAsync(v => v.Id == h.VehicleId)).OdometerKm);
    }

    // ---------------- FL-M7 ----------------

    [Fact]
    public async Task Damage_LinksToTheVehiclesOwnTrip_CarriesCosts_AndRefusesForeignTrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tripId = await PlannedTripAsync(h);
        var foreignTrip = new Trip { Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0077", VehicleId = h.SpareVehicleId, TripDate = TripDate };
        h.Context.Trips.Add(foreignTrip);
        await h.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(h.TenantId);
        var damage = new DamageReportService(h.Context, tenant, new AuditService(h.Context, tenant, new DevCurrentUserContext(null)));

        var created = await damage.CreateForVehicleAsync(h.VehicleId, new CreateDamageReportRequest(
            null, TripDate, "Laadkade", "Spiegel afgebroken", DamageSeverity.Moderate, "POL-1", null,
            TripId: tripId, RepairCost: 450.50m, DowntimeDays: 1), CancellationToken.None);
        var foreign = await damage.CreateForVehicleAsync(h.VehicleId, new CreateDamageReportRequest(
            null, TripDate, null, "Kras", DamageSeverity.Minor, null, null, TripId: foreignTrip.Id), CancellationToken.None);
        var parking = await damage.CreateForVehicleAsync(h.VehicleId, new CreateDamageReportRequest(
            null, TripDate, "Parking", "Deuk", DamageSeverity.Minor, null, null), CancellationToken.None);

        Assert.Equal(DamageOperationOutcome.Success, created.Outcome);
        Assert.Equal(tripId, created.Report!.TripId);
        Assert.Equal("RIT-0001", created.Report.TripNumber);
        Assert.Equal((450.50m, 1), (created.Report.RepairCost!.Value, created.Report.DowntimeDays!.Value));
        Assert.Equal(DamageOperationOutcome.ValidationFailed, foreign.Outcome);
        Assert.Equal("De gekozen rit hoort niet bij dit voertuig.", foreign.Error);
        Assert.Equal(DamageOperationOutcome.Success, parking.Outcome);
        Assert.Null(parking.Report!.TripId);

        var trips = await h.Vehicles.ListRecentTripsAsync(h.VehicleId, 30, CancellationToken.None);
        Assert.Equal("RIT-0001", Assert.Single(trips!).TripNumber);
    }

    // ---------------- FL-M11 ----------------

    private sealed class FakePermissions(params string[] granted) : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
            => Task.FromResult(granted.Contains(permissionCode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Kpi_CostValues_OnlyForFleetFinanceViewers(bool financeView)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Context.FuelTransactions.AddRange(
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new(2026, 9, 1), Litres = 100, TotalAmount = 180, OdometerKm = 120_000 },
            new FuelTransaction { Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new(2026, 9, 20), Litres = 120, TotalAmount = 216, OdometerKm = 120_800 });
        h.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, VehicleId = h.VehicleId, Description = "Service",
            Status = MaintenanceStatus.Completed, CompletedDate = new(2026, 9, 10), Cost = 900m,
        });
        await h.Context.SaveChangesAsync();
        var permissions = financeView
            ? new FakePermissions(PermissionCodes.VehiclesView, PermissionCodes.FleetFinanceView)
            : new FakePermissions(PermissionCodes.VehiclesView);
        var controller = new FleetKpiController(new FleetKpiService(h.Context, new DevTenantContext(h.TenantId)),
            new DevCurrentUserContext(Guid.NewGuid()), permissions);

        var response = await controller.Vehicle(h.VehicleId, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CancellationToken.None);

        var kpi = Assert.IsType<FleetKpiDto>(Assert.IsType<OkObjectResult>(response.Result).Value);
        var keys = kpi.Values.Select(v => v.Key).ToList();
        Assert.Contains("trips", keys);
        Assert.Contains("fuel_litres", keys);
        if (financeView)
        {
            Assert.Contains("maintenance_cost", keys);
            Assert.Contains("fuel_cost", keys);
        }
        else
        {
            Assert.DoesNotContain(kpi.Values, v => v.Unit.StartsWith("EUR"));
            Assert.DoesNotContain(keys, k => k.Contains("cost"));
        }
    }

    // ---------------- FL-M13 ----------------

    [Fact]
    public async Task ReplaceVehicle_OnPlannedTrip_KeepsItPlanned_AndAuditsTheReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tripId = await PlannedTripAsync(h);

        var result = await h.Trips.ReplaceVehicleAsync(tripId,
            new ReplaceTripVehicleRequest(h.SpareVehicleId, null, "VRT-0001 in onderhoud"), false, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.Success, result.Outcome);
        Assert.Equal(TripStatus.Planned, result.Trip!.Status);
        Assert.Equal(h.SpareVehicleId, result.Trip.VehicleId);
        var audit = await h.Context.AuditLogs.SingleAsync(a => a.EntityType == "Trip" && a.Action == "VehicleReplaced");
        Assert.Contains("VRT-0001 in onderhoud", audit.NewValuesJson);
    }

    [Fact]
    public async Task ReplaceVehicle_Guards_ReasonDraftAndBlockingConflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var tripId = await PlannedTripAsync(h);
        var spare = await h.Context.Vehicles.SingleAsync(v => v.Id == h.SpareVehicleId);
        spare.OperationalStatus = VehicleOperationalStatus.OutOfService;
        await h.Context.SaveChangesAsync();

        var noReason = await h.Trips.ReplaceVehicleAsync(tripId, new ReplaceTripVehicleRequest(h.SpareVehicleId, null, " "), false, CancellationToken.None);
        var blocked = await h.Trips.ReplaceVehicleAsync(tripId, new ReplaceTripVehicleRequest(h.SpareVehicleId, null, "vervangen"), false, CancellationToken.None);
        var forced = await h.Trips.ReplaceVehicleAsync(tripId, new ReplaceTripVehicleRequest(h.SpareVehicleId, null, "noodgeval"), true, CancellationToken.None);

        Assert.Equal(TripOperationOutcome.ValidationFailed, noReason.Outcome);
        Assert.Equal(TripOperationOutcome.ConflictsBlock, blocked.Outcome);
        Assert.Contains(blocked.Conflicts!, c => c.Code == PlanningConflictCode.VehicleNotOperational);
        Assert.Equal(TripOperationOutcome.Success, forced.Outcome);

        var draft = await h.Trips.CreateAsync(
            new CreateTripRequest(TripDate.AddDays(1), null, h.VehicleId, null, null, null, null, []), CancellationToken.None);
        var onDraft = await h.Trips.ReplaceVehicleAsync(draft.Trip!.Id, new ReplaceTripVehicleRequest(h.SpareVehicleId, null, "x"), false, CancellationToken.None);
        Assert.Equal(TripOperationOutcome.InvalidState, onDraft.Outcome);
    }

    // ---------------- Roles ----------------

    [Fact]
    public void Templates_Wagenparkbeheerder_ManagesFleet_Boekhouding_ReadsFleetCosts()
    {
        var fleet = DefaultRoleDefinitions.All.Single(t => t.Code == "wagenparkbeheerder");
        Assert.Equal("Wagenparkbeheerder", fleet.Name);
        Assert.Contains(PermissionCodes.VehiclesEdit, fleet.PermissionCodes);
        Assert.Contains(PermissionCodes.MaintenanceEdit, fleet.PermissionCodes);
        Assert.Contains(PermissionCodes.DamageReportsEdit, fleet.PermissionCodes);
        Assert.Contains(PermissionCodes.FleetFinanceView, fleet.PermissionCodes);
        Assert.DoesNotContain(fleet.PermissionCodes, p => p.StartsWith("users.") || p.StartsWith("roles.") || p.StartsWith("invoices."));
        Assert.DoesNotContain(PermissionCodes.EmployeesViewConfidential, fleet.PermissionCodes);

        var boekhouding = DefaultRoleDefinitions.All.Single(t => t.Code == "boekhouding");
        Assert.Contains(PermissionCodes.FleetFinanceView, boekhouding.PermissionCodes);
        Assert.Contains(PermissionCodes.MaintenanceView, boekhouding.PermissionCodes);
        Assert.DoesNotContain(PermissionCodes.VehiclesEdit, boekhouding.PermissionCodes);
        Assert.DoesNotContain(PermissionCodes.MaintenanceEdit, boekhouding.PermissionCodes);
        Assert.DoesNotContain(PermissionCodes.PlanningEdit, boekhouding.PermissionCodes);

        var step = DefaultRoleUpgrades.Steps.Single(s => s.Version == 37);
        Assert.Contains(PermissionCodes.FleetFinanceView, step.GrantsByTemplateCode["boekhouding"]);
        Assert.Equal(38, DefaultRoleUpgrades.CurrentVersion);
    }
}
