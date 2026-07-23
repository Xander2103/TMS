using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Hr;

public class LeaveBalanceServiceTests
{
    private const int Year = 2027;

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, LeaveBalanceService Service);

    private static Employee NewEmployee(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = "MED-" + Guid.NewGuid().ToString("N")[..4],
        FirstName = "Jan", LastName = "Janssen", DateOfBirth = new DateOnly(1990, 5, 1),
        Email = "jan@acme.example", PhoneNumber = "+3231112233",
        Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
        EmploymentStartDate = new DateOnly(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
    };

    private static async Task<Harness> SetupAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var emp = NewEmployee(tenantId);
        db.Context.Employees.Add(emp);
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var service = new LeaveBalanceService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid())));
        await service.EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, tenantId, emp.Id, service);
    }

    private static async Task<Guid> BalanceTypeIdAsync(Harness h, string code) =>
        (await h.Db.Context.LeaveBalanceTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == code)).Id;

    private static async Task AddAbsenceAsync(Harness h, string leaveTypeCode, AbsenceStatus status, DateOnly start, DateOnly end, AbsencePartDay part = AbsencePartDay.FullDay)
    {
        var lt = await h.Db.Context.LeaveTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == leaveTypeCode);
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId, Type = lt.AbsenceType,
            LeaveTypeId = lt.Id, StartDate = start, EndDate = end, PartDay = part, Status = status,
        });
        await h.Db.Context.SaveChangesAsync();
    }

    private static LeaveBalanceRowDto Row(EmployeeLeaveBalanceDto dto, string code) => dto.Rows.First(r => r.BalanceTypeCode == code);

    [Fact]
    public async Task StatutoryDefault_Is20_WhenNoEntitlementRow()
    {
        var h = await SetupAsync();
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(20m, Row(dto, "WETTELIJK").BaseEntitlementDays);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
        Assert.Equal(0m, Row(dto, "ADV").BaseEntitlementDays);
    }

    [Fact]
    public async Task SetEntitlement_Then_Get_ReflectsBaseAndCarryOver()
    {
        var h = await SetupAsync();
        await h.Service.SetEntitlementAsync(h.EmployeeId, Year, new SetLeaveEntitlementRequest(await BalanceTypeIdAsync(h, "WETTELIJK"), 25m, 2m), CancellationToken.None);
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(25m, Row(dto, "WETTELIJK").BaseEntitlementDays);
        Assert.Equal(2m, Row(dto, "WETTELIJK").CarryOverDays);
        Assert.Equal(27m, Row(dto, "WETTELIJK").RemainingDays);
    }

    [Fact]
    public async Task Adjustments_AddAndDeduct_WithHalfDays()
    {
        var h = await SetupAsync();
        var wettelijk = await BalanceTypeIdAsync(h, "WETTELIJK");
        await h.Service.AddAdjustmentAsync(h.EmployeeId, Year, new AddLeaveAdjustmentRequest(wettelijk, 2m, "Anciënniteit", LeaveAdjustmentKind.Seniority), CancellationToken.None);
        await h.Service.AddAdjustmentAsync(h.EmployeeId, Year, new AddLeaveAdjustmentRequest(wettelijk, -0.5m, "Correctie", LeaveAdjustmentKind.Correction), CancellationToken.None);
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(1.5m, Row(dto, "WETTELIJK").ManualAdjustmentDays);
        Assert.Equal(21.5m, Row(dto, "WETTELIJK").RemainingDays);
    }

    [Fact]
    public async Task ApprovedVacation_Reduces_StatutoryBalance()
    {
        var h = await SetupAsync();
        await AddAbsenceAsync(h, "WETTELIJK", AbsenceStatus.Approved, new DateOnly(Year, 3, 1), new DateOnly(Year, 3, 5)); // 5 days
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(5m, Row(dto, "WETTELIJK").ApprovedUsedDays);
        Assert.Equal(15m, Row(dto, "WETTELIJK").RemainingDays);
    }

    [Fact]
    public async Task Adv_Reduces_AdvBalance_NotStatutory()
    {
        var h = await SetupAsync();
        await h.Service.SetEntitlementAsync(h.EmployeeId, Year, new SetLeaveEntitlementRequest(await BalanceTypeIdAsync(h, "ADV"), 6m, 0m), CancellationToken.None);
        await AddAbsenceAsync(h, "ADV", AbsenceStatus.Approved, new DateOnly(Year, 4, 1), new DateOnly(Year, 4, 2)); // 2 days
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
        Assert.Equal(2m, Row(dto, "ADV").ApprovedUsedDays);
        Assert.Equal(4m, Row(dto, "ADV").RemainingDays);
    }

    [Fact]
    public async Task Sickness_DoesNotReduce_AnyBalance()
    {
        var h = await SetupAsync();
        await AddAbsenceAsync(h, "ZIEKTE", AbsenceStatus.Approved, new DateOnly(Year, 5, 1), new DateOnly(Year, 5, 3));
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
        Assert.Equal(0m, Row(dto, "WETTELIJK").ApprovedUsedDays);
    }

    [Fact]
    public async Task Unpaid_DoesNotReduce_StatutoryLeave()
    {
        var h = await SetupAsync();
        await AddAbsenceAsync(h, "ONBETAALD", AbsenceStatus.Approved, new DateOnly(Year, 6, 1), new DateOnly(Year, 6, 10));
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
    }

    [Fact]
    public async Task RejectedAndCancelled_DoNotReduce()
    {
        var h = await SetupAsync();
        await AddAbsenceAsync(h, "WETTELIJK", AbsenceStatus.Rejected, new DateOnly(Year, 3, 1), new DateOnly(Year, 3, 5));
        await AddAbsenceAsync(h, "WETTELIJK", AbsenceStatus.Cancelled, new DateOnly(Year, 4, 1), new DateOnly(Year, 4, 5));
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
        Assert.Equal(0m, Row(dto, "WETTELIJK").ApprovedUsedDays);
    }

    [Fact]
    public async Task PendingLeave_ReservesBalance_ByDefault()
    {
        var h = await SetupAsync();
        await AddAbsenceAsync(h, "WETTELIJK", AbsenceStatus.Requested, new DateOnly(Year, 7, 1), new DateOnly(Year, 7, 3)); // 3 days pending
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(3m, Row(dto, "WETTELIJK").PendingReservedDays);
        Assert.Equal(17m, Row(dto, "WETTELIJK").RemainingDays);
    }

    [Fact]
    public async Task PendingLeave_DoesNotReserve_WhenSettingDisabled()
    {
        var h = await SetupAsync();
        h.Db.Context.LeaveEntitlementSettings.Add(new LeaveEntitlementSettings { Id = Guid.NewGuid(), TenantId = h.TenantId, PendingReservesBalance = false });
        await h.Db.Context.SaveChangesAsync();
        await AddAbsenceAsync(h, "WETTELIJK", AbsenceStatus.Requested, new DateOnly(Year, 7, 1), new DateOnly(Year, 7, 3));
        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(3m, Row(dto, "WETTELIJK").PendingReservedDays);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
    }

    [Fact]
    public async Task CheckRequest_Blocks_WhenInsufficient_AndAllows_NonDeducting()
    {
        var h = await SetupAsync();
        await AddAbsenceAsync(h, "WETTELIJK", AbsenceStatus.Approved, new DateOnly(Year, 2, 1), new DateOnly(Year, 2, 18)); // 18 days used -> remaining 2
        var wettelijk = await h.Db.Context.LeaveTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "WETTELIJK");
        var blocked = await h.Service.CheckRequestAsync(h.EmployeeId, wettelijk.Id, new DateOnly(Year, 8, 1), new DateOnly(Year, 8, 5), AbsencePartDay.FullDay, CancellationToken.None);
        Assert.False(blocked.Allowed);

        var ziekte = await h.Db.Context.LeaveTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "ZIEKTE");
        var allowed = await h.Service.CheckRequestAsync(h.EmployeeId, ziekte.Id, new DateOnly(Year, 8, 1), new DateOnly(Year, 8, 30), AbsencePartDay.FullDay, CancellationToken.None);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task Usage_IsTenantIsolated()
    {
        var h = await SetupAsync();
        // A second tenant + employee with an approved vacation must not affect this tenant's balance.
        var otherTenant = Guid.NewGuid();
        var otherEmp = NewEmployee(otherTenant);
        h.Db.Context.Employees.Add(otherEmp);
        await h.Db.Context.SaveChangesAsync();
        var otherTenantCtx = new DevTenantContext(otherTenant);
        var otherService = new LeaveBalanceService(h.Db.Context, otherTenantCtx, new AuditService(h.Db.Context, otherTenantCtx, new DevCurrentUserContext(Guid.NewGuid())));
        await otherService.EnsureSeededAsync(CancellationToken.None);
        var otherLt = await h.Db.Context.LeaveTypes.FirstAsync(t => t.TenantId == otherTenant && t.Code == "WETTELIJK");
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = otherTenant, EmployeeId = otherEmp.Id, Type = AbsenceType.Vacation,
            LeaveTypeId = otherLt.Id, StartDate = new DateOnly(Year, 3, 1), EndDate = new DateOnly(Year, 3, 20), Status = AbsenceStatus.Approved,
        });
        await h.Db.Context.SaveChangesAsync();

        var dto = await h.Service.GetForEmployeeAsync(h.EmployeeId, Year, CancellationToken.None);
        Assert.Equal(20m, Row(dto, "WETTELIJK").RemainingDays);
    }
}
