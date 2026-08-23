using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.EmployeePlanning.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// HR-liveoverzicht (statusprioriteit, afwezigen, gepland-niet-ingepunt-anomalie,
/// vergeten-uitpunt-grens, filters), dagrapport met planning-vergelijking en
/// formule-injectieveilige XLSX-export.
/// </summary>
public class AttendanceOverviewAndReportTests
{
    // 20/08/2026 10:00 lokale tijd (Europe/Brussels, UTC+2).
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 8, 20);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, TestClock Clock)
    {
        public Guid Jan { get; init; }
        public Guid Sarah { get; init; }
        public Guid Tom { get; init; }
        public Guid Els { get; init; }
        public Guid DepartmentId { get; init; }

        public AttendanceOverviewService Overview() =>
            new(Db.Context, new DevTenantContext(TenantId), Clock);

        public AttendanceReportService Report() =>
            new(Db.Context, new DevTenantContext(TenantId), Clock);

        public AttendanceService Attendance() => new(Db.Context, new DevTenantContext(TenantId), Clock);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        Guid jan = Guid.NewGuid(), sarah = Guid.NewGuid(), tom = Guid.NewGuid(), els = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, Timezone = "Europe/Brussels" });
        db.Context.Departments.Add(new TransportationService.Api.Modules.Organization.Entities.Department
        {
            Id = departmentId, TenantId = tenantId, Name = "Magazijn",
        });
        db.Context.Employees.AddRange(
            new Employee { Id = jan, TenantId = tenantId, EmployeeNumber = "E-1", FirstName = "Jan", LastName = "Peeters", IsActive = true, DepartmentId = departmentId },
            new Employee { Id = sarah, TenantId = tenantId, EmployeeNumber = "E-2", FirstName = "Sarah", LastName = "Janssens", IsActive = true },
            new Employee { Id = tom, TenantId = tenantId, EmployeeNumber = "E-3", FirstName = "Tom", LastName = "Willems", IsActive = true },
            new Employee { Id = els, TenantId = tenantId, EmployeeNumber = "E-4", FirstName = "Els", LastName = "Vermeer", IsActive = true });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, new TestClock(Now)) { Jan = jan, Sarah = sarah, Tom = tom, Els = els, DepartmentId = departmentId };
    }

    private static AttendancePunchContext Web => new(AttendanceSource.Web);

    [Fact]
    public async Task Overview_ShowsStatusPerEmployee_WithSummaryCounts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var attendance = h.Attendance();

        // Jan werkt sinds 07:52 lokale tijd; Sarah is in pauze; Tom niet ingepunt;
        // Els heeft goedgekeurd verlof.
        h.Clock.Advance(TimeSpan.FromMinutes(-128)); // 05:52 UTC
        await attendance.ClockInAsync(h.Jan, Web, CancellationToken.None);
        await attendance.ClockInAsync(h.Sarah, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(120));
        await attendance.StartBreakAsync(h.Sarah, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(8)); // terug op Now

        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Els,
            Type = AbsenceType.Vacation, StartDate = Today, EndDate = Today,
            Status = AbsenceStatus.Approved,
        });
        await h.Db.Context.SaveChangesAsync();

        var overview = await h.Overview().GetOverviewAsync(null, null, null, CancellationToken.None);

        Assert.Equal(Today, overview.Date);
        var jan = overview.Rows.Single(r => r.EmployeeId == h.Jan);
        Assert.Equal(AttendanceOverviewStatus.Working, jan.Status);
        Assert.Equal("Magazijn", jan.DepartmentName);
        Assert.Equal(128, jan.WorkedMinutes);

        var sarah = overview.Rows.Single(r => r.EmployeeId == h.Sarah);
        Assert.Equal(AttendanceOverviewStatus.OnBreak, sarah.Status);
        Assert.Equal(8, sarah.BreakMinutes);

        Assert.Equal(AttendanceOverviewStatus.NotClockedIn, overview.Rows.Single(r => r.EmployeeId == h.Tom).Status);

        var els = overview.Rows.Single(r => r.EmployeeId == h.Els);
        Assert.Equal(AttendanceOverviewStatus.Absent, els.Status);
        Assert.Equal("Verlof", els.AbsenceLabel);

        Assert.Equal(1, overview.Summary.Working);
        Assert.Equal(1, overview.Summary.OnBreak);
        Assert.Equal(1, overview.Summary.NotClockedIn);
        Assert.Equal(1, overview.Summary.Absent);
    }

    [Fact]
    public async Task Overview_FlagsForgottenClockOut_AndPlannedNotClockedIn()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var attendance = h.Attendance();

        // Jan puntte gisteren om 07:52 in en nooit uit (> 16 u geleden).
        h.Clock.Advance(TimeSpan.FromHours(-26));
        await attendance.ClockInAsync(h.Jan, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(26));

        // Tom is vandaag gepland om 08:00 lokale tijd (grace 30 min ruim voorbij om 10:00).
        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Tom,
            Date = Today, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            BreakMinutes = 30, Type = ShiftType.Work, Status = ShiftStatus.Planned,
        });
        await h.Db.Context.SaveChangesAsync();

        var overview = await h.Overview().GetOverviewAsync(null, null, null, CancellationToken.None);

        Assert.Equal(AttendanceOverviewStatus.ForgottenClockOut, overview.Rows.Single(r => r.EmployeeId == h.Jan).Status);
        var tom = overview.Rows.Single(r => r.EmployeeId == h.Tom);
        Assert.True(tom.PlannedNotClockedIn);
        Assert.Equal(450, tom.PlannedMinutes);
        Assert.Equal(1, overview.Summary.ForgottenClockOut);
        Assert.Equal(1, overview.Summary.PlannedNotClockedIn);
    }

    [Fact]
    public async Task Overview_Filters_ByDepartmentAndSearch()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var byDepartment = await h.Overview().GetOverviewAsync(null, h.DepartmentId, null, CancellationToken.None);
        Assert.Single(byDepartment.Rows);
        Assert.Equal(h.Jan, byDepartment.Rows[0].EmployeeId);

        var bySearch = await h.Overview().GetOverviewAsync(null, null, "janss", CancellationToken.None);
        Assert.Single(bySearch.Rows);
        Assert.Equal(h.Sarah, bySearch.Rows[0].EmployeeId);
    }

    [Fact]
    public async Task Report_ComparesPlannedAndActual_AndFlagsMissingClockOut()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var attendance = h.Attendance();

        // Gisteren: Jan 06:00–14:30 UTC met 30 min pauze; gepland 08:00–16:30 lokaal (-30 pauze = 480).
        var yesterday = Today.AddDays(-1);
        h.Clock.Advance(TimeSpan.FromHours(-26)); // 19/08 06:00 UTC
        await attendance.ClockInAsync(h.Jan, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(4));
        await attendance.StartBreakAsync(h.Jan, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await attendance.EndBreakAsync(h.Jan, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(4));
        await attendance.ClockOutAsync(h.Jan, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(1050)); // terug naar Now

        // Sarah puntte gisteren in en vergat uit te punten.
        h.Clock.Advance(TimeSpan.FromHours(-20));
        await attendance.ClockInAsync(h.Sarah, Web, CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(20));

        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Jan,
            Date = yesterday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 30),
            BreakMinutes = 30, Type = ShiftType.Work, Status = ShiftStatus.Confirmed,
        });
        await h.Db.Context.SaveChangesAsync();

        var report = await h.Report().BuildAsync(yesterday, Today, null, null, CancellationToken.None);

        var janDay = report.Rows.Single(r => r.EmployeeId == h.Jan && r.Date == yesterday);
        Assert.Equal(510, janDay.GrossMinutes);
        Assert.Equal(30, janDay.BreakMinutes);
        Assert.Equal(480, janDay.NetMinutes);
        Assert.Equal(480, janDay.PlannedMinutes);
        Assert.Equal(0, janDay.DeviationMinutes);
        Assert.False(janDay.MissingClockOut);

        Assert.True(report.Rows.Where(r => r.EmployeeId == h.Sarah).All(r => r.MissingClockOut));
        Assert.True(report.TotalNetMinutes > 0);
    }

    [Fact]
    public async Task Report_ShowsPlannedDayWithoutRegistration_AsZeroActual()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Tom,
            Date = Today.AddDays(-1), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            BreakMinutes = 0, Type = ShiftType.Work, Status = ShiftStatus.Planned,
        });
        await h.Db.Context.SaveChangesAsync();

        var report = await h.Report().BuildAsync(Today.AddDays(-1), Today.AddDays(-1), null, null, CancellationToken.None);

        var row = report.Rows.Single(r => r.EmployeeId == h.Tom);
        Assert.Equal(0, row.NetMinutes);
        Assert.Equal(480, row.PlannedMinutes);
        Assert.Equal(-480, row.DeviationMinutes);
    }

    [Fact]
    public async Task Export_WritesFormulaLookalikesAsText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        // Naam die op een formule lijkt — mag nooit als formule in de cel belanden.
        var evil = h.Db.Context.Employees.Single(e => e.Id == h.Tom);
        evil.FirstName = "=HYPERLINK(\"http://evil.example\",\"klik\")";
        h.Db.Context.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.Tom,
            Date = Today, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0),
            BreakMinutes = 0, Type = ShiftType.Work, Status = ShiftStatus.Planned,
        });
        await h.Db.Context.SaveChangesAsync();

        var export = new AttendanceExportService(h.Report(), new DevCurrentUserContext(null), h.Clock, h.Db.Context);
        var (content, fileName) = await export.BuildAsync(Today, Today, null, null, CancellationToken.None);

        Assert.StartsWith("urenregistratie-", fileName)
        ;
        // FR-gebruiker krijgt Franse koppen — zelfde kolomvolgorde/data (§66).
        var frUser = new TransportationService.Api.Modules.Identity.Entities.User
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Email = "fr@acme.test",
            FirstName = "Luc", LastName = "Martin", IsActive = true, PreferredLanguageCode = "fr",
        };
        h.Db.Context.Users.Add(frUser);
        await h.Db.Context.SaveChangesAsync();
        var frExport = new AttendanceExportService(h.Report(), new DevCurrentUserContext(frUser.Id), h.Clock, h.Db.Context);
        var (frContent, _) = await frExport.BuildAsync(Today, Today, null, null, CancellationToken.None);
        using var frWorkbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(frContent));
        var frSheet = frWorkbook.Worksheet("Enregistrement des heures");
        Assert.Equal("Collaborateur", frSheet.Cell(1, 1).GetString());
        Assert.Equal("Écart (min)", frSheet.Cell(1, 9).GetString());
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(content));
        var sheet = workbook.Worksheet("Urenregistratie");
        var nameCell = sheet.Cell(2, 1);
        Assert.False(nameCell.HasFormula);
        Assert.Contains("HYPERLINK", nameCell.GetString());
        Assert.NotNull(workbook.Worksheet("Criteria"));
    }
}
