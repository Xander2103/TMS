using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

public class EmployeeProfileTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeService Sut, Guid TenantId, Guid UserId,
        Guid DepartmentId, Guid FunctionChauffeur, Guid FunctionPlanner);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var functionChauffeur = Guid.NewGuid();
        var functionPlanner = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "MED-", EmployeeNumberNextValue = 1 });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "PLAN", Name = "Planning", IsActive = true });
        db.Context.JobFunctions.Add(new JobFunction { Id = functionChauffeur, TenantId = tenantId, Code = "CHAUF", Name = "Chauffeur", IsActive = true });
        db.Context.JobFunctions.Add(new JobFunction { Id = functionPlanner, TenantId = tenantId, Code = "PLAN", Name = "Planner", IsActive = true, SortOrder = 1 });
        db.Context.Nationalities.Add(new Nationality { Id = Guid.NewGuid(), TenantId = tenantId, Code = "BE", Name = "Belg", IsActive = true });
        db.Context.Languages.Add(new Language { Id = Guid.NewGuid(), TenantId = tenantId, Code = "nl", Name = "Nederlands", IsActive = true });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(userId));
        var driverService = new Modules.Drivers.Services.DriverService(db.Context, tenant, audit,
            new Modules.Qualifications.Services.QualificationStatusCalculator(), TimeProvider.System);
        var qualificationService = new Modules.Qualifications.Services.QualificationService(
            db.Context, tenant, new Modules.Qualifications.Services.QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new Modules.Qualifications.Services.LocalFileStorageService(
                Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var sut = new EmployeeService(db.Context, tenant, audit,
            new CountryCodeValidator(db.Context), driverService, qualificationService,
            new EmployeeCompletenessService(db.Context, tenant));
        return new Harness(db, sut, tenantId, userId, departmentId, functionChauffeur, functionPlanner);
    }

    private static CreateEmployeeRequest FullRequest(Harness h) => new(
        "Jan", "Janssen", new DateOnly(1990, 5, 1),
        "Kerkstraat", "1", "1000", "Brussel",
        "+3221234567", "jan@acme.example", new DateOnly(2020, 1, 1),
        EmploymentStatus.Active,
        CountryCode: "be",
        PlaceOfBirth: "Gent",
        NationalityCode: "be",
        PreferredLanguageCode: "NL",
        MobilePhone: "+32478123456",
        DepartmentId: h.DepartmentId,
        JobFunctionIds: [h.FunctionChauffeur, h.FunctionPlanner],
        NationalRegisterNumber: "90.05.01-123.26",
        Iban: "BE68 5390 0754 7034",
        Bic: "KREDBEBB");

    [Fact]
    public async Task Create_PersistsFullProfile_AndNormalizesCodes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Faithful to a real request: the lookup rows are NOT tracked, so navigation fixup
        // cannot silently populate JobFunction names (regression guard for the create response).
        h.Db.Context.ChangeTracker.Clear();

        var created = await h.Sut.CreateAsync(FullRequest(h), canEditConfidential: true, CancellationToken.None);

        Assert.Equal("MED-0001", created.EmployeeNumber);
        Assert.Equal("BE", created.CountryCode);
        Assert.Equal("BE", created.NationalityCode);
        Assert.Equal("nl", created.PreferredLanguageCode);
        Assert.Equal("Planning", created.DepartmentName);
        Assert.Equal(["Chauffeur", "Planner"], created.FunctionNames);
        Assert.Equal("BE68539007547034", created.Iban);
        Assert.Equal("KREDBEBB", created.Bic);
        Assert.Equal("90050112326", created.NationalRegisterNumber);
    }

    [Fact]
    public async Task Detail_WithoutConfidentialAccess_NullsSensitiveFields()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(FullRequest(h), canEditConfidential: true, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var detail = await h.Sut.GetByIdAsync(created.Id, includeConfidential: false, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(detail!.NationalRegisterNumber);
        Assert.Null(detail.Iban);
        Assert.Null(detail.Bic);
        // Non-confidential data still visible.
        Assert.Equal("Jan", detail.FirstName);
    }

    [Fact]
    public async Task Update_WithoutConfidentialAccess_PreservesStoredConfidentialValues()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(FullRequest(h), canEditConfidential: true, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var update = new UpdateEmployeeRequest(
            "Jan", "Janssens", new DateOnly(1990, 5, 1),
            "Kerkstraat", "1", "1000", "Brussel",
            "+3221234567", "jan@acme.example", new DateOnly(2020, 1, 1),
            EmploymentStatus.Active,
            CountryCode: "BE",
            JobFunctionIds: [h.FunctionPlanner],
            Iban: null, Bic: null, NationalRegisterNumber: null);

        var updated = await h.Sut.UpdateAsync(created.Id, update, canEditConfidential: false, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Janssens", updated!.LastName);
        Assert.Equal(["Planner"], updated.FunctionNames);

        var stored = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("BE68539007547034", stored.Iban);
        Assert.Equal("90050112326", stored.NationalRegisterNumber);
    }

    [Fact]
    public async Task Create_InvalidIban_Throws()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = FullRequest(h) with { Iban = "BE00 1111 2222 3333" };

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(request, canEditConfidential: true, CancellationToken.None));
    }

    [Fact]
    public async Task Create_InvalidNationalRegisterNumber_Throws()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = FullRequest(h) with { NationalRegisterNumber = "90.05.01-123.45" };

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(request, canEditConfidential: true, CancellationToken.None));
    }

    [Fact]
    public async Task Create_UnknownNationality_Throws()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var request = FullRequest(h) with { NationalityCode = "XX" };

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(request, canEditConfidential: true, CancellationToken.None));
    }

    [Fact]
    public async Task Create_ForeignJobFunction_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignFunction = Guid.NewGuid();
        h.Db.Context.JobFunctions.Add(new JobFunction { Id = foreignFunction, TenantId = Guid.NewGuid(), Code = "X", Name = "Foreign", IsActive = true });
        await h.Db.Context.SaveChangesAsync();

        var request = FullRequest(h) with { JobFunctionIds = new[] { foreignFunction } };

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => h.Sut.CreateAsync(request, canEditConfidential: true, CancellationToken.None));
    }

    [Fact]
    public async Task Search_FiltersByJobFunctionAndDepartment()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(FullRequest(h), canEditConfidential: true, CancellationToken.None);
        await h.Sut.CreateAsync(new CreateEmployeeRequest(
            "Piet", "Peeters", new DateOnly(1985, 1, 1),
            "Dorpstraat", "2", "2000", "Antwerpen",
            "+323", "piet@acme.example", new DateOnly(2021, 1, 1),
            EmploymentStatus.Active, JobFunctionIds: [h.FunctionPlanner]),
            canEditConfidential: true, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var chauffeurs = await h.Sut.SearchAsync(null, null, h.FunctionChauffeur, null, null, false, null, PageRequest.Of(1, 25), CancellationToken.None);
        var planningDept = await h.Sut.SearchAsync(null, null, null, h.DepartmentId, null, false, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Single(chauffeurs.Items);
        Assert.Equal("Janssen", chauffeurs.Items[0].LastName);
        Assert.Single(planningDept.Items);
    }

    [Fact]
    public async Task Create_StampsAuditColumns_ViaInterceptor()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(FullRequest(h), canEditConfidential: true, CancellationToken.None);

        var stored = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.NotEqual(default, stored.CreatedAt);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task Create_WithDriverProfile_CreatesBothAtomically()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var categoryId = Guid.NewGuid();
        h.Db.Context.DriverCategories.Add(new Modules.Drivers.Entities.DriverCategory
        {
            Id = categoryId, TenantId = h.TenantId, Code = "NAT", Name = "Nationaal", IsActive = true,
        });
        var settings = await h.Db.Context.TenantSettings.SingleAsync(s => s.TenantId == h.TenantId);
        settings.DriverNumberPrefix = "CH-";
        settings.DriverNumberNextValue = 1;
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var request = FullRequest(h) with
        {
            DriverProfile = new CreateEmployeeDriverProfile(categoryId, "Vaste nachtritten"),
        };

        var created = await h.Sut.CreateAsync(request, canEditConfidential: true, CancellationToken.None);

        Assert.NotNull(created.DriverId);
        var driver = await h.Db.Context.Drivers.AsNoTracking().SingleAsync(d => d.Id == created.DriverId);
        Assert.Equal(created.Id, driver.EmployeeId);
        Assert.Equal("CH-0001", driver.DriverNumber);
    }

    [Fact]
    public async Task Create_WithInvalidDriverCategory_RollsBackEmployee()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.ChangeTracker.Clear();

        var request = FullRequest(h) with
        {
            DriverProfile = new CreateEmployeeDriverProfile(Guid.NewGuid(), null),
        };

        await Assert.ThrowsAsync<InvalidTenantReferenceException>(
            () => h.Sut.CreateAsync(request, canEditConfidential: true, CancellationToken.None));

        h.Db.Context.ChangeTracker.Clear();
        Assert.Equal(0, await h.Db.Context.Employees.CountAsync(e => e.TenantId == h.TenantId));
        Assert.Equal(0, await h.Db.Context.Drivers.CountAsync(d => d.TenantId == h.TenantId));
    }

    [Fact]
    public async Task Search_ExcludeDrivers_HidesLinkedEmployees()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.ChangeTracker.Clear();
        var withDriver = await h.Sut.CreateAsync(FullRequest(h) with
        {
            DriverProfile = new CreateEmployeeDriverProfile(null, null),
        }, canEditConfidential: true, CancellationToken.None);
        await h.Sut.CreateAsync(new CreateEmployeeRequest(
            "Piet", "Peeters", new DateOnly(1985, 1, 1),
            "Dorpstraat", "2", "2000", "Antwerpen",
            "+323", "piet2@acme.example", new DateOnly(2021, 1, 1),
            EmploymentStatus.Active), canEditConfidential: true, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var withoutDrivers = await h.Sut.SearchAsync(null, null, null, null, null, true, null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Single(withoutDrivers.Items);
        Assert.DoesNotContain(withoutDrivers.Items, e => e.Id == withDriver.Id);
    }

    [Fact]
    public async Task Search_HasDriverProfile_ReturnsOnlyDriversWithReadinessColumns()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.ChangeTracker.Clear();
        var withDriver = await h.Sut.CreateAsync(FullRequest(h) with
        {
            DriverProfile = new CreateEmployeeDriverProfile(null, null),
        }, canEditConfidential: true, CancellationToken.None);
        await h.Sut.CreateAsync(new CreateEmployeeRequest(
            "Piet", "Peeters", new DateOnly(1985, 1, 1),
            "Dorpstraat", "2", "2000", "Antwerpen",
            "+323", "piet2@acme.example", new DateOnly(2021, 1, 1),
            EmploymentStatus.Active), canEditConfidential: true, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var onlyDrivers = await h.Sut.SearchAsync(null, null, null, null, null, false, true, PageRequest.Of(1, 25), CancellationToken.None);

        var row = Assert.Single(onlyDrivers.Items);
        Assert.Equal(withDriver.Id, row.Id);
        Assert.True(row.IsDriver);
        Assert.False(row.DriverIsBlocked);
        Assert.NotNull(row.DriverAvailability);
    }

    [Fact]
    public async Task DeactivateAndReactivate_ManageLifecycle()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(FullRequest(h), canEditConfidential: true, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        Assert.True(await h.Sut.DeactivateAsync(created.Id, CancellationToken.None));
        var afterDeactivate = await h.Sut.GetByIdAsync(created.Id, true, CancellationToken.None);
        Assert.False(afterDeactivate!.IsActive);
        Assert.Equal(EmploymentStatus.Terminated, afterDeactivate.EmploymentStatus);
        Assert.NotNull(afterDeactivate.EmploymentEndDate);

        h.Db.Context.ChangeTracker.Clear();
        Assert.True(await h.Sut.ReactivateAsync(created.Id, CancellationToken.None));
        var afterReactivate = await h.Sut.GetByIdAsync(created.Id, true, CancellationToken.None);
        Assert.True(afterReactivate!.IsActive);
        Assert.Equal(EmploymentStatus.Active, afterReactivate.EmploymentStatus);
        Assert.Null(afterReactivate.EmploymentEndDate);
    }

    [Fact]
    public async Task CreateAsync_WithQualifications_PersistsThemInTheSameTransaction()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var typeId = Guid.NewGuid();
        h.Db.Context.QualificationTypes.Add(new Modules.Qualifications.Entities.QualificationType
        {
            Id = typeId, Code = "adr", Name = "ADR", Category = "Certificaat", RequiresExpiryDate = true, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var created = await h.Sut.CreateAsync(FullRequest(h) with
        {
            Qualifications =
            [
                new Modules.Qualifications.Dtos.CreateEmployeeQualificationRequest(
                    typeId, "DOC-1", new DateOnly(2024, 1, 1), new DateOnly(2029, 1, 1), null),
            ],
        }, canEditConfidential: true, CancellationToken.None);

        var stored = Assert.Single(h.Db.Context.EmployeeQualifications.Where(q => q.EmployeeId == created.Id));
        Assert.Equal(typeId, stored.QualificationTypeId);
        Assert.Equal("DOC-1", stored.DocumentNumber);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownQualificationType_FailsWithFieldError_AndCreatesNoEmployee()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var ex = await Assert.ThrowsAsync<TransportationService.Api.Common.DomainValidationException>(
            () => h.Sut.CreateAsync(FullRequest(h) with
            {
                Qualifications =
                [
                    new Modules.Qualifications.Dtos.CreateEmployeeQualificationRequest(
                        Guid.NewGuid(), null, new DateOnly(2024, 1, 1), null, null),
                ],
            }, canEditConfidential: true, CancellationToken.None));

        Assert.Contains("qualifications", ex.FieldErrors!.Keys);
        Assert.Empty(h.Db.Context.Employees.ToList());
        Assert.Empty(h.Db.Context.EmployeeQualifications.ToList());
    }
}
