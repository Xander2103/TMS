using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
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

public class LeaveConfigAndGuardTests
{
    private static Employee NewEmployee(Guid tenantId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumber = "MED-" + Guid.NewGuid().ToString("N")[..4],
        FirstName = "Jan", LastName = "Janssen", DateOfBirth = new DateOnly(1990, 5, 1),
        Email = "jan@acme.example", PhoneNumber = "+3231112233",
        Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
        EmploymentStartDate = new DateOnly(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
    };

    private static LeaveBalanceService Service(SqliteTestDbContext db, Guid tenantId)
    {
        var tenant = new DevTenantContext(tenantId);
        return new LeaveBalanceService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid())));
    }

    [Fact]
    public async Task ListLeaveTypes_SelfServiceFilter_HidesNonSelfServiceTypes()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var svc = Service(db, tenantId);
        await svc.EnsureSeededAsync(CancellationToken.None);

        var all = await svc.ListLeaveTypesAsync(activeOnly: true, selfServiceOnly: false, CancellationToken.None);
        var selfService = await svc.ListLeaveTypesAsync(activeOnly: true, selfServiceOnly: true, CancellationToken.None);

        Assert.Contains(all, t => t.Code == "ANDERE"); // "Andere" is not self-service
        Assert.DoesNotContain(selfService, t => t.Code == "ANDERE");
        Assert.Contains(selfService, t => t.Code == "WETTELIJK");
    }

    [Fact]
    public async Task InactiveLeaveType_IsExcluded_FromActiveList()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var svc = Service(db, tenantId);
        await svc.EnsureSeededAsync(CancellationToken.None);
        var adv = await db.Context.LeaveTypes.FirstAsync(t => t.TenantId == tenantId && t.Code == "ADV");
        await svc.SaveLeaveTypeAsync(adv.Id, new SaveLeaveTypeRequest(
            adv.Code, adv.Name, adv.Description, IsActive: false, adv.IsPaid, adv.DeductsFromBalance, adv.BalanceTypeId,
            adv.AbsenceType, adv.RequiresApproval, adv.AllowsHalfDays, adv.RequiresReason, adv.RequiresAttachment,
            adv.VisibleInSelfService, adv.Colour, adv.SortOrder), CancellationToken.None);

        var active = await svc.ListLeaveTypesAsync(activeOnly: true, selfServiceOnly: false, CancellationToken.None);
        Assert.DoesNotContain(active, t => t.Code == "ADV");
    }

    [Fact]
    public async Task SaveLeaveType_Deducting_WithoutBalanceType_IsRejected()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var svc = Service(db, tenantId);
        await svc.EnsureSeededAsync(CancellationToken.None);

        await Assert.ThrowsAsync<DomainValidationException>(() => svc.SaveLeaveTypeAsync(null, new SaveLeaveTypeRequest(
            "NEW", "Nieuw", null, IsActive: true, IsPaid: true, DeductsFromBalance: true, BalanceTypeId: null,
            AbsenceType.Vacation, RequiresApproval: true, AllowsHalfDays: true, RequiresReason: false, RequiresAttachment: false,
            VisibleInSelfService: true, Colour: null, SortOrder: 99), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSettings_TogglesNegativeAndPending()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var svc = Service(db, tenantId);

        var updated = await svc.UpdateSettingsAsync(new LeaveSettingsDto(25m, PendingReservesBalance: false, AllowNegativeBalance: true, CarryOverEnabled: true, MaxCarryOverDays: 5m), CancellationToken.None);
        Assert.Equal(25m, updated.DefaultAnnualEntitlementDays);
        Assert.False(updated.PendingReservesBalance);
        Assert.True(updated.AllowNegativeBalance);

        var reloaded = await svc.GetSettingsAsync(CancellationToken.None);
        Assert.True(reloaded.AllowNegativeBalance);
    }

    [Fact]
    public async Task CheckRequest_RespectsNegativeAllowedSetting()
    {
        using var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var emp = NewEmployee(tenantId);
        db.Context.Employees.Add(emp);
        await db.Context.SaveChangesAsync();
        var svc = Service(db, tenantId);
        await svc.EnsureSeededAsync(CancellationToken.None);
        var wettelijk = await db.Context.LeaveTypes.FirstAsync(t => t.TenantId == tenantId && t.Code == "WETTELIJK");

        // Default (negatives blocked): a 25-day request against 20 remaining is blocked.
        var blocked = await svc.CheckRequestAsync(emp.Id, wettelijk.Id, new DateOnly(2027, 1, 6), new DateOnly(2027, 1, 30), AbsencePartDay.FullDay, CancellationToken.None);
        Assert.False(blocked.Allowed);

        // With negatives allowed, the same request is permitted.
        await svc.UpdateSettingsAsync(new LeaveSettingsDto(20m, true, AllowNegativeBalance: true, true, null), CancellationToken.None);
        var allowed = await svc.CheckRequestAsync(emp.Id, wettelijk.Id, new DateOnly(2027, 1, 6), new DateOnly(2027, 1, 30), AbsencePartDay.FullDay, CancellationToken.None);
        Assert.True(allowed.Allowed);
    }
}
