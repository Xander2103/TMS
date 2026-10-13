using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    /// <summary>
    /// B07: the leave settings page fires its three reads in parallel, so on a fresh tenant two
    /// requests seed the defaults at the same time. The unique (tenant, code) index rejects the
    /// loser's insert; that request must continue with the winner's rows instead of failing
    /// with a 500 — in both seeding rounds (balance types, then leave types).
    /// </summary>
    [Fact]
    public async Task EnsureSeeded_ConcurrentSeedingOfTheSameTenant_TakesTheWinnersRows()
    {
        var competitor = new CompetingSeedInterceptor();
        using var db = new SqliteTestDbContext(null, competitor);
        var tenantId = Guid.NewGuid();
        competitor.Configure(db, tenantId);
        var svc = Service(db, tenantId);

        await svc.EnsureSeededAsync(CancellationToken.None);

        Assert.Equal(2, competitor.Inserts);
        var balanceCodes = (await svc.ListBalanceTypesAsync(CancellationToken.None)).Select(t => t.Code).ToList();
        var leaveCodes = (await svc.ListLeaveTypesAsync(false, false, CancellationToken.None)).Select(t => t.Code).ToList();
        Assert.Equal(LeaveDefaults.BalanceTypes.Select(s => s.Code).Order(), balanceCodes.Order());
        Assert.Equal(LeaveDefaults.LeaveTypes.Select(s => s.Code).Order(), leaveCodes.Order());
    }

    /// <summary>Simulates another request of the same tenant winning both seeding rounds: right
    /// before the first insert of balance types (and later of leave types) commits, the same
    /// codes are written through a second context.</summary>
    private sealed class CompetingSeedInterceptor : SaveChangesInterceptor
    {
        private SqliteTestDbContext? _db;
        private Guid _tenantId;
        private bool _busy;
        public int Inserts { get; private set; }

        public void Configure(SqliteTestDbContext db, Guid tenantId)
        {
            _db = db;
            _tenantId = tenantId;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_db is null || _busy || eventData.Context is not { } context)
            {
                return result;
            }

            _busy = true; // the second context shares this interceptor; never compete with ourselves
            try
            {
                await using var other = _db.CreateContextForTenant(_tenantId);
                if (context.ChangeTracker.Entries<LeaveBalanceType>().Any(e => e.State == EntityState.Added)
                    && !await other.LeaveBalanceTypes.AnyAsync(cancellationToken))
                {
                    Inserts++;
                    other.LeaveBalanceTypes.AddRange(LeaveDefaults.BalanceTypes.Select(s => new LeaveBalanceType
                    {
                        Id = Guid.NewGuid(), TenantId = _tenantId, Code = s.Code, Name = s.Name, SortOrder = s.SortOrder,
                    }));
                    await other.SaveChangesAsync(cancellationToken);
                }
                else if (context.ChangeTracker.Entries<LeaveType>().Any(e => e.State == EntityState.Added)
                    && !await other.LeaveTypes.AnyAsync(cancellationToken))
                {
                    Inserts++;
                    var balanceByCode = await other.LeaveBalanceTypes.ToDictionaryAsync(t => t.Code, t => t.Id, cancellationToken);
                    other.LeaveTypes.AddRange(LeaveDefaults.LeaveTypes.Select(s => new LeaveType
                    {
                        Id = Guid.NewGuid(), TenantId = _tenantId, Code = s.Code, Name = s.Name,
                        AbsenceType = s.AbsenceType, DeductsFromBalance = s.DeductsFromBalance,
                        BalanceTypeId = s.BalanceCode is null ? null : balanceByCode.GetValueOrDefault(s.BalanceCode),
                        IsPaid = s.IsPaid, AllowsHalfDays = s.AllowsHalfDays, RequiresReason = s.RequiresReason,
                        RequiresAttachment = s.RequiresAttachment, VisibleInSelfService = s.VisibleInSelfService,
                        Colour = s.Colour, SortOrder = s.SortOrder,
                    }));
                    await other.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                _busy = false;
            }

            return result;
        }
    }
}
