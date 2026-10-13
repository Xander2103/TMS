using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Hr;

public class LeaveModelTests
{
    private static Employee NewEmployee(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = "MED-" + Guid.NewGuid().ToString("N")[..4],
        FirstName = "Jan", LastName = "Janssen", DateOfBirth = new DateOnly(1990, 5, 1),
        Email = "jan@acme.example", PhoneNumber = "+3231112233",
        Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
        EmploymentStartDate = new DateOnly(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
    };

    [Fact]
    public async Task LeaveBalance_RoundTrips_WithDecimalDays()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId);
        var balanceType = new LeaveBalanceType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "WETTELIJK", Name = "Wettelijk verlof" };
        db.Context.Employees.Add(employee);
        db.Context.LeaveBalanceTypes.Add(balanceType);
        db.Context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, CalendarYear = 2027,
            BalanceTypeId = balanceType.Id, BaseEntitlementDays = 20m, CarryOverDays = 0.5m,
        });
        await db.Context.SaveChangesAsync();

        var loaded = await db.Context.EmployeeLeaveBalances.SingleAsync();
        Assert.Equal(20m, loaded.BaseEntitlementDays);
        Assert.Equal(0.5m, loaded.CarryOverDays);
    }

    [Fact]
    public async Task EmployeeLeaveBalance_IsUnique_PerEmployeeYearBalanceType()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId);
        var balanceType = new LeaveBalanceType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "ADV", Name = "ADV" };
        db.Context.Employees.Add(employee);
        db.Context.LeaveBalanceTypes.Add(balanceType);
        db.Context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, CalendarYear = 2027,
            BalanceTypeId = balanceType.Id, BaseEntitlementDays = 6m,
        });
        await db.Context.SaveChangesAsync();

        db.Context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, CalendarYear = 2027,
            BalanceTypeId = balanceType.Id, BaseEntitlementDays = 6m,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Absence_Persists_LeaveTypeId()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId);
        var leaveType = new LeaveType { Id = Guid.NewGuid(), TenantId = tenantId, Code = "WETTELIJK", Name = "Wettelijk verlof", DeductsFromBalance = false, AbsenceType = AbsenceType.Vacation };
        db.Context.Employees.Add(employee);
        db.Context.LeaveTypes.Add(leaveType);
        db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, Type = AbsenceType.Vacation,
            LeaveTypeId = leaveType.Id, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 1),
            Status = AbsenceStatus.Approved,
        });
        await db.Context.SaveChangesAsync();

        var loaded = await db.Context.Absences.SingleAsync();
        Assert.Equal(leaveType.Id, loaded.LeaveTypeId);
    }
}
