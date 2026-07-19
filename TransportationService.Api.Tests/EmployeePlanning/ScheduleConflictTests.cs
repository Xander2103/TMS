using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Integrations.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.EmployeePlanning;

public class ScheduleConflictTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Date = new(2026, 07, 21);

    private sealed record Harness(
        SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, Guid DriverId, Guid VehicleId)
    {
        public ShiftService Shifts()
        {
            var tenant = new DevTenantContext(TenantId);
            var clock = new TestClock(Now);
            return new ShiftService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)),
                new NotificationService(Db.Context, tenant, new DevCurrentUserContext(null), clock),
                new NoOpCalendarSyncService(), clock);
        }

        public PlanningConflictService TripConflicts()
        {
            var tenant = new DevTenantContext(TenantId);
            return new PlanningConflictService(Db.Context, tenant, new QualificationStatusCalculator(), new TestClock(Now));
        }
    }

    private static async Task<Harness> SeedAsync(
        string trainingSeverity = "Warning", string shiftOverlapSeverity = "Warning")
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            TrainingConflictSeverity = trainingSeverity, ShiftOverlapConflictSeverity = shiftOverlapSeverity,
        });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
            FirstName = "Jan", LastName = "Jansen", IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.Vehicles.Add(new TransportationService.Api.Modules.Fleet.Entities.Vehicle
        {
            Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-1", LicensePlate = "1-A-1", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, employeeId, driverId, vehicleId);
    }

    private static TripPlanningEntry TripEntry(Harness h, TripStatus status = TripStatus.Planned,
        TimeOnly? start = null, TimeOnly? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripId = Guid.NewGuid(),
        EmployeeId = h.EmployeeId, DriverId = h.DriverId, TripNumber = "RIT-0001",
        Date = Date, Status = status,
        PlannedStart = start is { } s ? Date.ToDateTime(s) : null,
        PlannedEnd = end is { } e ? Date.ToDateTime(e) : null,
    };

    private static Absence Approved(Harness h, AbsenceType type) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
        Type = type, Status = AbsenceStatus.Approved, StartDate = Date, EndDate = Date,
    };

    private static Trip Trip(Harness h, TimeOnly? start = null, TimeOnly? end = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TripNumber = "RIT-0099", TripDate = Date,
        DriverId = h.DriverId, VehicleId = h.VehicleId, Status = TripStatus.Draft,
        PlannedStart = start is { } s ? Date.ToDateTime(s) : null,
        PlannedEnd = end is { } e ? Date.ToDateTime(e) : null,
        Orders = [new TripOrder { Id = Guid.NewGuid(), TenantId = h.TenantId, TransportOrderId = Guid.NewGuid(), Sequence = 1 }],
    };

    [Fact]
    public async Task Grid_TripVsApprovedLeave_IsBlockingOnBothEntries()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TripPlanningEntries.Add(TripEntry(h));
        h.Db.Context.Absences.Add(Approved(h, AbsenceType.Vacation));
        await h.Db.Context.SaveChangesAsync();

        var grid = await h.Shifts().GetScheduleAsync(Date, Date, null, null, CancellationToken.None);
        var entries = grid.Rows.Single().Days.Single().Entries;

        var trip = entries.Single(e => e.SourceType == "Trip");
        var leave = entries.Single(e => e.SourceType == "Absence");
        Assert.Equal(ConflictSeverity.Blocking, trip.ConflictSeverity);
        Assert.Equal(ConflictSeverity.Blocking, leave.ConflictSeverity);
        Assert.NotNull(trip.ConflictNotes);
        Assert.Contains(trip.ConflictNotes!, n => n.Contains("afwezigheid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Grid_TwoOverlappingTrips_Block()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TripPlanningEntries.Add(TripEntry(h, start: new(8, 0), end: new(16, 0)));
        var second = TripEntry(h, start: new(14, 0), end: new(20, 0));
        second.TripNumber = "RIT-0002";
        h.Db.Context.TripPlanningEntries.Add(second);
        await h.Db.Context.SaveChangesAsync();

        var entries = (await h.Shifts().GetScheduleAsync(Date, Date, null, null, CancellationToken.None))
            .Rows.Single().Days.Single().Entries;

        Assert.All(entries.Where(e => e.SourceType == "Trip"),
            e => Assert.Equal(ConflictSeverity.Blocking, e.ConflictSeverity));
    }

    [Fact]
    public async Task Grid_NonOverlappingTripAndShift_NoConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TripPlanningEntries.Add(TripEntry(h, start: new(6, 0), end: new(10, 0)));
        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Date = Date, StartTime = new(12, 0), EndTime = new(18, 0), Type = ShiftType.Work,
        });
        await h.Db.Context.SaveChangesAsync();

        var entries = (await h.Shifts().GetScheduleAsync(Date, Date, null, null, CancellationToken.None))
            .Rows.Single().Days.Single().Entries;

        Assert.All(entries, e => Assert.Null(e.ConflictSeverity));
    }

    [Fact]
    public async Task Grid_TrainingAbsenceVsTrip_UsesConfiguredSeverity()
    {
        var warn = await SeedAsync(trainingSeverity: "Warning");
        using (warn.Db)
        {
            warn.Db.Context.TripPlanningEntries.Add(TripEntry(warn));
            warn.Db.Context.Absences.Add(Approved(warn, AbsenceType.Training));
            await warn.Db.Context.SaveChangesAsync();
            var trip = (await warn.Shifts().GetScheduleAsync(Date, Date, null, null, CancellationToken.None))
                .Rows.Single().Days.Single().Entries.Single(e => e.SourceType == "Trip");
            Assert.Equal(ConflictSeverity.Warning, trip.ConflictSeverity);
        }

        var block = await SeedAsync(trainingSeverity: "Blocking");
        using (block.Db)
        {
            block.Db.Context.TripPlanningEntries.Add(TripEntry(block));
            block.Db.Context.Absences.Add(Approved(block, AbsenceType.Training));
            await block.Db.Context.SaveChangesAsync();
            var trip = (await block.Shifts().GetScheduleAsync(Date, Date, null, null, CancellationToken.None))
                .Rows.Single().Days.Single().Entries.Single(e => e.SourceType == "Trip");
            Assert.Equal(ConflictSeverity.Blocking, trip.ConflictSeverity);
        }
    }

    [Fact]
    public async Task ShiftCreate_OverApprovedLeave_BlocksWithoutOverride_AllowsWithPermittedOverride()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Absences.Add(Approved(h, AbsenceType.Vacation));
        await h.Db.Context.SaveChangesAsync();

        var request = new CreateShiftRequest(h.EmployeeId, Date, new(8, 0), new(16, 0), 30, ShiftType.Work, null, null, null);
        var blocked = await h.Shifts().CreateAsync(request, false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Conflict, blocked.Outcome);
        Assert.NotNull(blocked.Conflicts);
        Assert.Contains(blocked.Conflicts!, c => c.Contains("afwezig"));

        // Override requested but caller lacks the permission → still refused (controller passes false).
        var withOverrideFlagOnly = await h.Shifts().CreateAsync(
            request with { Override = true }, false, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Conflict, withOverrideFlagOnly.Outcome);

        var overridden = await h.Shifts().CreateAsync(request with { Override = true }, true, CancellationToken.None);
        Assert.Equal(ShiftOutcome.Success, overridden.Outcome);
    }

    [Fact]
    public async Task ShiftCreate_OverTrip_DefaultWarning_DoesNotBlock_ButBlocksWhenConfigured()
    {
        var warn = await SeedAsync(shiftOverlapSeverity: "Warning");
        using (warn.Db)
        {
            warn.Db.Context.TripPlanningEntries.Add(TripEntry(warn, start: new(8, 0), end: new(16, 0)));
            await warn.Db.Context.SaveChangesAsync();
            var result = await warn.Shifts().CreateAsync(
                new CreateShiftRequest(warn.EmployeeId, Date, new(10, 0), new(18, 0), 30, ShiftType.Work, null, null, null),
                false, CancellationToken.None);
            Assert.Equal(ShiftOutcome.Success, result.Outcome);
        }

        var block = await SeedAsync(shiftOverlapSeverity: "Blocking");
        using (block.Db)
        {
            block.Db.Context.TripPlanningEntries.Add(TripEntry(block, start: new(8, 0), end: new(16, 0)));
            await block.Db.Context.SaveChangesAsync();
            var result = await block.Shifts().CreateAsync(
                new CreateShiftRequest(block.EmployeeId, Date, new(10, 0), new(18, 0), 30, ShiftType.Work, null, null, null),
                false, CancellationToken.None);
            Assert.Equal(ShiftOutcome.Conflict, result.Outcome);
        }
    }

    [Fact]
    public async Task TripEngine_ShiftOverlap_WarnsByDefault_BlocksWhenConfigured()
    {
        var warn = await SeedAsync(shiftOverlapSeverity: "Warning");
        using (warn.Db)
        {
            warn.Db.Context.Shifts.Add(new Shift
            {
                Id = Guid.NewGuid(), TenantId = warn.TenantId, EmployeeId = warn.EmployeeId,
                Date = Date, StartTime = new(8, 0), EndTime = new(16, 0), Type = ShiftType.Work,
            });
            await warn.Db.Context.SaveChangesAsync();

            var conflicts = await warn.TripConflicts().EvaluateAsync(Trip(warn, new(9, 0), new(17, 0)), CancellationToken.None);
            var overlap = conflicts.Single(c => c.Code == PlanningConflictCode.DriverShiftOverlap);
            Assert.Equal(ConflictSeverity.Warning, overlap.Severity);
            Assert.False(overlap.Blocking);
        }

        var block = await SeedAsync(shiftOverlapSeverity: "Blocking");
        using (block.Db)
        {
            block.Db.Context.Shifts.Add(new Shift
            {
                Id = Guid.NewGuid(), TenantId = block.TenantId, EmployeeId = block.EmployeeId,
                Date = Date, StartTime = new(8, 0), EndTime = new(16, 0), Type = ShiftType.Work,
            });
            await block.Db.Context.SaveChangesAsync();

            var conflicts = await block.TripConflicts().EvaluateAsync(Trip(block, new(9, 0), new(17, 0)), CancellationToken.None);
            var overlap = conflicts.Single(c => c.Code == PlanningConflictCode.DriverShiftOverlap);
            Assert.Equal(ConflictSeverity.Blocking, overlap.Severity);
            Assert.True(overlap.Blocking);
        }
    }

    [Fact]
    public async Task TripEngine_NonOverlappingShift_NoConflict()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId,
            Date = Date, StartTime = new(6, 0), EndTime = new(8, 0), Type = ShiftType.Work,
        });
        await h.Db.Context.SaveChangesAsync();

        var conflicts = await h.TripConflicts().EvaluateAsync(Trip(h, new(9, 0), new(17, 0)), CancellationToken.None);

        Assert.DoesNotContain(conflicts, c => c.Code == PlanningConflictCode.DriverShiftOverlap);
    }

    [Fact]
    public async Task TripEngine_ApprovedSickAbsence_Blocks_TrainingConfigurable()
    {
        var h = await SeedAsync(trainingSeverity: "Warning");
        using var _ = h.Db;
        h.Db.Context.Absences.AddRange(Approved(h, AbsenceType.Sick), Approved(h, AbsenceType.Training));
        await h.Db.Context.SaveChangesAsync();

        var conflicts = await h.TripConflicts().EvaluateAsync(Trip(h), CancellationToken.None);

        var sick = conflicts.Single(c => c.Code == PlanningConflictCode.DriverAbsent);
        Assert.Equal(ConflictSeverity.Blocking, sick.Severity);
        var training = conflicts.Single(c => c.Code == PlanningConflictCode.DriverTraining);
        Assert.Equal(ConflictSeverity.Warning, training.Severity);
    }

    [Fact]
    public async Task SeverityParsing_UnknownValue_FallsBackToWarning()
    {
        Assert.Equal(ConflictSeverity.Warning, ScheduleConflictRules.Parse("nonsense"));
        Assert.Equal(ConflictSeverity.Warning, ScheduleConflictRules.Parse(null));
        Assert.Equal(ConflictSeverity.Blocking, ScheduleConflictRules.Parse("blocking"));
        Assert.Equal(ConflictSeverity.Information, ScheduleConflictRules.Parse("Information"));
    }
}
