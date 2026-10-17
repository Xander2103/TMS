using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

/// <summary>
/// FL-H3: the vehicle timeline tells the business story in Dutch — WHEN / WHO (or Systeem) / WHAT —
/// across the vehicle, its documents, inspections, maintenance, damage, fuel and the trips that
/// carried it, and only shows amounts to fleet_finance.view holders.
/// </summary>
public class FleetHistoryServiceTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, FleetHistoryService Sut, Guid TenantId, Guid UserId, Guid VehicleId, Guid DriverId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = T0 });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "fleet@acme.be", FirstName = "Dev", LastName = "Admin", IsActive = true });
        db.Context.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-0007", FirstName = "TEST HR", LastName = "Inzetbaar" });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, EmployeeId = employeeId, DriverNumber = "CH-0006", IsActive = true });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0012", LicensePlate = "1-TFG-001", NormalizedLicensePlate = "1TFG001", OdometerKm = 42_000, IsActive = true });
        await db.Context.SaveChangesAsync();
        var sut = new FleetHistoryService(db.Context, new DevTenantContext(tenantId), new TestClock(Now));
        return new Harness(db, sut, tenantId, userId, vehicleId, driverId);
    }

    private static AuditLog Audit(Harness h, string type, Guid entityId, string action, string? oldJson, string? newJson, int hours, Guid? userId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = userId ?? h.UserId, EntityType = type, EntityId = entityId.ToString(),
        Action = action, OldValuesJson = oldJson, NewValuesJson = newJson, Timestamp = T0.AddHours(hours),
    };

    [Fact]
    public async Task Timeline_TellsTheVehicleStory_InDutch_NewestFirst_WithSystemReadinessMoments()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var docId = Guid.NewGuid();
        var maintenanceId = Guid.NewGuid();
        var damageId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        h.Db.Context.FleetDocuments.Add(new FleetDocument
        {
            Id = docId, TenantId = h.TenantId, VehicleId = h.VehicleId, DocumentType = FleetDocumentType.TechnicalInspection,
            DocumentNumber = "TEST-GOLD-KEUR", ExpiryDate = new DateOnly(2027, 9, 1),
        });
        h.Db.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = maintenanceId, TenantId = h.TenantId, VehicleId = h.VehicleId, MaintenanceType = MaintenanceType.Repair,
            Description = "Koppeling vervangen", Status = MaintenanceStatus.Completed,
            ScheduledDate = new DateOnly(2026, 10, 20), ScheduledEndDate = new DateOnly(2026, 10, 21),
            CompletedDate = new DateOnly(2026, 10, 21), CompletedOdometerKm = 42_350, Cost = 2150m, Provider = "Garage Peeters",
        });
        h.Db.Context.DamageReports.Add(new DamageReport
        {
            Id = damageId, TenantId = h.TenantId, VehicleId = h.VehicleId, DriverId = h.DriverId, TripId = tripId,
            IncidentDate = new DateOnly(2026, 10, 23), Description = "Aanrijding bij lossen", Severity = DamageSeverity.Severe,
            Status = DamageStatus.Closed, RepairCost = 3275.40m,
        });
        h.Db.Context.Trips.Add(new Trip { Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0043", TripDate = new DateOnly(2026, 10, 22), VehicleId = h.VehicleId, DriverId = h.DriverId, Status = TripStatus.Completed });
        var v = h.VehicleId;
        h.Db.Context.AuditLogs.AddRange(
            Audit(h, "Vehicle", v, "Created", null, "{\"InternalNumber\":\"VRT-0012\",\"LicensePlate\":\"1-TFG-001\"}", 0),
            Audit(h, "FleetDocument", docId, "Created", null, $"{{\"DocumentType\":\"TechnicalInspection\",\"VehicleId\":\"{v}\",\"ExpiryDate\":\"2027-09-01\"}}", 1),
            Audit(h, "Vehicle", v, "AssignmentChanged", "{\"Kind\":\"Fixed\",\"DriverId\":null}", $"{{\"Kind\":\"Fixed\",\"DriverId\":\"{h.DriverId}\"}}", 2),
            Audit(h, "Trip", tripId, "Created", null, $"{{\"TripNumber\":\"RIT-0043\",\"TripDate\":\"2026-10-22\",\"DriverId\":\"{h.DriverId}\",\"VehicleId\":\"{v}\"}}", 3),
            Audit(h, "Trip", tripId, "StatusChanged", "{\"Status\":\"Draft\"}", "{\"Status\":\"Planned\",\"Overridden\":false,\"OverrideReason\":null}", 4),
            Audit(h, "MaintenanceRecord", maintenanceId, "Created", null, $"{{\"MaintenanceType\":\"Repair\",\"VehicleId\":\"{v}\",\"TrailerId\":null,\"ScheduledDate\":\"2026-10-20\"}}", 5),
            Audit(h, "Vehicle", v, "Updated",
                "{\"LicensePlate\":\"1-TFG-001\",\"OperationalStatus\":\"Available\",\"StatusReason\":null,\"IsActive\":true}",
                "{\"LicensePlate\":\"1-TFG-001\",\"OperationalStatus\":\"OutOfService\",\"StatusReason\":\"Koppeling defect\",\"IsActive\":true}", 6),
            Audit(h, "Vehicle", v, "Updated",
                "{\"LicensePlate\":\"1-TFG-001\",\"OperationalStatus\":\"OutOfService\",\"StatusReason\":\"Koppeling defect\",\"IsActive\":true}",
                "{\"LicensePlate\":\"1-TFG-001\",\"OperationalStatus\":\"Available\",\"StatusReason\":null,\"IsActive\":true}", 7),
            Audit(h, "MaintenanceRecord", maintenanceId, "Completed", "{\"Status\":\"Planned\",\"CompletedDate\":null}",
                "{\"Status\":\"Completed\",\"CompletedDate\":\"2026-10-21\",\"Cost\":2150,\"FollowUpId\":null}", 24 * 4 + 6),
            Audit(h, "DamageReport", damageId, "Created", null, $"{{\"IncidentDate\":\"2026-10-23\",\"Severity\":\"Severe\",\"VehicleId\":\"{v}\",\"TrailerId\":null}}", 24 * 6),
            Audit(h, "DamageReport", damageId, "Updated", "{\"Status\":\"Reported\",\"Severity\":\"Severe\",\"RepairCost\":null}",
                "{\"Status\":\"Closed\",\"Severity\":\"Severe\",\"RepairCost\":3275.40}", 24 * 7));
        await h.Db.Context.SaveChangesAsync();

        var history = (await h.Sut.GetVehicleHistoryAsync(v, includeFinancial: false, CancellationToken.None))!;
        var titles = history.Select(e => e.Title).ToList();

        // Newest first.
        Assert.Equal("Voertuig aangemaakt — VRT-0012 · 1-TFG-001", titles[^1]);
        Assert.Equal("Schade afgesloten", titles[1]);
        Assert.Contains("Technische keuring toegevoegd", titles);
        Assert.Contains(history, e => e.Title == "Technische keuring toegevoegd" && e.Detail == "Nr. TEST-GOLD-KEUR · geldig tot 01/09/2027");
        Assert.Contains("Vaste chauffeur toegewezen: TEST HR Inzetbaar (CH-0006)", titles);
        Assert.Contains(history, e => e.Title == "Ingepland op RIT-0043" && e.Detail!.Contains("chauffeur TEST HR Inzetbaar (CH-0006)"));
        Assert.Contains("Onderhoud gepland: Herstelling — 20/10/2026 t/m 21/10/2026", titles);
        Assert.Contains(history, e => e.Title == "Operationele status: Beschikbaar → Buiten dienst" && e.Detail == "Reden: Koppeling defect");
        Assert.Contains("Onderhoud afgerond: Herstelling", titles);
        Assert.Contains(history, e => e.Title == "Schade gemeld (Zwaar): Aanrijding bij lossen" && e.Detail!.Contains("rit RIT-0043"));

        // Systeem: the maintenance window and the heavy damage block, and both resolutions free the vehicle.
        var system = history.Where(e => e.ActorKind == "System").Select(e => e.Title).ToList();
        Assert.Contains("Voertuig niet inzetbaar wegens onderhoud (20/10/2026 t/m 21/10/2026)", system);
        Assert.Contains("Voertuig niet inzetbaar wegens zware schade", system);
        Assert.Equal(2, system.Count(t => t == "Voertuig opnieuw inzetbaar"));
        Assert.All(history.Where(e => e.ActorKind == "User"), e => Assert.Equal("Dev Admin", e.ActorName));

        // No raw codes, no amounts without fleet_finance.view.
        Assert.DoesNotContain(history, e => e.Title.Contains("OutOfService") || e.Title.Contains("Available") || e.Title.Contains("OperationalStatus"));
        Assert.DoesNotContain(history, e => (e.Detail ?? "").Contains('€') || e.Financial);
        Assert.DoesNotContain(history, e => e.Title == "Schademelding bijgewerkt");
    }

    [Fact]
    public async Task FinanceViewers_SeeTheAmounts_OnTheSameLines()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var maintenanceId = Guid.NewGuid();
        var fuelId = Guid.NewGuid();
        h.Db.Context.MaintenanceRecords.Add(new MaintenanceRecord
        {
            Id = maintenanceId, TenantId = h.TenantId, VehicleId = h.VehicleId, MaintenanceType = MaintenanceType.PeriodicService,
            Description = "Grote beurt", Status = MaintenanceStatus.Completed, ScheduledDate = new DateOnly(2026, 10, 18),
            CompletedDate = new DateOnly(2026, 10, 18), CompletedOdometerKm = 125_900, Cost = 1480.50m,
        });
        h.Db.Context.FuelTransactions.Add(new FuelTransaction
        {
            Id = fuelId, TenantId = h.TenantId, VehicleId = h.VehicleId, TransactionDate = new DateOnly(2026, 10, 15),
            Litres = 380m, TotalAmount = 612.40m, OdometerKm = 125_600, Station = "Total Antwerpen",
        });
        h.Db.Context.AuditLogs.AddRange(
            Audit(h, "MaintenanceRecord", maintenanceId, "Completed", "{\"Status\":\"Planned\"}", "{\"Status\":\"Completed\",\"CompletedDate\":\"2026-10-18\",\"Cost\":1480.50}", 30),
            Audit(h, "FuelTransaction", fuelId, "Created", null, $"{{\"VehicleId\":\"{h.VehicleId}\",\"TransactionDate\":\"2026-10-15\",\"Litres\":380,\"TotalAmount\":612.40}}", 10));
        await h.Db.Context.SaveChangesAsync();

        var plain = (await h.Sut.GetVehicleHistoryAsync(h.VehicleId, includeFinancial: false, CancellationToken.None))!;
        var finance = (await h.Sut.GetVehicleHistoryAsync(h.VehicleId, includeFinancial: true, CancellationToken.None))!;

        var plainCompletion = Assert.Single(plain, e => e.Title == "Onderhoud afgerond: Periodiek onderhoud");
        Assert.Contains("kilometerstand 125.900 km", plainCompletion.Detail);
        Assert.DoesNotContain("€", plainCompletion.Detail);
        Assert.False(plainCompletion.Financial);
        var financeCompletion = Assert.Single(finance, e => e.Title == "Onderhoud afgerond: Periodiek onderhoud");
        Assert.Contains("kosten € 1.480,50", financeCompletion.Detail);
        Assert.True(financeCompletion.Financial);

        Assert.Contains(plain, e => e.Title == "Tankbeurt — 380 l · 125.600 km" && !(e.Detail ?? "").Contains('€'));
        Assert.Contains(finance, e => e.Title == "Tankbeurt — 380 l · 125.600 km" && e.Detail!.Contains("€ 612,40"));
    }

    [Fact]
    public async Task UnknownVehicle_IsNull_AndAReplacedTripIsToldOnBothVehicles()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Null(await h.Sut.GetVehicleHistoryAsync(Guid.NewGuid(), false, CancellationToken.None));

        var otherId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        h.Db.Context.Vehicles.Add(new Vehicle { Id = otherId, TenantId = h.TenantId, InternalNumber = "VRT-0004", LicensePlate = "1-ABC-103", NormalizedLicensePlate = "1ABC103", IsActive = true });
        h.Db.Context.Trips.Add(new Trip { Id = tripId, TenantId = h.TenantId, TripNumber = "RIT-0050", TripDate = new DateOnly(2026, 10, 29), VehicleId = otherId, Status = TripStatus.Planned });
        h.Db.Context.AuditLogs.AddRange(
            Audit(h, "Trip", tripId, "Created", null, $"{{\"TripNumber\":\"RIT-0050\",\"VehicleId\":\"{h.VehicleId}\"}}", 1),
            Audit(h, "Trip", tripId, "StatusChanged", "{\"Status\":\"Draft\"}", "{\"Status\":\"Planned\"}", 2),
            Audit(h, "Trip", tripId, "VehicleReplaced", $"{{\"VehicleId\":\"{h.VehicleId}\"}}", $"{{\"VehicleId\":\"{otherId}\",\"Reason\":\"In onderhoud\"}}", 3));
        await h.Db.Context.SaveChangesAsync();

        var golden = (await h.Sut.GetVehicleHistoryAsync(h.VehicleId, false, CancellationToken.None))!;
        Assert.Contains(golden, e => e.Title == "Ingepland op RIT-0050");
        Assert.Contains(golden, e => e.Title == "Vervangen op RIT-0050 door VRT-0004 (1-ABC-103)" && e.Detail!.Contains("reden: In onderhoud"));

        var replacement = (await h.Sut.GetVehicleHistoryAsync(otherId, false, CancellationToken.None))!;
        Assert.Contains(replacement, e => e.Title == "Ingezet op RIT-0050 ter vervanging van VRT-0012 (1-TFG-001)");
    }
}
