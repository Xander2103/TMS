using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hr;

/// <summary>
/// Corrections wave §5: leave/absence categories are manageable tenant master data — deletable
/// only while unused (exact Dutch refusal otherwise), deactivatable always, never resurrected
/// by the add-if-missing seeding, audited and tenant-isolated.
/// </summary>
public class LeaveCategoryManagementTests
{
    private sealed record Harness(SqliteTestDbContext Db, LeaveBalanceService Service, Guid TenantId);

    private static async Task<Harness> SetupAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var service = new LeaveBalanceService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(Guid.NewGuid())));
        await service.EnsureSeededAsync(CancellationToken.None);
        return new Harness(db, service, tenantId);
    }

    private static SaveLeaveTypeRequest NewLeaveType(string code = "ADR", string name = "ADR-verlof") => new(
        code, name, null, true, true, false, null, AbsenceType.Other,
        true, true, false, false, true, null, 99);

    [Fact]
    public async Task UnusedLeaveType_CanBeDeleted_AndSeedingNeverResurrectsIt()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        // Delete the seeded COMPENSATIE type before anyone used it (spec: irrelevant for some companies).
        var compensatie = await h.Db.Context.LeaveTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "COMPENSATIE");

        Assert.True(await h.Service.DeleteLeaveTypeAsync(compensatie.Id, CancellationToken.None));

        var listed = await h.Service.ListLeaveTypesAsync(false, false, CancellationToken.None);
        Assert.DoesNotContain(listed, t => t.Code == "COMPENSATIE");

        // The lazy add-if-missing seeding must NOT bring a deliberately deleted category back.
        await h.Service.EnsureSeededAsync(CancellationToken.None);
        listed = await h.Service.ListLeaveTypesAsync(false, false, CancellationToken.None);
        Assert.DoesNotContain(listed, t => t.Code == "COMPENSATIE");

        Assert.Contains(await h.Db.Context.AuditLogs.Where(l => l.TenantId == h.TenantId).ToListAsync(),
            l => l.EntityType == "LeaveType" && l.Action == "Deleted");
    }

    [Fact]
    public async Task UsedLeaveType_DeleteIsBlocked_WithTheExactMessage_AndAudited()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var ziekte = await h.Db.Context.LeaveTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "ZIEKTE");
        var employee = new Modules.Employees.Entities.Employee
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeNumber = "MED-0001",
            FirstName = "Jan", LastName = "Janssen", DateOfBirth = new DateOnly(1990, 5, 1),
            Email = "jan@acme.example", PhoneNumber = "0470", Street = "Straat", HouseNumber = "1",
            PostalCode = "1000", City = "Brussel", EmploymentStartDate = new DateOnly(2020, 1, 1),
            EmploymentStatus = Modules.Employees.Entities.EmploymentStatus.Active, IsActive = true,
        };
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employee.Id,
            Type = AbsenceType.Sick, LeaveTypeId = ziekte.Id,
            StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 2),
            Status = AbsenceStatus.Approved,
        });
        await h.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Service.DeleteLeaveTypeAsync(ziekte.Id, CancellationToken.None));
        Assert.Equal(
            $"Categorie '{ziekte.Name}' is al gebruikt en kan niet worden verwijderd. Je kunt de categorie wel deactiveren.",
            ex.Message);

        Assert.Contains(await h.Db.Context.AuditLogs.Where(l => l.TenantId == h.TenantId).ToListAsync(),
            l => l.EntityType == "LeaveType" && l.Action == "DeleteBlocked");

        // Deactivating stays possible; inactive types disappear from new-registration lists but
        // remain readable in the full list (historical data).
        var current = (await h.Service.ListLeaveTypesAsync(false, false, CancellationToken.None)).First(t => t.Id == ziekte.Id);
        await h.Service.SaveLeaveTypeAsync(ziekte.Id, new SaveLeaveTypeRequest(
            current.Code, current.Name, current.Description, false, current.IsPaid, current.DeductsFromBalance,
            current.BalanceTypeId, current.AbsenceType, current.RequiresApproval, current.AllowsHalfDays,
            current.RequiresReason, current.RequiresAttachment, current.VisibleInSelfService, current.Colour,
            current.SortOrder), CancellationToken.None);
        Assert.DoesNotContain(await h.Service.ListLeaveTypesAsync(true, false, CancellationToken.None), t => t.Id == ziekte.Id);
        Assert.Contains(await h.Service.ListLeaveTypesAsync(false, false, CancellationToken.None), t => t.Id == ziekte.Id);
    }

    [Fact]
    public async Task BalanceType_DeleteBlockedWhenReferenced_UnusedDeletes()
    {
        var h = await SetupAsync();
        using var _ = h.Db;

        // WETTELIJK is referenced by seeded leave types → blocked.
        var wettelijk = await h.Db.Context.LeaveBalanceTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "WETTELIJK");
        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Service.DeleteBalanceTypeAsync(wettelijk.Id, CancellationToken.None));
        Assert.Contains("kan niet worden verwijderd", ex.Message);

        // A freshly created, unreferenced balance type deletes fine.
        var created = await h.Service.SaveBalanceTypeAsync(null,
            new SaveLeaveBalanceTypeRequest("EXTRA", "Extra saldo", null, true, 50), CancellationToken.None);
        Assert.True(await h.Service.DeleteBalanceTypeAsync(created.Id, CancellationToken.None));
        Assert.DoesNotContain(await h.Service.ListBalanceTypesAsync(CancellationToken.None), t => t.Id == created.Id);
    }

    [Fact]
    public async Task CategoryManagement_IsTenantIsolated()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var created = await h.Service.SaveLeaveTypeAsync(null, NewLeaveType(), CancellationToken.None);

        var foreignTenant = new DevTenantContext(Guid.NewGuid());
        var foreignService = new LeaveBalanceService(h.Db.Context, foreignTenant,
            new AuditService(h.Db.Context, foreignTenant, new DevCurrentUserContext(null)));

        // The foreign tenant neither sees nor deletes this tenant's category.
        Assert.DoesNotContain(await foreignService.ListLeaveTypesAsync(false, false, CancellationToken.None), t => t.Id == created.Id);
        Assert.False(await foreignService.DeleteLeaveTypeAsync(created.Id, CancellationToken.None));
        Assert.Contains(await h.Service.ListLeaveTypesAsync(false, false, CancellationToken.None), t => t.Id == created.Id);
    }

    [Fact]
    public async Task CreateEditAndSort_RoundTrip()
    {
        var h = await SetupAsync();
        using var _ = h.Db;
        var created = await h.Service.SaveLeaveTypeAsync(null, NewLeaveType(), CancellationToken.None);
        Assert.Equal("ADR", created.Code);
        Assert.Equal(99, created.SortOrder);

        var renamed = await h.Service.SaveLeaveTypeAsync(created.Id,
            NewLeaveType(name: "ADR-opleiding") with { SortOrder = 1, Description = "Verplichte ADR-opfrissing" },
            CancellationToken.None);
        Assert.Equal("ADR-opleiding", renamed.Name);
        Assert.Equal(1, renamed.SortOrder);
        Assert.Equal("Verplichte ADR-opfrissing", renamed.Description);

        // Ordering follows SortOrder in the list endpoints.
        var listed = await h.Service.ListLeaveTypesAsync(false, false, CancellationToken.None);
        Assert.Equal(renamed.Id, listed.First().Id);
    }
}
