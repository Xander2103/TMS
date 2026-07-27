using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Corrections wave §4: the personnel history is a complete, readable audit trail — field-level
/// before/after with Dutch labels, actor and timestamp, child entities included, confidential
/// values masked, no misleading empty "updated" entries, tenant-isolated.
/// </summary>
public class EmployeeHistoryTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, EmployeeService Employees, EmployeeHistoryService History,
        QualificationService Qualifications, LeaveBalanceService LeaveBalances,
        Guid TenantId, Guid UserId, Guid DepartmentId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "MED-", EmployeeNumberNextValue = 1 });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "PLAN", Name = "Planning", IsActive = true });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "ann@acme.example", FirstName = "Ann", LastName = "HR", IsActive = true });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(userId));
        var driverService = new Modules.Drivers.Services.DriverService(db.Context, tenant, audit,
            new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(
            db.Context, tenant, new QualificationStatusCalculator(), TimeProvider.System, audit,
            new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit,
            new CountryCodeValidator(db.Context), driverService, qualifications);
        var history = new EmployeeHistoryService(db.Context, tenant);
        var leaveBalances = new LeaveBalanceService(db.Context, tenant, audit);
        return new Harness(db, employees, history, qualifications, leaveBalances, tenantId, userId, departmentId);
    }

    private static CreateEmployeeRequest CreateRequest(string? notes = null) => new(
        "Jan", "Janssen", new DateOnly(1990, 5, 1),
        "Oude straat", "10", "1000", "Brussel",
        "0470 12 34 56", "jan@acme.example", new DateOnly(2020, 1, 1),
        EmploymentStatus.Active, CountryCode: "BE", Notes: notes);

    private static UpdateEmployeeRequest UpdateRequest(
        string phone = "0470 12 34 56", string street = "Oude straat",
        EmploymentStatus status = EmploymentStatus.Active, string? notes = null,
        string? iban = null) => new(
        "Jan", "Janssen", new DateOnly(1990, 5, 1),
        street, "10", "1000", "Brussel",
        phone, "jan@acme.example", new DateOnly(2020, 1, 1),
        status, CountryCode: "BE", Notes: notes, Iban: iban);

    [Fact]
    public async Task CreateWithNote_ReturnsNoteInDetail_AndHistoryShowsIt()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest("Belangrijke afspraak"), false, CancellationToken.None);

        Assert.Equal("Belangrijke afspraak", created.Notes);
        var detail = await h.Employees.GetByIdAsync(created.Id, false, CancellationToken.None);
        Assert.Equal("Belangrijke afspraak", detail!.Notes);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None);
        var entry = Assert.Single(history!.Items, e => e.Action == "Created");
        Assert.Equal("Profiel", entry.Category);
        Assert.Contains(entry.Changes, c => c.Field == "Notities" && c.Before is null && c.After == "Belangrijke afspraak");
    }

    [Fact]
    public async Task SingleFieldChange_ShowsLabelBeforeAfterActorAndTimestamp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(phone: "0485 98 76 54"), false, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None);
        var entry = history!.Items.First();
        Assert.Equal("Updated", entry.Action);
        Assert.Equal("Gewijzigd", entry.ActionLabel);
        Assert.Equal("Ann HR", entry.UserName);
        Assert.True(entry.Timestamp > DateTime.UtcNow.AddMinutes(-5));
        var change = Assert.Single(entry.Changes);
        Assert.Equal("Telefoonnummer", change.Field);
        Assert.Equal("0470 12 34 56", change.Before);
        Assert.Equal("0485 98 76 54", change.After);
    }

    [Fact]
    public async Task MultipleChanges_InOneSave_GroupIntoOneReadableEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest("Oude notitie"), false, CancellationToken.None);

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(
            street: "Nieuwe straat", status: EmploymentStatus.OnLeave, notes: "Nieuwe notitie"), false, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None);
        var entry = history!.Items.First();
        Assert.Equal(3, entry.Changes.Count);
        Assert.Contains(entry.Changes, c => c.Field == "Straat" && c.Before == "Oude straat" && c.After == "Nieuwe straat");
        Assert.Contains(entry.Changes, c => c.Field == "Status tewerkstelling" && c.Before == "Actief" && c.After == "Met verlof");
        Assert.Contains(entry.Changes, c => c.Field == "Notities" && c.Before == "Oude notitie" && c.After == "Nieuwe notitie");
    }

    [Fact]
    public async Task NoopSave_NeverProducesAMisleadingEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        var before = (await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None))!.TotalCount;

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(), false, CancellationToken.None);

        var after = (await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None))!.TotalCount;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ConfidentialChange_IsVisibleButMasked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), true, CancellationToken.None);

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(iban: "BE68 5390 0754 7034"), true, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None);
        var change = Assert.Single(history!.Items.First().Changes);
        Assert.Equal("IBAN", change.Field);
        Assert.StartsWith("•••", change.After);
        Assert.DoesNotContain("BE68539007547034", change.After);
        Assert.DoesNotContain("5390", change.After!);
    }

    [Fact]
    public async Task QualificationChanges_AppearAsKwalificatiesEntries()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        var type = new QualificationType { Id = Guid.NewGuid(), Code = "ADR", Name = "ADR-attest", IsActive = true };
        h.Db.Context.QualificationTypes.Add(type);
        await h.Db.Context.SaveChangesAsync();

        var qualification = await h.Qualifications.CreateAsync(created.Id, new CreateEmployeeQualificationRequest(
            type.Id, null, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), null, null), CancellationToken.None);
        await h.Qualifications.UpdateAsync(qualification.Id, new UpdateEmployeeQualificationRequest(
            null, new DateOnly(2024, 1, 1), new DateOnly(2027, 1, 1), null, null), CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None);
        var updateEntry = history!.Items.First(e => e.Category == "Kwalificaties" && e.Action == "Updated");
        Assert.Contains(updateEntry.Changes, c => c.Field == "Vervaldatum" && c.Before == "01-01-2026" && c.After == "01-01-2027");
    }

    [Fact]
    public async Task LeaveEntitlement_ShowsCategoryYearBeforeAfterDifferenceAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        await h.LeaveBalances.EnsureSeededAsync(CancellationToken.None);
        var balanceType = await h.Db.Context.LeaveBalanceTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "WETTELIJK");

        await h.LeaveBalances.SetEntitlementAsync(created.Id, 2027, new SetLeaveEntitlementRequest(
            balanceType.Id, 12m, 0m), CancellationToken.None);
        await h.LeaveBalances.SetEntitlementAsync(created.Id, 2027, new SetLeaveEntitlementRequest(
            balanceType.Id, 20m, 0m, Reason: "Jaarlijks saldo 2027 toegekend"), CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None);
        var entry = history!.Items.First(e => e.Category == "Verlofsaldo" && e.Action == "Updated");
        Assert.Equal("Ann HR", entry.UserName);
        Assert.Contains(entry.Changes, c => c.Field == "Basisrecht (dagen)" && c.Before == "12" && c.After == "20");
        Assert.Contains(entry.Changes, c => c.Field == "Verschil" && c.After == "8");
        Assert.Contains(entry.Changes, c => c.Field == "Reden" && c.After == "Jaarlijks saldo 2027 toegekend");
    }

    [Fact]
    public async Task History_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        var foreignHistory = new EmployeeHistoryService(h.Db.Context, new DevTenantContext(Guid.NewGuid()));
        Assert.Null(await foreignHistory.GetHistoryAsync(created.Id, 1, 25, CancellationToken.None));
    }
}
