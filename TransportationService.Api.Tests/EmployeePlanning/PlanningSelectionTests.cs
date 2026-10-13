using System.Data.Common;
using System.Diagnostics;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Controllers;
using TransportationService.Api.Modules.EmployeePlanning.Dtos;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.EmployeePlanning.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.EmployeePlanning;

/// <summary>
/// Personeelsplanning scalability wave (2026-10-07): ONE selection model shared by grid, list,
/// week-copy and Excel export. OR within a dimension, AND between dimensions, explicit picks are
/// pinned on top of group filters, the GLOBAL employee search ignores the planning filters, and
/// everything stays tenant-scoped and free of N+1 queries.
/// </summary>
public class PlanningSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 05, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Monday = new(2026, 10, 5);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    /// <summary>Counts SQL commands so a test can prove the grid is set-based (no per-row queries).</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Count++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Count++;
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public void Reset() => Count = 0;
    }

    private sealed class Harness : IDisposable
    {
        public SqliteTestDbContext Db { get; }
        public CommandCounter Counter { get; }
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid ForeignTenantId { get; } = Guid.NewGuid();
        public Guid Transport { get; } = Guid.NewGuid();
        public Guid Dispatch { get; } = Guid.NewGuid();
        public Guid Chauffeur { get; } = Guid.NewGuid();
        public Guid Dispatcher { get; } = Guid.NewGuid();
        public Guid Planner { get; } = Guid.NewGuid();
        public Guid Vast { get; } = Guid.NewGuid();
        public Guid Interim { get; } = Guid.NewGuid();
        public Guid Jan { get; } = Guid.NewGuid();       // Transport · Chauffeur · Vast · driver
        public Guid Marc { get; } = Guid.NewGuid();      // Dispatch · Dispatcher · Vast
        public Guid Els { get; } = Guid.NewGuid();       // Dispatch · Planner · Interim
        public Guid Tom { get; } = Guid.NewGuid();       // Transport · Chauffeur · inactive · driver
        public Guid Nadia { get; } = Guid.NewGuid();     // Dispatch · Dispatcher + Planner
        public Guid Foreign { get; } = Guid.NewGuid();   // other tenant
        public Guid PlannerUserId { get; } = Guid.NewGuid();

        public Harness()
        {
            Counter = new CommandCounter();
            Db = new SqliteTestDbContext(null, Counter);
        }

        public ShiftService Shifts(Guid? tenant = null) => CreateShifts(Db, tenant ?? TenantId);

        public PlanningEmployeeDirectoryService Directory(Guid? tenant = null, Guid? user = null) =>
            new(Db.Context, new DevTenantContext(tenant ?? TenantId), new DevCurrentUserContext(user ?? PlannerUserId));

        public EmployeePlanningExportService Export() => new(Shifts());

        public EmployeePlanningController Controller() => new(
            Shifts(), Directory(), Export(),
            new InventoryTestFactory.AllowAllPermissionService(), new DevCurrentUserContext(PlannerUserId));

        public void Dispose() => Db.Dispose();
    }

    private static ShiftService CreateShifts(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(null);
        return new ShiftService(
            db.Context, tenant,
            new AuditService(db.Context, tenant, user),
            new NotificationService(db.Context, tenant, user, new TestClock(Now)),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            new TestClock(Now));
    }

    private static Employee NewEmployee(Guid id, Guid tenantId, string number, string first, string last,
        Guid? department, Guid? contract, bool active = true) => new()
    {
        Id = id, TenantId = tenantId, EmployeeNumber = number, FirstName = first, LastName = last,
        DepartmentId = department, ContractTypeId = contract, IsActive = active,
        EmploymentStatus = active ? EmploymentStatus.Active : EmploymentStatus.Terminated,
        CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    };

    private static Shift NewShift(Guid tenantId, Guid employeeId, DateOnly date, string start = "06:00", string end = "14:00") => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, Date = date,
        StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end), BreakMinutes = 30,
        Type = ShiftType.Work, Status = ShiftStatus.Planned, WorkLocation = "Antwerpen",
    };

    private static async Task<Harness> SeedAsync()
    {
        var h = new Harness();
        var db = h.Db.Context;
        db.Tenants.AddRange(
            new Tenant { Id = h.TenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime },
            new Tenant { Id = h.ForeignTenantId, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Departments.AddRange(
            new Department { Id = h.Transport, TenantId = h.TenantId, Code = "TRA", Name = "Transport", IsActive = true, SortOrder = 1 },
            new Department { Id = h.Dispatch, TenantId = h.TenantId, Code = "DIS", Name = "Dispatch", IsActive = true, SortOrder = 2 });
        db.JobFunctions.AddRange(
            new JobFunction { Id = h.Chauffeur, TenantId = h.TenantId, Code = "CHA", Name = "Chauffeur", IsActive = true, SortOrder = 1 },
            new JobFunction { Id = h.Dispatcher, TenantId = h.TenantId, Code = "DSP", Name = "Dispatcher", IsActive = true, SortOrder = 2 },
            new JobFunction { Id = h.Planner, TenantId = h.TenantId, Code = "PLN", Name = "Planner", IsActive = true, SortOrder = 3 });
        db.ContractTypes.AddRange(
            new ContractType { Id = h.Vast, TenantId = h.TenantId, Code = "VAST", Name = "Vast", IsActive = true },
            new ContractType { Id = h.Interim, TenantId = h.TenantId, Code = "INT", Name = "Interim", IsActive = true });
        db.Employees.AddRange(
            NewEmployee(h.Jan, h.TenantId, "E-001", "Jan", "Peeters", h.Transport, h.Vast),
            NewEmployee(h.Marc, h.TenantId, "E-002", "Marc", "Janssens", h.Dispatch, h.Vast),
            NewEmployee(h.Els, h.TenantId, "E-003", "Els", "Claes", h.Dispatch, h.Interim),
            NewEmployee(h.Tom, h.TenantId, "E-004", "Tom", "Maes", h.Transport, h.Vast, active: false),
            NewEmployee(h.Nadia, h.TenantId, "E-005", "Nadia", "Peeters", h.Dispatch, null),
            NewEmployee(h.Foreign, h.ForeignTenantId, "X-001", "Jan", "Peeters", null, null));
        db.Set<EmployeeJobFunction>().AddRange(
            new EmployeeJobFunction { EmployeeId = h.Jan, JobFunctionId = h.Chauffeur },
            new EmployeeJobFunction { EmployeeId = h.Marc, JobFunctionId = h.Dispatcher },
            new EmployeeJobFunction { EmployeeId = h.Els, JobFunctionId = h.Planner },
            new EmployeeJobFunction { EmployeeId = h.Tom, JobFunctionId = h.Chauffeur },
            new EmployeeJobFunction { EmployeeId = h.Nadia, JobFunctionId = h.Dispatcher },
            new EmployeeJobFunction { EmployeeId = h.Nadia, JobFunctionId = h.Planner });
        db.Drivers.AddRange(
            new Driver { Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Jan, DriverNumber = "D-1", IsActive = true },
            new Driver { Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Tom, DriverNumber = "D-2", IsActive = false });
        db.Users.Add(new User
        {
            Id = h.PlannerUserId, TenantId = h.TenantId, Email = "els@acme.be", PasswordHash = "x",
            FirstName = "Els", LastName = "Claes", EmployeeId = h.Els, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Shifts.AddRange(
            NewShift(h.TenantId, h.Jan, Monday),
            NewShift(h.TenantId, h.Jan, Monday.AddDays(1)),
            NewShift(h.TenantId, h.Marc, Monday, "08:00", "16:30"),
            NewShift(h.TenantId, h.Els, Monday.AddDays(2)),
            NewShift(h.TenantId, h.Tom, Monday),
            NewShift(h.TenantId, h.Jan, Monday.AddDays(7)),          // next week: outside the period
            NewShift(h.TenantId, h.Marc, Monday.AddDays(-1)),        // previous week: outside the period
            NewShift(h.ForeignTenantId, h.Foreign, Monday));
        await db.SaveChangesAsync();
        h.Counter.Reset();
        return h;
    }

    private static ScheduleQuery Query(
        Guid[]? departments = null, Guid[]? functions = null, Guid[]? contracts = null,
        PlanningEmployeeStatus[]? statuses = null, bool driversOnly = false, Guid[]? employees = null) =>
        new(Monday, Sunday, departments ?? [], functions ?? [], contracts ?? [], statuses ?? [], driversOnly, employees ?? []);

    private static string[] Names(ScheduleGridDto grid) => grid.Rows.Select(r => r.EmployeeName).ToArray();

    // ---- filter semantics ---------------------------------------------------------------------

    [Fact]
    public async Task NoFilters_ReturnsAllActiveEmployees_WithScopeCount()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(), CancellationToken.None);

        Assert.Equal(["Els Claes", "Marc Janssens", "Jan Peeters", "Nadia Peeters"], Names(grid));
        Assert.Equal(4, grid.TotalInScope);
        Assert.All(grid.Rows, r => Assert.True(r.MatchesFilters));
        Assert.Contains(grid.Rows, r => r.EmployeeName == "Jan Peeters" && r.FunctionNames!.SequenceEqual(["Chauffeur"]) && r.ContractTypeName == "Vast" && r.IsActive);
    }

    [Fact]
    public async Task ExplicitEmployee_Single_ReturnsOnlyThatEmployee()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(employees: [h.Marc]), CancellationToken.None);
        Assert.Equal(["Marc Janssens"], Names(grid));
    }

    [Fact]
    public async Task ExplicitEmployees_Multiple_ReturnsExactlyThose_EvenInactive()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(employees: [h.Marc, h.Tom]), CancellationToken.None);
        Assert.Equal(["Marc Janssens", "Tom Maes"], Names(grid));
        Assert.All(grid.Rows, r => Assert.True(r.MatchesFilters));
    }

    [Fact]
    public async Task MultipleFunctions_IsOr()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(functions: [h.Chauffeur, h.Dispatcher]), CancellationToken.None);
        Assert.Equal(["Marc Janssens", "Jan Peeters", "Nadia Peeters"], Names(grid));
    }

    [Fact]
    public async Task MultipleDepartments_IsOr()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(departments: [h.Transport, h.Dispatch]), CancellationToken.None);
        Assert.Equal(4, grid.Rows.Count);
    }

    [Fact]
    public async Task DepartmentAndFunction_IsAnd()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(departments: [h.Dispatch], functions: [h.Dispatcher]), CancellationToken.None);
        Assert.Equal(["Marc Janssens", "Nadia Peeters"], Names(grid));
    }

    [Fact]
    public async Task ContractType_Filters_AndCombinesWithDepartment()
    {
        using var h = await SeedAsync();
        var interim = await h.Shifts().GetScheduleAsync(Query(contracts: [h.Interim]), CancellationToken.None);
        Assert.Equal(["Els Claes"], Names(interim));

        var vastDispatch = await h.Shifts().GetScheduleAsync(Query(departments: [h.Dispatch], contracts: [h.Vast]), CancellationToken.None);
        Assert.Equal(["Marc Janssens"], Names(vastDispatch));
    }

    [Fact]
    public async Task ExplicitEmployeeOutsideGroupFilters_IsIncludedAndMarked_NeverBlocked()
    {
        using var h = await SeedAsync();
        // Dispatch + Dispatcher active, and the planner explicitly adds Jan (Chauffeur, Transport).
        var grid = await h.Shifts().GetScheduleAsync(
            Query(departments: [h.Dispatch], functions: [h.Dispatcher], employees: [h.Jan]), CancellationToken.None);

        Assert.Equal(["Marc Janssens", "Jan Peeters", "Nadia Peeters"], Names(grid));
        Assert.False(grid.Rows.Single(r => r.EmployeeId == h.Jan).MatchesFilters);
        Assert.True(grid.Rows.Single(r => r.EmployeeId == h.Marc).MatchesFilters);
        // Jan's planning data travels with him.
        Assert.Equal(2, grid.Rows.Single(r => r.EmployeeId == h.Jan).Days.Sum(d => d.Entries.Count));
    }

    [Fact]
    public async Task ExplicitEmployeeAlsoMatchingGroup_AppearsOnce()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(departments: [h.Dispatch], employees: [h.Marc]), CancellationToken.None);
        Assert.Equal(["Els Claes", "Marc Janssens", "Nadia Peeters"], Names(grid));
        Assert.Single(grid.Rows, r => r.EmployeeId == h.Marc);
        Assert.True(grid.Rows.Single(r => r.EmployeeId == h.Marc).MatchesFilters);
    }

    [Fact]
    public async Task StatusScope_DefaultsToActive_InactiveOnRequest_BothPossible()
    {
        using var h = await SeedAsync();
        var inactive = await h.Shifts().GetScheduleAsync(Query(statuses: [PlanningEmployeeStatus.Inactive]), CancellationToken.None);
        Assert.Equal(["Tom Maes"], Names(inactive));
        Assert.Equal(1, inactive.TotalInScope);
        Assert.False(inactive.Rows[0].IsActive);

        var both = await h.Shifts().GetScheduleAsync(
            Query(statuses: [PlanningEmployeeStatus.Active, PlanningEmployeeStatus.Inactive]), CancellationToken.None);
        Assert.Equal(5, both.Rows.Count);
        Assert.Equal(5, both.TotalInScope);

        // Status is the scope of the GROUP: an inactive chauffeur disappears from "Chauffeur" by default …
        var chauffeurs = await h.Shifts().GetScheduleAsync(Query(functions: [h.Chauffeur]), CancellationToken.None);
        Assert.Equal(["Jan Peeters"], Names(chauffeurs));
        // … but never from an explicit pick.
        var pinnedTom = await h.Shifts().GetScheduleAsync(Query(functions: [h.Chauffeur], employees: [h.Tom]), CancellationToken.None);
        Assert.Equal(["Tom Maes", "Jan Peeters"], Names(pinnedTom));
        Assert.False(pinnedTom.Rows.Single(r => r.EmployeeId == h.Tom).MatchesFilters);
    }

    [Fact]
    public async Task DriversOnly_UsesTheDriverProfile_NotAFunctionName()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(driversOnly: true), CancellationToken.None);
        Assert.Equal(["Jan Peeters"], Names(grid));

        var withInactive = await h.Shifts().GetScheduleAsync(
            Query(driversOnly: true, statuses: [PlanningEmployeeStatus.Active, PlanningEmployeeStatus.Inactive]), CancellationToken.None);
        Assert.Equal(["Tom Maes", "Jan Peeters"], Names(withInactive));
    }

    [Fact]
    public async Task Period_OnlyReturnsEntriesInsideTheWindow()
    {
        using var h = await SeedAsync();
        var grid = await h.Shifts().GetScheduleAsync(Query(employees: [h.Jan, h.Marc]), CancellationToken.None);
        Assert.Equal(7, grid.Rows[0].Days.Count);
        Assert.Equal(2, grid.Rows.Single(r => r.EmployeeId == h.Jan).Days.Sum(d => d.Entries.Count));
        Assert.Equal(1, grid.Rows.Single(r => r.EmployeeId == h.Marc).Days.Sum(d => d.Entries.Count));
    }

    [Fact]
    public async Task LegacyOverload_KeepsItsMeaning()
    {
        using var h = await SeedAsync();
        var department = await h.Shifts().GetScheduleAsync(Monday, Sunday, h.Transport, null, CancellationToken.None);
        Assert.Equal(["Jan Peeters"], Names(department));
        var single = await h.Shifts().GetScheduleAsync(Monday, Sunday, null, h.Els, CancellationToken.None);
        Assert.Equal(["Els Claes"], Names(single));
    }

    // ---- tenant isolation -----------------------------------------------------------------------

    [Fact]
    public async Task TenantIsolation_ForeignIdsAndForeignCallerSeeNothingOfEachOther()
    {
        using var h = await SeedAsync();
        // Our planner asks for the foreign employee explicitly: filters are never a security boundary.
        var ours = await h.Shifts().GetScheduleAsync(Query(employees: [h.Foreign]), CancellationToken.None);
        Assert.Empty(ours.Rows);

        // The foreign tenant's planner sees only its own people, whatever ids are requested.
        var theirs = await h.Shifts(h.ForeignTenantId).GetScheduleAsync(Query(employees: [h.Jan, h.Foreign]), CancellationToken.None);
        Assert.Equal([h.Foreign], theirs.Rows.Select(r => r.EmployeeId));
        var theirSearch = await h.Directory(h.ForeignTenantId).SearchAsync("peeters", 20, CancellationToken.None);
        Assert.Equal([h.Foreign], theirSearch.Select(r => r.Id));
    }

    // ---- global search ------------------------------------------------------------------------------

    [Fact]
    public async Task GlobalSearch_FindsEmployeesRegardlessOfPlanningFilters_IncludingInactive()
    {
        using var h = await SeedAsync();
        var directory = h.Directory();

        // There is no filter parameter at all on the search: the planning filters cannot leak in.
        var peeters = await directory.SearchAsync("Peeters", 20, CancellationToken.None);
        Assert.Equal(["Jan Peeters", "Nadia Peeters"], peeters.Select(r => $"{r.FirstName} {r.LastName}"));
        Assert.Equal("Transport", peeters[0].DepartmentName);
        Assert.Equal(["Chauffeur"], peeters[0].FunctionNames);

        var multiWord = await directory.SearchAsync("peeters jan", 20, CancellationToken.None);
        Assert.Equal([h.Jan], multiWord.Select(r => r.Id));

        var byNumber = await directory.SearchAsync("E-00", 20, CancellationToken.None);
        Assert.Equal(5, byNumber.Count);
        Assert.True(byNumber.Take(4).All(r => r.IsActive));
        Assert.False(byNumber[4].IsActive); // Tom: still findable, flagged inactive, sorted last

        var all = await directory.SearchAsync(null, 3, CancellationToken.None);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task FilterOptions_ListActiveLookups_AndTheCallersOwnDepartment()
    {
        using var h = await SeedAsync();
        var options = await h.Directory().GetFilterOptionsAsync(CancellationToken.None);

        Assert.Equal(["Transport", "Dispatch"], options.Departments.Select(d => d.Name));
        Assert.Equal(["Chauffeur", "Dispatcher", "Planner"], options.JobFunctions.Select(j => j.Name));
        Assert.Equal(["Interim", "Vast"], options.ContractTypes.Select(c => c.Name));
        Assert.Equal(h.Dispatch, options.MyDepartmentId); // Els (the planner) works in Dispatch
        Assert.True(options.HasDrivers);

        var anonymous = await h.Directory(user: Guid.NewGuid()).GetFilterOptionsAsync(CancellationToken.None);
        Assert.Null(anonymous.MyDepartmentId);
    }

    // ---- copy week ------------------------------------------------------------------------------------

    [Fact]
    public async Task CopyWeek_WithoutSelection_StillCopiesTheWholeTenant()
    {
        using var h = await SeedAsync();
        var result = await h.Shifts().CopyWeekAsync(new CopyWeekRequest(Monday, Monday.AddDays(14), null, null), CancellationToken.None);

        Assert.Equal(ShiftOutcome.Success, result.Outcome);
        Assert.Equal(5, result.CopiedCount); // Jan ×2, Marc, Els, Tom — the pre-wave behaviour
    }

    [Fact]
    public async Task CopyWeek_WithSelection_CopiesExactlyTheEmployeesOnScreen()
    {
        using var h = await SeedAsync();
        var request = new CopyWeekRequest(Monday, Monday.AddDays(14), null, null,
            DepartmentIds: [h.Dispatch], JobFunctionIds: [h.Dispatcher], EmployeeIds: [h.Jan]);

        var result = await h.Shifts().CopyWeekAsync(request, CancellationToken.None);

        Assert.Equal(ShiftOutcome.Success, result.Outcome);
        Assert.Equal(3, result.CopiedCount); // Marc (Dispatch·Dispatcher) + Jan ×2 (pinned); not Els, not Tom
        var target = await h.Shifts().GetScheduleAsync(
            new ScheduleQuery(Monday.AddDays(14), Monday.AddDays(20), [], [], [], [PlanningEmployeeStatus.Active, PlanningEmployeeStatus.Inactive], false, []),
            CancellationToken.None);
        var copiedFor = target.Rows.Where(r => r.Days.Any(d => d.Entries.Count > 0)).Select(r => r.EmployeeId).ToHashSet();
        Assert.Equal(new HashSet<Guid> { h.Marc, h.Jan }, copiedFor);
    }

    // ---- export ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_UsesExactlyTheGridSelection_AndIsAReadableWorkbook()
    {
        using var h = await SeedAsync();
        var query = Query(departments: [h.Dispatch], functions: [h.Dispatcher], employees: [h.Jan]);
        var grid = await h.Shifts().GetScheduleAsync(query, CancellationToken.None);

        var (content, fileName) = await h.Export().BuildAsync(query, CancellationToken.None);

        Assert.Equal("personeelsplanning_2026-10-05_2026-10-11.xlsx", fileName);
        using var workbook = new XLWorkbook(new MemoryStream(content));
        Assert.Equal([EmployeePlanningExportService.PlanningSheetName, EmployeePlanningExportService.DetailsSheetName],
            workbook.Worksheets.Select(w => w.Name));

        var planning = workbook.Worksheet(EmployeePlanningExportService.PlanningSheetName);
        Assert.Equal("Medewerker", planning.Cell(1, 1).GetString());
        Assert.Equal("ma 05/10", planning.Cell(1, 5).GetString());
        Assert.Equal("zo 11/10", planning.Cell(1, 11).GetString());
        Assert.True(planning.Cell(1, 1).Style.Font.Bold);
        Assert.True(planning.AutoFilter.IsEnabled);
        Assert.Equal(1, planning.SheetView.SplitRow);
        Assert.Equal(1, planning.SheetView.SplitColumn);
        // Same people, same order as the grid.
        var exported = Enumerable.Range(2, grid.Rows.Count).Select(r => planning.Cell(r, 1).GetString()).ToArray();
        Assert.Equal(Names(grid), exported);
        Assert.Equal(string.Empty, planning.Cell(grid.Rows.Count + 2, 1).GetString());
        // Jan's Monday shift renders as a readable cell.
        var janRow = Array.IndexOf(exported, "Jan Peeters") + 2;
        Assert.Equal("06:00–14:00 Antwerpen (Gepland)", planning.Cell(janRow, 5).GetString());
        Assert.Equal("Chauffeur", planning.Cell(janRow, 4).GetString());

        var details = workbook.Worksheet(EmployeePlanningExportService.DetailsSheetName);
        Assert.Equal(EmployeePlanningExportService.DetailHeaders, Enumerable.Range(1, EmployeePlanningExportService.DetailHeaders.Length).Select(c => details.Cell(1, c).GetString()).ToArray());
        var totalEntries = grid.Rows.Sum(r => r.Days.Sum(d => d.Entries.Count));
        Assert.Equal(totalEntries, details.LastRowUsed()!.RowNumber() - 1);
        // First detail row = first grid row (Marc Janssens, Monday 08:00–16:30).
        Assert.Equal("Marc Janssens", details.Cell(2, 1).GetString());
        Assert.Equal("dd/mm/yyyy", details.Cell(2, 5).Style.DateFormat.Format);
        Assert.Equal(new DateTime(2026, 10, 5), details.Cell(2, 5).GetDateTime());
        Assert.Equal("08:00", details.Cell(2, 9).GetString());
        Assert.Equal("16:30", details.Cell(2, 10).GetString());
        Assert.Equal(8.5, details.Cell(2, 11).GetDouble());
        Assert.Equal("Shift · Werk", details.Cell(2, 6).GetString());
        Assert.True(details.AutoFilter.IsEnabled);
    }

    [Fact]
    public async Task Export_EmptySelection_IsAValidWorkbookWithHeadersOnly()
    {
        using var h = await SeedAsync();
        var (content, _) = await h.Export().BuildAsync(Query(departments: [Guid.NewGuid()]), CancellationToken.None);

        using var workbook = new XLWorkbook(new MemoryStream(content));
        var planning = workbook.Worksheet(EmployeePlanningExportService.PlanningSheetName);
        Assert.Equal("Medewerker", planning.Cell(1, 1).GetString());
        Assert.Equal(1, planning.LastRowUsed()!.RowNumber());
        var details = workbook.Worksheet(EmployeePlanningExportService.DetailsSheetName);
        Assert.Equal(1, details.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task Controller_Export_ReturnsXlsxContentTypeAndFileName_AndValidatesInput()
    {
        using var h = await SeedAsync();
        var controller = h.Controller();

        var result = await controller.Export(
            new EmployeePlanningController.ScheduleQueryParameters { From = Monday, To = Sunday, EmployeeIds = [h.Jan] },
            CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(EmployeePlanningExportService.ContentType, file.ContentType);
        Assert.Equal("personeelsplanning_2026-10-05_2026-10-11.xlsx", file.FileDownloadName);
        Assert.NotEmpty(file.FileContents);

        var tooLong = await controller.Export(
            new EmployeePlanningController.ScheduleQueryParameters { From = Monday, To = Monday.AddDays(90) }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(tooLong);

        var tooManyIds = await controller.Schedule(
            new EmployeePlanningController.ScheduleQueryParameters
            {
                From = Monday, To = Sunday,
                EmployeeIds = Enumerable.Range(0, EmployeePlanningController.MaxFilterIds + 1).Select(_ => Guid.NewGuid()).ToArray(),
            }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(tooManyIds.Result);
    }

    [Fact]
    public async Task Controller_Schedule_FoldsLegacyParametersIntoTheSelection()
    {
        using var h = await SeedAsync();
        var result = await h.Controller().Schedule(
            new EmployeePlanningController.ScheduleQueryParameters { From = Monday, To = Sunday, DepartmentId = h.Transport },
            CancellationToken.None);

        var grid = Assert.IsType<ScheduleGridDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["Jan Peeters"], Names(grid));
    }

    [Fact]
    public void PlanningEndpoints_RequireThePlanningPermission()
    {
        foreach (var action in new[] { "Schedule", "SearchEmployees", "FilterOptions", "Export" })
        {
            var method = typeof(EmployeePlanningController).GetMethod(action)!;
            var attribute = Assert.IsType<RequirePermissionAttribute>(
                method.GetCustomAttributes(typeof(RequirePermissionAttribute), false).Single());
            Assert.Equal([PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage], attribute.PermissionCodes);
        }
    }

    // ---- scalability ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(100)]
    [InlineData(300)]
    [InlineData(500)]
    public async Task Grid_ScalesWithBoundedQueryCount_AndNoDuplicateRows(int headcount)
    {
        using var h = await SeedAsync();
        var db = h.Db.Context;
        var extra = new List<Guid>();
        for (var i = 0; i < headcount; i++)
        {
            var id = Guid.NewGuid();
            extra.Add(id);
            db.Employees.Add(NewEmployee(id, h.TenantId, $"S-{i:0000}", $"Vn{i}", $"An{i:0000}", i % 2 == 0 ? h.Transport : h.Dispatch, h.Vast));
            db.Set<EmployeeJobFunction>().Add(new EmployeeJobFunction { EmployeeId = id, JobFunctionId = i % 3 == 0 ? h.Chauffeur : h.Dispatcher });
            for (var d = 0; d < 5; d++)
            {
                db.Shifts.Add(NewShift(h.TenantId, id, Monday.AddDays(d)));
            }
        }

        await db.SaveChangesAsync();
        h.Counter.Reset();

        var watch = Stopwatch.StartNew();
        var grid = await h.Shifts().GetScheduleAsync(
            Query(departments: [h.Transport, h.Dispatch], employees: [extra[0], extra[1], h.Tom]), CancellationToken.None);
        watch.Stop();

        Assert.Equal(headcount + 4 + 1, grid.Rows.Count); // seeded actives + all extras + pinned inactive Tom
        Assert.Equal(grid.Rows.Count, grid.Rows.Select(r => r.EmployeeId).Distinct().Count());
        // Extras: 5 shifts each; seeded week: Jan ×2, Marc, Els and pinned Tom = 5.
        Assert.Equal(headcount * 5 + 5, grid.Rows.Sum(r => r.Days.Sum(d => d.Entries.Count)));
        // Set-based: employees, scope count, departments, contract types, job functions, shifts,
        // absences, trips, (leave types), settings — never one query per employee.
        Assert.InRange(h.Counter.Count, 1, 12);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"grid for {headcount} took {watch.Elapsed}");
    }
}
