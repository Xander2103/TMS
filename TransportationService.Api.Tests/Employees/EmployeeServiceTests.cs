using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// HR maturity wave, task 6: server-side sort parameter for the personnel list.
/// <c>SearchAsync</c>'s ordering used to be a fixed LastName/FirstName; these tests cover the
/// full allowed-value set plus the "unknown value falls back to name_asc" contract.
/// </summary>
public class EmployeeServiceTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeService Employees, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(db.Context, tenant, new QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit, new CountryCodeValidator(db.Context), drivers, qualifications,
            new EmployeeCompletenessService(db.Context, tenant));
        return new Harness(db, employees, tenantId);
    }

    private static Employee NewEmployee(
        Guid tenantId, string firstName, string lastName, string employeeNumber,
        Guid? departmentId = null, bool isActive = true, EmploymentStatus status = EmploymentStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeNumber = employeeNumber,
        FirstName = firstName,
        LastName = lastName,
        DepartmentId = departmentId,
        IsActive = isActive,
        EmploymentStatus = status,
    };

    /// <summary>Same technique as <c>EmployeeDossierReminderTests</c>: the production audit
    /// interceptor always stamps CreatedAt with the real insert time and refuses to let a later
    /// Modified save touch it, so raw SQL is the only way to backdate it for the "recent" sort.</summary>
    private static async Task BackdateCreatedAtAsync(Harness h, Guid employeeId, DateTime createdAt) =>
        await h.Db.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Employees SET CreatedAt = {createdAt} WHERE Id = {employeeId}");

    private static async Task<List<string>> LastNamesAsync(Harness h, string? sort)
    {
        var result = await h.Employees.SearchAsync(
            null, null, null, null, null, false, null, sort, PageRequest.Of(1, 25), CancellationToken.None);
        return result.Items.Select(i => $"{i.LastName} {i.FirstName}").ToList();
    }

    [Fact]
    public async Task Sort_NameDesc_OrdersByLastNameThenFirstName_Descending()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var aerts = NewEmployee(h.TenantId, "Bert", "Aerts", "MED-0001");
        var bakkerAnna = NewEmployee(h.TenantId, "Anna", "Bakker", "MED-0002");
        var bakkerChris = NewEmployee(h.TenantId, "Chris", "Bakker", "MED-0003");
        h.Db.Context.Employees.AddRange(aerts, bakkerAnna, bakkerChris);
        await h.Db.Context.SaveChangesAsync();

        var names = await LastNamesAsync(h, "name_desc");

        Assert.Equal(["Bakker Chris", "Bakker Anna", "Aerts Bert"], names);
    }

    [Fact]
    public async Task Sort_Number_OrdersByEmployeeNumber_Ascending()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var zeeman = NewEmployee(h.TenantId, "Piet", "Zeeman", "MED-0003");
        var aerts = NewEmployee(h.TenantId, "Bert", "Aerts", "MED-0001");
        var mertens = NewEmployee(h.TenantId, "Ann", "Mertens", "MED-0002");
        h.Db.Context.Employees.AddRange(zeeman, aerts, mertens);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Employees.SearchAsync(
            null, null, null, null, null, false, null, "number", PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(["MED-0001", "MED-0002", "MED-0003"], result.Items.Select(i => i.EmployeeNumber).ToList());
    }

    [Fact]
    public async Task Sort_Recent_OrdersByCreatedAt_Descending()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var now = DateTime.UtcNow;
        var oldest = NewEmployee(h.TenantId, "Piet", "Oud", "MED-0001");
        var middle = NewEmployee(h.TenantId, "Ann", "Midden", "MED-0002");
        var newest = NewEmployee(h.TenantId, "Bert", "Nieuw", "MED-0003");
        h.Db.Context.Employees.AddRange(oldest, middle, newest);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, oldest.Id, now.AddDays(-5));
        await BackdateCreatedAtAsync(h, middle.Id, now.AddDays(-3));
        await BackdateCreatedAtAsync(h, newest.Id, now.AddDays(-1));

        var names = await LastNamesAsync(h, "recent");

        Assert.Equal(["Nieuw Bert", "Midden Ann", "Oud Piet"], names);
    }

    [Fact]
    public async Task Sort_Department_OrdersByDepartmentName_NullLast()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var planning = new Department { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "PLN", Name = "Planning", IsActive = true };
        var warehouse = new Department { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "WH", Name = "Warehouse", IsActive = true };
        h.Db.Context.Add(planning);
        h.Db.Context.Add(warehouse);
        var withWarehouse = NewEmployee(h.TenantId, "Bert", "Aerts", "MED-0001", warehouse.Id);
        var withPlanning = NewEmployee(h.TenantId, "Ann", "Mertens", "MED-0002", planning.Id);
        var withoutDept = NewEmployee(h.TenantId, "Piet", "Zeeman", "MED-0003", null);
        h.Db.Context.Employees.AddRange(withWarehouse, withPlanning, withoutDept);
        await h.Db.Context.SaveChangesAsync();

        var names = await LastNamesAsync(h, "department");

        Assert.Equal(["Mertens Ann", "Aerts Bert", "Zeeman Piet"], names);
    }

    [Fact]
    public async Task Sort_Function_OrdersBySortOrderFirstFunctionName_NotAlphabeticalMinimum_NullLast()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // SortOrder deliberately disagrees with alphabetical order: "Administratie" sorts first
        // alphabetically but has the HIGHER SortOrder, so the SortOrder-first function for the
        // multi-function employee below is "Chauffeur" — the same definition the list projection
        // (OrderBy SortOrder, ThenBy Name) uses for its FunctionNames column. A key built from
        // Min(Name) would (wrongly) pick "Administratie" instead and reverse the expected order.
        var chauffeur = new JobFunction { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "DRV", Name = "Chauffeur", IsActive = true, SortOrder = 2 };
        var administratie = new JobFunction { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "ADM", Name = "Administratie", IsActive = true, SortOrder = 5 };
        var baliemedewerker = new JobFunction { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "BAL", Name = "Baliemedewerker", IsActive = true, SortOrder = 1 };
        h.Db.Context.Add(chauffeur);
        h.Db.Context.Add(administratie);
        h.Db.Context.Add(baliemedewerker);
        // Multi-function employee: SortOrder-first is Chauffeur (2 < 5); alphabetical-min would
        // wrongly be Administratie (A < C).
        var multi = NewEmployee(h.TenantId, "Werknemer", "Multi", "MED-0001");
        // Single-function employee whose key ("Baliemedewerker") falls alphabetically BETWEEN
        // "Administratie" and "Chauffeur" — this is what flips the expected order depending on
        // which definition of "first function" is used.
        var solo = NewEmployee(h.TenantId, "Werknemer", "Solo", "MED-0002");
        var withoutFunction = NewEmployee(h.TenantId, "Functie", "Geen", "MED-0003");
        h.Db.Context.Employees.AddRange(multi, solo, withoutFunction);
        h.Db.Context.Add(new EmployeeJobFunction { EmployeeId = multi.Id, JobFunctionId = chauffeur.Id });
        h.Db.Context.Add(new EmployeeJobFunction { EmployeeId = multi.Id, JobFunctionId = administratie.Id });
        h.Db.Context.Add(new EmployeeJobFunction { EmployeeId = solo.Id, JobFunctionId = baliemedewerker.Id });
        await h.Db.Context.SaveChangesAsync();

        var names = await LastNamesAsync(h, "function");

        // "Baliemedewerker" (Solo) < "Chauffeur" (Multi, SortOrder-first) < null (Geen, last).
        Assert.Equal(["Solo Werknemer", "Multi Werknemer", "Geen Functie"], names);
    }

    [Fact]
    public async Task Sort_Status_OrdersByIsActiveThenEmploymentStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var activeOnLeave = NewEmployee(h.TenantId, "Ann", "Midden", "MED-0002", status: EmploymentStatus.OnLeave);
        var activeActive = NewEmployee(h.TenantId, "Bert", "Actief", "MED-0001", status: EmploymentStatus.Active);
        var inactiveTerminated = NewEmployee(h.TenantId, "Piet", "Uit", "MED-0003", isActive: false, status: EmploymentStatus.Terminated);
        h.Db.Context.Employees.AddRange(activeOnLeave, activeActive, inactiveTerminated);
        await h.Db.Context.SaveChangesAsync();

        var names = await LastNamesAsync(h, "status");

        Assert.Equal(["Actief Bert", "Midden Ann", "Uit Piet"], names);
    }

    [Fact]
    public async Task Sort_NullOrUnknownValue_FallsBackToNameAscending()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var aerts = NewEmployee(h.TenantId, "Bert", "Aerts", "MED-0001");
        var bakkerAnna = NewEmployee(h.TenantId, "Anna", "Bakker", "MED-0002");
        var bakkerChris = NewEmployee(h.TenantId, "Chris", "Bakker", "MED-0003");
        h.Db.Context.Employees.AddRange(aerts, bakkerAnna, bakkerChris);
        await h.Db.Context.SaveChangesAsync();

        var expected = new[] { "Aerts Bert", "Bakker Anna", "Bakker Chris" };

        Assert.Equal(expected, await LastNamesAsync(h, null));
        Assert.Equal(expected, await LastNamesAsync(h, "not-a-real-sort-value"));
        Assert.Equal(expected, await LastNamesAsync(h, "name_asc"));
    }
}
