using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Scheduling;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Orders.Entities;
using TransportationService.Api.Modules.Partners.Entities;
using TransportationService.Api.Modules.Planning.Dtos;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Planning.Services;
using TransportationService.Api.Modules.Qualifications;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit.Abstractions;

namespace TransportationService.Api.Tests.Planning;

/// <summary>
/// PERF-BLOCKER 3 (performance audit 2026-10-21): the planning board and the trip list ran the
/// conflict engine once per trip, ~15–20 statements each (1,924 statements for a 3-day board,
/// 5,040 for a 14-day trip list on the 12k-trip clone). The engine now loads one fact snapshot
/// for the whole set and judges every trip in memory with the SAME rule code. This fixture —
/// 40 trips over 5 days with every conflict family present — pins (a) the exact verdict per
/// trip, (b) single-trip == batch, and (c) a statement count that grows per fact category,
/// not per trip.
/// </summary>
public class PlanningConflictBatchingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly D0 = new(2026, 7, 20);

    /// <summary>Upper bound for one board / trip-list read of 40 trips (measured: see the test output).</summary>
    private const int MaxStatementsPerRead = 35;

    private readonly ITestOutputHelper _output;

    public PlanningConflictBatchingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static DateOnly Day(int offset) => D0.AddDays(offset);

    private sealed class Fixture : IDisposable
    {
        public required SqliteTestDbContext Db { get; init; }
        public required QueryCountingInterceptor Counter { get; init; }
        public required Guid TenantId { get; init; }
        public required PlanningConflictService Conflicts { get; init; }
        public required TripService Trips { get; init; }
        public required PlanningBoardService Board { get; init; }
        /// <summary>Scenario key ("S1".."S20", "C{day}{n}") → trip id.</summary>
        public required IReadOnlyDictionary<string, Guid> TripIds { get; init; }

        public string KeyOf(Guid tripId) => TripIds.First(kv => kv.Value == tripId).Key;

        public Task<List<Trip>> LoadTripsAsync() => Db.Context.Trips.AsNoTracking()
            .Include(t => t.Orders)
            .Where(t => t.TenantId == TenantId)
            .OrderBy(t => t.TripDate).ThenBy(t => t.TripNumber)
            .ToListAsync();

        public void Dispose() => Db.Dispose();
    }

    /// <summary>
    /// 5 days × 8 trips. Four "clean" trips per day (distinct driver/vehicle pairs, a valid C
    /// licence, a 500 kg order on a 10 t vehicle) and four scenario trips per day, each exercising
    /// one or more rule families. Resources are unique per day except where a double booking is
    /// the point (S14/S15) or the other trip is not occupying (S17 cancelled, S18 completed).
    /// </summary>
    private static async Task<Fixture> SeedAsync()
    {
        var counter = new QueryCountingInterceptor();
        var db = new SqliteTestDbContext(null, counter);
        var ctx = db.Context;
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var tripIds = new Dictionary<string, Guid>();
        var employees = new Dictionary<string, Guid>();

        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        ctx.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, QualificationExpiryWarningDays = 30 });
        ctx.Customers.Add(new Customer { Id = customerId, TenantId = tenantId, CustomerNumber = "KL-1", Name = "Haven BV", IsActive = true });

        var licenceC = new QualificationType
        {
            Id = Guid.NewGuid(), Code = QualificationTypeCodes.DrivingLicenceC, Name = "Rijbewijs C",
            Category = "Rijbewijs", RequiresExpiryDate = true, IsActive = true,
        };
        ctx.QualificationTypes.Add(licenceC);

        Guid Driver(string key, EmploymentStatus status = EmploymentStatus.Active, bool blocked = false, string? blockReason = null)
        {
            var employeeId = Guid.NewGuid();
            var driverId = Guid.NewGuid();
            ctx.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = tenantId, EmployeeNumber = $"MED-{key}", FirstName = key, LastName = "Jansen",
                EmploymentStatus = status, IsActive = true, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
            ctx.Drivers.Add(new Driver
            {
                Id = driverId, TenantId = tenantId, EmployeeId = employeeId, DriverNumber = $"CH-{key}",
                IsActive = true, IsBlocked = blocked, BlockReason = blockReason,
            });
            ctx.EmployeeQualifications.Add(new EmployeeQualification
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, QualificationTypeId = licenceC.Id,
                ObtainedDate = new(2020, 1, 1), ExpiryDate = new(2030, 1, 1), Status = QualificationStatus.Valid,
            });
            employees[key] = employeeId;
            return driverId;
        }

        Guid Vehicle(string number, decimal? payload = 10000, VehicleOperationalStatus status = VehicleOperationalStatus.Available,
            bool active = true, bool tailLift = false, decimal? tailLiftCapacity = null, string? requiredLicence = null)
        {
            var id = Guid.NewGuid();
            ctx.Vehicles.Add(new Vehicle
            {
                Id = id, TenantId = tenantId, InternalNumber = number, LicensePlate = $"1-{number}", PayloadKg = payload,
                OperationalStatus = status, IsActive = active, AdrSuitable = true, HasTailLift = tailLift,
                TailLiftCapacityKg = tailLiftCapacity, RequiredLicenceCode = requiredLicence,
            });
            return id;
        }

        Guid Trailer(string number, bool active = true)
        {
            var id = Guid.NewGuid();
            ctx.Trailers.Add(new Trailer
            {
                Id = id, TenantId = tenantId, InternalNumber = number, LicensePlate = $"O-{number}",
                IsActive = active, AdrSuitable = true, CapacityKg = 20000,
            });
            return id;
        }

        var orderSeq = 0;
        Guid Order(decimal? weight, bool adr, decimal? unitWeight)
        {
            var id = Guid.NewGuid();
            ctx.TransportOrders.Add(new TransportOrder
            {
                Id = id, TenantId = tenantId, CustomerId = customerId, OrderNumber = $"ORD-{++orderSeq:000}",
                OrderDate = D0, Status = TransportOrderStatus.Confirmed, GoodsDescription = "Paletten",
                WeightKg = weight, AdrRequired = adr,
            });
            if (unitWeight is { } unit)
            {
                ctx.CargoItems.Add(new CargoItem
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = id, Sequence = 1,
                    Description = "Machine", ExpectedQuantity = 1, WeightPerUnitKg = unit,
                });
            }

            return id;
        }

        void Trip(string key, int day, Guid? driver, Guid? vehicle, Guid? trailer = null,
            TripStatus status = TripStatus.Planned, bool withOrder = true, decimal? weight = 500, bool adr = false,
            decimal? unitWeight = null, int startHour = 8, int endHour = 16)
        {
            var date = Day(day);
            var trip = new Trip
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = $"RIT-{key}", TripDate = date,
                DriverId = driver, VehicleId = vehicle, TrailerId = trailer, Status = status,
                PlannedStart = new DateTime(date.Year, date.Month, date.Day, startHour, 0, 0, DateTimeKind.Utc),
                PlannedEnd = new DateTime(date.Year, date.Month, date.Day, endHour, 0, 0, DateTimeKind.Utc),
            };
            if (withOrder)
            {
                trip.Orders =
                [
                    new TripOrder { Id = Guid.NewGuid(), TenantId = tenantId, TransportOrderId = Order(weight, adr, unitWeight), Sequence = 1 },
                ];
            }

            ctx.Trips.Add(trip);
            tripIds[key] = trip.Id;
        }

        // --- Drivers -----------------------------------------------------------------------
        var d1 = Driver("d1");
        var d2 = Driver("d2");                                   // approved vacation day 1–2
        var d3 = Driver("d3");                                   // work shift day 0, 08:00–12:00
        var d4 = Driver("d4");                                   // no ADR certificate
        var d5 = Driver("d5", blocked: true, blockReason: "Rijverbod");
        var d6 = Driver("d6");                                   // approved training day 3
        var d7 = Driver("d7", EmploymentStatus.Terminated);      // H5: HR status wins
        var d8 = Driver("d8");
        var d9 = Driver("d9");
        var d10 = Driver("d10");
        var d11 = Driver("d11");

        ctx.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employees["d2"], Type = AbsenceType.Vacation,
            StartDate = Day(1), EndDate = Day(2), Status = AbsenceStatus.Approved,
        });
        ctx.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employees["d6"], Type = AbsenceType.Training,
            StartDate = Day(3), EndDate = Day(3), Status = AbsenceStatus.Approved,
        });
        ctx.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employees["d3"], Date = Day(0),
            StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), Type = ShiftType.Work, Status = ShiftStatus.Planned,
        });

        // --- Vehicles / trailers -------------------------------------------------------------
        var v1 = Vehicle("VRT-1");
        var v2 = Vehicle("VRT-2", payload: 1000);                                  // small: 2 t order overloads it
        var v3 = Vehicle("VRT-3", status: VehicleOperationalStatus.InUse);        // "in gebruik" warning
        var v4 = Vehicle("VRT-4", tailLift: true, tailLiftCapacity: 500);          // D4 tail-lift check
        var v5 = Vehicle("VRT-5", active: false);
        var v6 = Vehicle("VRT-6", requiredLicence: "CE");                          // drivers only hold C
        var v7 = Vehicle("VRT-7");
        var v8 = Vehicle("VRT-8");
        var v9 = Vehicle("VRT-9");
        var v10 = Vehicle("VRT-10");
        var t1 = Trailer("OPL-1");
        var t2 = Trailer("OPL-2", active: false);

        // --- Clean trips: 4 per day, distinct pairs --------------------------------------------
        Guid[] cleanDrivers = [d1, d8, d9, d11];
        Guid[] cleanVehicles = [v1, v7, v8, v9];
        for (var day = 0; day < 5; day++)
        {
            for (var i = 0; i < cleanDrivers.Length; i++)
            {
                Trip($"C{day}{i}", day, cleanDrivers[i], cleanVehicles[i]);
            }
        }

        // --- Scenario trips: 4 per day ----------------------------------------------------------
        Trip("S1", 0, d3, v10, startHour: 9, endHour: 11);          // shift 08–12 overlaps 09–11
        Trip("S2", 0, d5, v2);                                     // blocked driver
        Trip("S3", 0, null, v3);                                   // no driver + vehicle in use
        Trip("S4", 0, d7, v5);                                     // terminated driver + inactive vehicle

        Trip("S5", 1, d2, v10);                                    // vacation (day 1)
        Trip("S6", 1, d4, v2, adr: true);                          // ADR order, driver without certificate
        Trip("S7", 1, d6, v3);                                     // vehicle in use
        Trip("S8", 1, d5, v6);                                     // blocked + CE vehicle with a C driver

        Trip("S9", 2, d2, v10);                                    // vacation (day 2)
        Trip("S10", 2, d4, v2, weight: 2000);                      // 2 t on a 1 t vehicle
        Trip("S11", 2, d6, v4, weight: null, unitWeight: 800);     // 800 kg unit on a 500 kg tail lift
        Trip("S12", 2, d5, v3);                                    // blocked + vehicle in use

        Trip("S13", 3, d6, v10);                                   // approved training (warning per tenant default)
        Trip("S14", 3, d4, v2);                                    // vehicle double-booked with S15
        Trip("S15", 3, d7, v2);                                    // terminated driver + same vehicle as S14
        Trip("S16", 3, d5, v3, trailer: t2);                       // blocked + in use + inactive trailer

        Trip("S17", 4, d1, v1, status: TripStatus.Cancelled);      // same resources as C40 — only S17 sees it
        Trip("S18", 4, d8, v7, status: TripStatus.Completed);      // same resources as C41 — only S18 sees it
        Trip("S19", 4, d10, v10, withOrder: false);                // no orders
        Trip("S20", 4, d6, null, trailer: t1);                     // no vehicle (capacity rides the trailer)

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var tenant = new DevTenantContext(tenantId);
        var clock = new TestClock(Now);
        var conflicts = new PlanningConflictService(ctx, tenant, new QualificationStatusCalculator(), clock);
        var trips = new TripService(ctx, tenant,
            new AuditService(ctx, tenant, new DevCurrentUserContext(null)), conflicts,
            new NotificationService(ctx, tenant, new DevCurrentUserContext(null), clock),
            new TripPlanningSyncService(ctx, tenant),
            CostingTestFactory.Create(ctx, tenant, clock),
            TripPackageTestFactory.Create(ctx, tenant, clock));
        var board = new PlanningBoardService(ctx, tenant, conflicts, new QualificationStatusCalculator(), clock);
        counter.Reset();

        return new Fixture
        {
            Db = db, Counter = counter, TenantId = tenantId, Conflicts = conflicts, Trips = trips, Board = board, TripIds = tripIds,
        };
    }

    private static readonly IReadOnlyDictionary<string, (PlanningConflictCode Code, ConflictSeverity Severity)[]> Expected =
        new Dictionary<string, (PlanningConflictCode, ConflictSeverity)[]>
        {
            ["S1"] = [(PlanningConflictCode.DriverShiftOverlap, ConflictSeverity.Warning)],
            ["S2"] = [(PlanningConflictCode.DriverBlocked, ConflictSeverity.Blocking)],
            ["S3"] = [(PlanningConflictCode.MissingDriver, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleNotOperational, ConflictSeverity.Warning)],
            ["S4"] = [(PlanningConflictCode.DriverInactive, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleInactive, ConflictSeverity.Blocking)],
            ["S5"] = [(PlanningConflictCode.DriverAbsent, ConflictSeverity.Blocking)],
            ["S6"] = [(PlanningConflictCode.DriverNotReady, ConflictSeverity.Blocking)],
            ["S7"] = [(PlanningConflictCode.VehicleNotOperational, ConflictSeverity.Warning)],
            ["S8"] = [(PlanningConflictCode.DriverBlocked, ConflictSeverity.Blocking), (PlanningConflictCode.DriverLicenceInsufficient, ConflictSeverity.Blocking)],
            ["S9"] = [(PlanningConflictCode.DriverAbsent, ConflictSeverity.Blocking)],
            ["S10"] = [(PlanningConflictCode.CapacityExceeded, ConflictSeverity.Blocking)],
            ["S11"] = [(PlanningConflictCode.TailLiftCapacityExceeded, ConflictSeverity.Warning)],
            ["S12"] = [(PlanningConflictCode.DriverBlocked, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleNotOperational, ConflictSeverity.Warning)],
            ["S13"] = [(PlanningConflictCode.DriverTraining, ConflictSeverity.Warning)],
            ["S14"] = [(PlanningConflictCode.VehicleDoubleBooked, ConflictSeverity.Blocking)],
            ["S15"] = [(PlanningConflictCode.DriverInactive, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleDoubleBooked, ConflictSeverity.Blocking)],
            ["S16"] = [(PlanningConflictCode.DriverBlocked, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleNotOperational, ConflictSeverity.Warning), (PlanningConflictCode.TrailerInactive, ConflictSeverity.Blocking)],
            // The engine itself judges a cancelled/completed trip like any other (the trip LIST skips
            // them, MD-M6): both collide with the clean Planned trip on the same resources.
            ["S17"] = [(PlanningConflictCode.DriverDoubleBooked, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleDoubleBooked, ConflictSeverity.Blocking)],
            ["S18"] = [(PlanningConflictCode.DriverDoubleBooked, ConflictSeverity.Blocking), (PlanningConflictCode.VehicleDoubleBooked, ConflictSeverity.Blocking)],
            ["S19"] = [(PlanningConflictCode.NoOrders, ConflictSeverity.Blocking)],
            ["S20"] = [(PlanningConflictCode.MissingVehicle, ConflictSeverity.Blocking)],
        };

    private static (PlanningConflictCode, ConflictSeverity)[] ExpectedFor(string key) =>
        Expected.TryGetValue(key, out var expected) ? expected : [];

    // -----------------------------------------------------------------------
    // (a) exact verdicts, (b) single == batch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EvaluateMany_ReturnsTheExpectedConflicts_PerTrip()
    {
        using var f = await SeedAsync();
        var trips = await f.LoadTripsAsync();
        Assert.Equal(40, trips.Count);

        var verdicts = await f.Conflicts.EvaluateManyAsync(trips, CancellationToken.None);

        Assert.Equal(40, verdicts.Count);
        foreach (var trip in trips)
        {
            var key = f.KeyOf(trip.Id);
            var actual = verdicts[trip.Id].Select(c => (c.Code, c.Severity)).ToArray();
            Assert.True(ExpectedFor(key).SequenceEqual(actual),
                $"{key}: expected [{string.Join(", ", ExpectedFor(key))}] but got [{string.Join(", ", actual)}]: "
                + string.Join(" | ", verdicts[trip.Id].Select(c => c.Description)));
        }
    }

    [Fact]
    public async Task EvaluateMany_CarriesTheSameMessagesAndEnrichment_AsTheSingleTripPath()
    {
        using var f = await SeedAsync();
        var trips = await f.LoadTripsAsync();

        var batch = await f.Conflicts.EvaluateManyAsync(trips, CancellationToken.None);

        foreach (var trip in trips)
        {
            var single = await f.Conflicts.EvaluateAsync(trip, CancellationToken.None);
            var singleton = (await f.Conflicts.EvaluateManyAsync([trip], CancellationToken.None))[trip.Id];
            // PlanningConflictDto is a record: value equality covers code, severity, description,
            // category, related entity, override metadata and suggested action.
            Assert.True(single.SequenceEqual(batch[trip.Id]), $"{f.KeyOf(trip.Id)}: single-trip and batch verdicts differ");
            Assert.True(single.SequenceEqual(singleton), $"{f.KeyOf(trip.Id)}: EvaluateAsync and EvaluateManyAsync([trip]) differ");
        }

        // Spot-check the texts the UI shows (unchanged by the batching).
        var s14 = batch[f.TripIds["S14"]].Single();
        Assert.Equal($"Voertuig VRT-2 is op {Day(3):dd/MM/yyyy} al ingepland op rit RIT-S15.", s14.Description);
        Assert.Equal("Vehicle", s14.RelatedEntityType);
        Assert.True(s14.OverrideAllowed);
        var s10 = batch[f.TripIds["S10"]].Single();
        Assert.Equal("Totaalgewicht 2000 kg overschrijdt het laadvermogen van 1000 kg van voertuig VRT-2 (+1000 kg).", s10.Description);
        Assert.Equal(ConflictCategory.Capacity, s10.Category);
        var s6 = batch[f.TripIds["S6"]].Single();
        Assert.Equal("ADR-transport: d4 Jansen heeft geen geldig ADR-certificaat.", s6.Description);
        var s5 = batch[f.TripIds["S5"]].Single();
        Assert.Equal($"Chauffeur d2 Jansen is afwezig op {Day(1):dd/MM/yyyy} (Verlof).", s5.Description);
    }

    [Fact]
    public async Task EvaluateMany_WithAnEmptySet_IssuesNoStatements()
    {
        using var f = await SeedAsync();

        var statements = await f.Counter.MeasureAsync(async () =>
            Assert.Empty(await f.Conflicts.EvaluateManyAsync([], CancellationToken.None)));

        Assert.Equal(0, statements);
    }

    // -----------------------------------------------------------------------
    // (c) statement counts: per category, not per trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Board_StatementCount_IsBoundedAndIndependentOfTheTripCount()
    {
        using var f = await SeedAsync();

        PlanningBoardDto? oneDay = null, fiveDays = null;
        var oneDayStatements = await f.Counter.MeasureAsync(async () => oneDay = await f.Board.GetBoardAsync(Day(3), Day(3), CancellationToken.None));
        var fiveDayStatements = await f.Counter.MeasureAsync(async () => fiveDays = await f.Board.GetBoardAsync(Day(0), Day(4), CancellationToken.None));
        _output.WriteLine($"planning board: {oneDay!.Trips.Count} trips → {oneDayStatements} statements; {fiveDays!.Trips.Count} trips → {fiveDayStatements} statements");

        Assert.Equal(8, oneDay.Trips.Count);
        Assert.Equal(40, fiveDays.Trips.Count);
        Assert.True(fiveDayStatements <= MaxStatementsPerRead,
            $"board for 40 trips issued {fiveDayStatements} statements (max {MaxStatementsPerRead}):\n{string.Join("\n", f.Counter.Statements.TakeLast(fiveDayStatements).Select(s => s.Split('\n')[0]))}");
        Assert.InRange(fiveDayStatements - oneDayStatements, -2, 2);

        // The board judges every trip, cancelled/completed included (it is the dispatcher's view).
        var byKey = fiveDays.Trips.ToDictionary(t => f.KeyOf(t.Id));
        Assert.Equal(0, byKey["C00"].BlockingConflictCount);
        Assert.Equal(0, byKey["C00"].WarningConflictCount);
        Assert.Equal(2, byKey["S16"].BlockingConflictCount);
        Assert.Equal(1, byKey["S16"].WarningConflictCount);
        Assert.Equal(2, byKey["S17"].BlockingConflictCount);
        Assert.Equal(1, byKey["S14"].BlockingConflictCount);
        Assert.Contains($"Voertuig VRT-2 is op {Day(3):dd/MM/yyyy} al ingepland op rit RIT-S15.", byKey["S14"].ConflictPreview!);
    }

    [Fact]
    public async Task TripList_StatementCount_IsBoundedAndIndependentOfTheTripCount()
    {
        using var f = await SeedAsync();

        IReadOnlyList<TripListItemDto>? oneDay = null, fiveDays = null;
        var oneDayStatements = await f.Counter.MeasureAsync(async () => oneDay = await f.Trips.ListAsync(Day(3), Day(3), null, null, CancellationToken.None));
        var fiveDayStatements = await f.Counter.MeasureAsync(async () => fiveDays = await f.Trips.ListAsync(Day(0), Day(4), null, null, CancellationToken.None));
        _output.WriteLine($"trip list: {oneDay!.Count} trips → {oneDayStatements} statements; {fiveDays!.Count} trips → {fiveDayStatements} statements");

        Assert.Equal(8, oneDay.Count);
        Assert.Equal(40, fiveDays.Count);
        Assert.True(fiveDayStatements <= MaxStatementsPerRead,
            $"trip list for 40 trips issued {fiveDayStatements} statements (max {MaxStatementsPerRead}):\n{string.Join("\n", f.Counter.Statements.TakeLast(fiveDayStatements).Select(s => s.Split('\n')[0]))}");
        Assert.InRange(fiveDayStatements - oneDayStatements, -2, 2);

        // MD-M6 stays: executed / cancelled history is never re-judged in the list.
        var byKey = fiveDays.ToDictionary(t => f.KeyOf(t.Id));
        Assert.Equal(0, byKey["S17"].BlockingConflictCount);
        Assert.Equal(0, byKey["S18"].BlockingConflictCount);
        Assert.Equal(2, byKey["S16"].BlockingConflictCount);
        Assert.Equal(1, byKey["S10"].BlockingConflictCount);
        Assert.Equal(0, byKey["S11"].BlockingConflictCount);
        Assert.Equal(0, byKey["C23"].BlockingConflictCount);
    }

    // -----------------------------------------------------------------------
    // PERF-HIGH 3: a capped list says it was cut
    // -----------------------------------------------------------------------

    [Fact]
    public async Task TripListAndBoard_BeyondTheCap_ReportTruncation_AndStillReadInOnePass()
    {
        using var f = await SeedAsync();
        // 40 fixture trips + 470 drafts on day 4 = 510 trips in the window, 10 over the cap.
        for (var i = 0; i < 470; i++)
        {
            f.Db.Context.Trips.Add(new Trip
            {
                Id = Guid.NewGuid(), TenantId = f.TenantId, TripNumber = $"RIT-X{i:000}", TripDate = Day(4), Status = TripStatus.Draft,
            });
        }

        await f.Db.Context.SaveChangesAsync();
        f.Db.Context.ChangeTracker.Clear();
        f.Counter.Reset();

        var list = await f.Trips.ListWithLimitAsync(Day(0), Day(4), null, null, CancellationToken.None);
        var listStatements = f.Counter.Count;
        f.Counter.Reset();
        var board = await f.Board.GetBoardAsync(Day(0), Day(4), CancellationToken.None);
        var boardStatements = f.Counter.Count;
        _output.WriteLine($"capped: trip list {list.Items.Count} rows → {listStatements} statements; board {board.Trips.Count} rows → {boardStatements} statements");

        Assert.True(list.Truncated);
        Assert.Equal(PlanningListLimits.Trips, list.Limit);
        Assert.Equal(PlanningListLimits.Trips, list.Items.Count);
        Assert.True(board.Truncated);
        Assert.Equal(PlanningListLimits.BoardTrips, board.Limit);
        Assert.Equal(PlanningListLimits.BoardTrips, board.Trips.Count);
        Assert.True(listStatements <= MaxStatementsPerRead, $"trip list at the cap issued {listStatements} statements");
        Assert.True(boardStatements <= MaxStatementsPerRead, $"board at the cap issued {boardStatements} statements");

        // Under the cap nothing is flagged.
        var oneDay = await f.Trips.ListWithLimitAsync(Day(3), Day(3), null, null, CancellationToken.None);
        Assert.False(oneDay.Truncated);
        Assert.Equal(8, oneDay.Items.Count);
        Assert.False((await f.Board.GetBoardAsync(Day(3), Day(3), CancellationToken.None)).Truncated);
    }

    [Fact]
    public async Task TripList_FilteredByStatus_StillSeesOccupyingTripsOutsideTheList()
    {
        using var f = await SeedAsync();
        // Promote S14 to InProgress: a list filtered on InProgress contains S14 alone, yet its
        // vehicle double booking with the Planned S15 (NOT in the list) must still be reported.
        var s14 = await f.Db.Context.Trips.SingleAsync(t => t.Id == f.TripIds["S14"]);
        s14.Status = TripStatus.InProgress;
        await f.Db.Context.SaveChangesAsync();
        f.Db.Context.ChangeTracker.Clear();

        var list = await f.Trips.ListAsync(null, null, TripStatus.InProgress, null, CancellationToken.None);

        var single = Assert.Single(list);
        Assert.Equal("RIT-S14", single.TripNumber);
        Assert.Equal(1, single.BlockingConflictCount);
    }
}
