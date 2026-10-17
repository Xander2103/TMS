using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Fleet;

/// <summary>
/// FL-H1/H2/H4/M3/M4/M14 — one fleet readiness verdict (manual status + maintenance windows +
/// legal documents/inspections + damage), and Planning reading exactly that verdict.
/// </summary>
public class FleetReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly DateOnly TripDay = new(2026, 10, 22);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid VehicleId, Guid TrailerId,
        FleetReadinessService Readiness, PlanningConflictService Conflicts, MaintenanceService Maintenance,
        FleetDocumentService Documents, DamageReportService Damage);

    private static async Task<Harness> SeedAsync(decimal payloadKg = 11500m)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-TFT-001", NormalizedLicensePlate = "1TFT001",
            OperationalStatus = VehicleOperationalStatus.Available, IsActive = true, PayloadKg = payloadKg, OdometerKm = 125400,
        });
        db.Context.Trailers.Add(new Trailer
        {
            Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-1", LicensePlate = "Q-TFT-001", NormalizedLicensePlate = "QTFT001",
            OperationalStatus = TrailerOperationalStatus.Available, IsActive = true, CapacityKg = 27000m,
        });
        db.Context.Customers.Add(new TransportationService.Api.Modules.Partners.Entities.Customer
        {
            Id = CustomerId(tenantId), TenantId = tenantId, CustomerNumber = "KL-1", Name = "TEST klant", IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var storage = new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N")));
        return new Harness(db, tenantId, vehicleId, trailerId,
            new FleetReadinessService(db.Context, tenant, clock),
            new PlanningConflictService(db.Context, tenant, new QualificationStatusCalculator(), clock),
            new MaintenanceService(db.Context, tenant, audit, clock),
            new FleetDocumentService(db.Context, tenant, audit, clock, storage),
            new DamageReportService(db.Context, tenant, audit));
    }

    private static Guid CustomerId(Guid tenantId) => new(tenantId.ToByteArray().Reverse().ToArray());

    private static Trip TripFor(Harness h, DateOnly? day = null, Guid? trailerId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-" + Guid.NewGuid().ToString("N")[..6],
        TripDate = day ?? TripDay, VehicleId = h.VehicleId, TrailerId = trailerId, Status = TripStatus.Draft,
    };

    private static async Task<Trip> TripWithOrderAsync(Harness h, decimal weightKg, bool adr = false, bool crane = false, Guid? trailerId = null)
    {
        var orderId = Guid.NewGuid();
        h.Db.Context.TransportOrders.Add(new TransportOrder
        {
            Id = orderId, TenantId = h.TenantId, CustomerId = CustomerId(h.TenantId), OrderNumber = "ORD-" + orderId.ToString("N")[..6], OrderDate = TripDay,
            Status = TransportOrderStatus.Confirmed, WeightKg = weightKg, VolumeM3 = 10m, AdrRequired = adr, CraneRequired = crane,
        });
        var trip = TripFor(h, trailerId: trailerId);
        trip.Orders.Add(new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = trip.Id, TransportOrderId = orderId, Sequence = 1 });
        h.Db.Context.Trips.Add(trip);
        await h.Db.Context.SaveChangesAsync();
        return trip;
    }

    private static CreateMaintenanceRequest Maintenance(DateOnly start, DateOnly? end = null) => new(
        MaintenanceType.PeriodicService, null, "TEST service", start, null, "Garage", null, null, null, end);

    [Fact]
    public async Task HealthyVehicle_IsDeployable_AndPlanningHasNoFleetConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var readiness = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);
        var conflicts = await h.Conflicts.EvaluateAsync(TripFor(h), CancellationToken.None);

        Assert.Equal(ReadinessLevel.Deployable, readiness!.Level);
        Assert.Equal("Inzetbaar", readiness.Summary);
        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.VehicleNotOperational);
    }

    [Fact]
    public async Task PlannedMaintenanceWindow_BlocksOnlyItsDays_AndCompletionRestoresReadiness()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Maintenance.CreateForVehicleAsync(h.VehicleId, Maintenance(TripDay, TripDay.AddDays(1)), CancellationToken.None);
        Assert.Equal(TripDay.AddDays(1), created.Record!.ScheduledEndDate);

        var onDay = await h.Conflicts.EvaluateAsync(TripFor(h, TripDay.AddDays(1)), CancellationToken.None);
        var conflict = Assert.Single(onDay, c => c.Code == PlanningConflictCode.VehicleNotOperational);
        Assert.True(conflict.Blocking);
        Assert.Contains("Onderhoud gepland van 22/10/2026 t/m 23/10/2026", conflict.Description);

        var outside = await h.Conflicts.EvaluateAsync(TripFor(h, TripDay.AddDays(2)), CancellationToken.None);
        Assert.DoesNotContain(outside, c => c.Code == PlanningConflictCode.VehicleNotOperational);

        // Started → in the garage; completed → the blocker is gone without any manual status flip.
        await h.Maintenance.UpdateAsync(created.Record.Id, new UpdateMaintenanceRequest(
            MaintenanceType.PeriodicService, null, MaintenanceStatus.InProgress, "TEST service", TripDay, null, "Garage", null, null, null,
            TripDay.AddDays(1)), CancellationToken.None);
        var active = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);
        Assert.Contains(active!.Issues, i => i.Code == "maintenance.active");
        await h.Maintenance.CompleteAsync(created.Record.Id, new CompleteMaintenanceRequest(TripDay, 126000, "Olie", "Garage", 410m, null), CancellationToken.None);
        var restored = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);
        Assert.Equal(ReadinessLevel.Deployable, restored!.Level);
        Assert.Equal(126000, (await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId)).OdometerKm);
    }

    [Fact]
    public async Task MaintenanceStartedEarlierThanPlanned_BlocksFromToday_UntilCompleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Maintenance.CreateForVehicleAsync(h.VehicleId, Maintenance(TripDay, TripDay.AddDays(1)), CancellationToken.None);
        await h.Maintenance.UpdateAsync(created.Record!.Id, new UpdateMaintenanceRequest(
            MaintenanceType.PeriodicService, null, MaintenanceStatus.InProgress, "TEST service", TripDay, null, "Garage", null, null, null,
            TripDay.AddDays(1)), CancellationToken.None);

        var today = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, Today, CancellationToken.None);

        Assert.Equal(ReadinessLevel.Blocked, today!.Level);
        Assert.Contains(today.Issues, i => i.Code == "maintenance.active");
    }

    [Fact]
    public async Task MaintenanceWindow_EndBeforeStart_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Maintenance.CreateForVehicleAsync(h.VehicleId, Maintenance(TripDay, TripDay.AddDays(-1)), CancellationToken.None);

        Assert.Equal("De einddatum moet op of na de startdatum liggen.", result.Error);
    }

    [Fact]
    public async Task ManualOutOfService_StaysBlocking_AfterAnUnrelatedMaintenanceIsCompleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vehicle = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId);
        vehicle.OperationalStatus = VehicleOperationalStatus.OutOfService;
        vehicle.StatusReason = "Turbo defect";
        await h.Db.Context.SaveChangesAsync();
        var m = await h.Maintenance.CreateForVehicleAsync(h.VehicleId, Maintenance(Today), CancellationToken.None);
        await h.Maintenance.CompleteAsync(m.Record!.Id, new CompleteMaintenanceRequest(Today, null, null, null, null, null), CancellationToken.None);

        var readiness = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);

        Assert.Equal(ReadinessLevel.Blocked, readiness!.Level);
        Assert.Contains(readiness.Issues, i => i.Code == "status.out_of_service" && i.Message.Contains("Turbo defect"));
    }

    [Fact]
    public async Task ExpiredTechnicalInspection_BlocksPlanning_AndARenewalRestoresIt_WithoutStaleOverdueInspection()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Documents.CreateForVehicleAsync(h.VehicleId, new CreateFleetDocumentRequest(
            FleetDocumentType.TechnicalInspection, null, "KEUR-2025", new DateOnly(2025, 9, 30), new DateOnly(2026, 9, 30), 30, null), CancellationToken.None);
        // The certificate itself puts the next keuring on the schedule (FL-M3): one Inspection, due 30/09/2026.
        var scheduled = await h.Db.Context.Inspections.SingleAsync(i => i.VehicleId == h.VehicleId);
        Assert.Equal(new DateOnly(2026, 9, 30), scheduled.DueDate);
        Assert.Null(scheduled.CompletedDate);

        var blocked = await h.Conflicts.EvaluateAsync(TripFor(h), CancellationToken.None);
        var conflict = Assert.Single(blocked, c => c.Code == PlanningConflictCode.VehicleNotOperational);
        Assert.True(conflict.Blocking);
        Assert.Contains("Technische keuring verlopen op 30/09/2026", conflict.Description);

        // Renewal: the new certificate closes the overdue inspection and plans the next one.
        var renewal = await h.Documents.CreateForVehicleAsync(h.VehicleId, new CreateFleetDocumentRequest(
            FleetDocumentType.TechnicalInspection, null, "KEUR-2026", new DateOnly(2026, 10, 1), new DateOnly(2027, 10, 1), 30, null), CancellationToken.None);
        Assert.Null(renewal.Error);

        var inspections = await h.Db.Context.Inspections.Where(i => i.VehicleId == h.VehicleId).OrderBy(i => i.DueDate).ToListAsync();
        Assert.Equal(2, inspections.Count);
        Assert.NotNull(inspections[0].CompletedDate);
        Assert.Equal(InspectionResult.Passed, inspections[0].Result);
        Assert.Null(inspections[1].CompletedDate);
        Assert.Equal(new DateOnly(2027, 10, 1), inspections[1].DueDate);
        var readiness = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);
        Assert.Equal(ReadinessLevel.Deployable, readiness!.Level);
    }

    [Fact]
    public async Task CorrectingTheCertificateExpiry_MovesTheScheduledInspection_AndReadinessFollows()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var doc = await h.Documents.CreateForVehicleAsync(h.VehicleId, new CreateFleetDocumentRequest(
            FleetDocumentType.TechnicalInspection, null, "KEUR-1", new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1), 30, null), CancellationToken.None);
        Assert.Equal(ReadinessLevel.Deployable, (await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None))!.Level);

        await h.Documents.UpdateAsync(doc.Document!.Id, new UpdateFleetDocumentRequest(
            FleetDocumentType.TechnicalInspection, null, "KEUR-1", new DateOnly(2026, 6, 1), new DateOnly(2026, 10, 20), 30, null), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 20), (await h.Db.Context.Inspections.SingleAsync(i => i.VehicleId == h.VehicleId)).DueDate);
        var readiness = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);
        Assert.Equal(ReadinessLevel.Blocked, readiness!.Level);
        Assert.Contains(readiness.Issues, i => i.Code == "inspection.expired");
    }

    [Fact]
    public async Task ExpiredInsurance_Blocks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Documents.CreateForVehicleAsync(h.VehicleId, new CreateFleetDocumentRequest(
            FleetDocumentType.Insurance, null, "POLIS", null, new DateOnly(2026, 10, 1), null, null), CancellationToken.None);

        var readiness = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);

        Assert.Contains(readiness!.Issues, i => i.Code == "insurance.expired" && i.Severity == ReadinessSeverity.Blocking);
    }

    [Fact]
    public async Task ExpiredAdrCertificate_OnlyBlocksAdrWork()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vehicle = await h.Db.Context.Vehicles.SingleAsync(v => v.Id == h.VehicleId);
        vehicle.AdrSuitable = true;
        await h.Db.Context.SaveChangesAsync();
        await h.Documents.CreateForVehicleAsync(h.VehicleId, new CreateFleetDocumentRequest(
            FleetDocumentType.AdrCertificate, null, "ADR-1", null, new DateOnly(2026, 10, 1), null, null), CancellationToken.None);

        var normal = await h.Conflicts.EvaluateAsync(await TripWithOrderAsync(h, 1000m), CancellationToken.None);
        Assert.DoesNotContain(normal, c => c.Blocking && c.Code is PlanningConflictCode.VehicleNotOperational or PlanningConflictCode.OrderRequiresAdr);
        var readiness = await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None);
        Assert.Equal(ReadinessLevel.Warning, readiness!.Level);

        var adr = await h.Conflicts.EvaluateAsync(await TripWithOrderAsync(h, 1000m, adr: true), CancellationToken.None);
        var conflict = Assert.Single(adr, c => c.Code == PlanningConflictCode.OrderRequiresAdr);
        Assert.Contains("ADR-certificaat verlopen", conflict.Description);
    }

    [Theory]
    [InlineData(DamageSeverity.Minor, ReadinessLevel.Deployable)]
    [InlineData(DamageSeverity.Moderate, ReadinessLevel.Warning)]
    [InlineData(DamageSeverity.Severe, ReadinessLevel.Blocked)]
    [InlineData(DamageSeverity.TotalLoss, ReadinessLevel.Blocked)]
    public async Task OpenDamage_AffectsReadinessBySeverity_AndClosingRestoresIt(DamageSeverity severity, ReadinessLevel expected)
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var report = await h.Damage.CreateForVehicleAsync(h.VehicleId,
            new CreateDamageReportRequest(null, Today, null, "TEST aanrijding", severity, null, null), CancellationToken.None);

        Assert.Equal(expected, (await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None))!.Level);

        var stored = await h.Db.Context.DamageReports.SingleAsync(d => d.Id == report.Report!.Id);
        stored.Status = DamageStatus.Repaired;
        await h.Db.Context.SaveChangesAsync();
        Assert.Equal(ReadinessLevel.Deployable, (await h.Readiness.EvaluateVehicleAsync(h.VehicleId, TripDay, CancellationToken.None))!.Level);
    }

    [Fact]
    public async Task Overload_IsBlocking_TrailerCapacityCounts_AndRoundingStaysAWarning()
    {
        var h = await SeedAsync(payloadKg: 11500m);
        using var _ = h.Db;

        var over = await h.Conflicts.EvaluateAsync(await TripWithOrderAsync(h, 12000m), CancellationToken.None);
        var overload = Assert.Single(over, c => c.Code == PlanningConflictCode.CapacityExceeded);
        Assert.True(overload.Blocking);
        Assert.True(overload.OverrideAllowed);

        var rounding = await h.Conflicts.EvaluateAsync(await TripWithOrderAsync(h, 11520m), CancellationToken.None);
        Assert.False(Assert.Single(rounding, c => c.Code == PlanningConflictCode.CapacityExceeded).Blocking);

        var combination = await h.Conflicts.EvaluateAsync(await TripWithOrderAsync(h, 12000m, trailerId: h.TrailerId), CancellationToken.None);
        Assert.DoesNotContain(combination, c => c.Code == PlanningConflictCode.CapacityExceeded);
    }

    [Fact]
    public async Task TrailerReadiness_IsIndependent_AndReachesPlanning()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Maintenance.CreateForTrailerAsync(h.TrailerId, Maintenance(TripDay), CancellationToken.None);

        var conflicts = await h.Conflicts.EvaluateAsync(TripFor(h, trailerId: h.TrailerId), CancellationToken.None);

        Assert.Contains(conflicts, c => c.Code == PlanningConflictCode.TrailerNotOperational && c.Blocking);
        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.VehicleNotOperational);
    }

    [Theory]
    [InlineData(FleetDocumentType.Insurance, null, null, "Een vervaldatum is verplicht voor dit documenttype.")]
    [InlineData(FleetDocumentType.Registration, null, "2027-01-01", "Een documentnummer is verplicht voor dit documenttype.")]
    [InlineData(FleetDocumentType.Other, null, null, "Een naam voor het aangepaste documenttype is verplicht.")]
    public async Task EmptyOrMeaninglessDocuments_AreRejected(FleetDocumentType type, string? number, string? expiry, string error)
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Documents.CreateForVehicleAsync(h.VehicleId, new CreateFleetDocumentRequest(
            type, null, number, null, expiry is null ? null : DateOnly.Parse(expiry), null, null), CancellationToken.None);

        Assert.Equal(error, result.Error);
    }
}
