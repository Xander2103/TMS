using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// HR maturity wave, task 5: the contract type governs whether an employment end date is
/// mandatory (bepaalde duur / uitzendkracht / student contracts require one; open-ended
/// contracts do not).
/// </summary>
public class EmployeeContractTypeTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "MED-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
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
        return new Harness(db, sut, tenantId);
    }

    private static async Task<Guid> AddContractTypeAsync(SqliteTestDbContext db, Guid tenantId, string code, bool requiresEndDate)
    {
        var contractType = new ContractType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = code,
            IsActive = true,
            SortOrder = 0,
            RequiresEndDate = requiresEndDate,
        };
        db.Context.Set<ContractType>().Add(contractType);
        await db.Context.SaveChangesAsync();
        return contractType.Id;
    }

    private static CreateEmployeeRequest MinimalRequest(Guid? contractTypeId, DateOnly? employmentEndDate) => new(
        "Ann", "Peeters", null, null, null, null, null, null, null, null, EmploymentStatus.Active,
        ContractTypeId: contractTypeId, EmploymentEndDate: employmentEndDate);

    private static UpdateEmployeeRequest MinimalUpdate(Guid? contractTypeId, DateOnly? employmentEndDate) => new(
        "Ann", "Peeters", null, null, null, null, null, null, null, null, EmploymentStatus.Active,
        EmploymentEndDate: employmentEndDate, ContractTypeId: contractTypeId);

    [Fact]
    public async Task Create_WithRequiresEndDateContractType_AndNoEndDate_FailsWithFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var bepId = await AddContractTypeAsync(h.Db, h.TenantId, "BEP", requiresEndDate: true);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(MinimalRequest(bepId, null), canEditConfidential: false, CancellationToken.None));

        Assert.Contains("employmentEndDate", ex.FieldErrors!.Keys);
        Assert.Equal("Einddatum is verplicht voor dit contracttype.", ex.Message);
    }

    [Fact]
    public async Task Create_WithRequiresEndDateContractType_AndEndDate_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var bepId = await AddContractTypeAsync(h.Db, h.TenantId, "BEP", requiresEndDate: true);

        var created = await h.Sut.CreateAsync(
            MinimalRequest(bepId, new DateOnly(2026, 12, 31)), canEditConfidential: false, CancellationToken.None);

        Assert.Equal(bepId, created.ContractTypeId);
        Assert.Equal(new DateOnly(2026, 12, 31), created.EmploymentEndDate);
    }

    [Fact]
    public async Task Create_WithContractTypeThatDoesNotRequireEndDate_AndNoEndDate_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vastId = await AddContractTypeAsync(h.Db, h.TenantId, "VAST", requiresEndDate: false);

        var created = await h.Sut.CreateAsync(MinimalRequest(vastId, null), canEditConfidential: false, CancellationToken.None);

        Assert.Equal(vastId, created.ContractTypeId);
        Assert.Null(created.EmploymentEndDate);
    }

    [Fact]
    public async Task Create_WithoutContractType_AndNoEndDate_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(MinimalRequest(null, null), canEditConfidential: false, CancellationToken.None);

        Assert.Null(created.ContractTypeId);
        Assert.Null(created.EmploymentEndDate);
    }

    [Fact]
    public async Task Update_SwitchingToRequiresEndDateContractType_WithoutEndDate_FailsWithFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vastId = await AddContractTypeAsync(h.Db, h.TenantId, "VAST", requiresEndDate: false);
        var uitzId = await AddContractTypeAsync(h.Db, h.TenantId, "UITZ", requiresEndDate: true);

        var created = await h.Sut.CreateAsync(MinimalRequest(vastId, null), canEditConfidential: false, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpdateAsync(created.Id, MinimalUpdate(uitzId, null), canEditConfidential: false, CancellationToken.None));

        Assert.Contains("employmentEndDate", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Update_SwitchingToRequiresEndDateContractType_WithEndDate_Succeeds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var vastId = await AddContractTypeAsync(h.Db, h.TenantId, "VAST", requiresEndDate: false);
        var uitzId = await AddContractTypeAsync(h.Db, h.TenantId, "UITZ", requiresEndDate: true);

        var created = await h.Sut.CreateAsync(MinimalRequest(vastId, null), canEditConfidential: false, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Id,
            MinimalUpdate(uitzId, new DateOnly(2027, 1, 1)), canEditConfidential: false, CancellationToken.None);

        Assert.Equal(uitzId, updated!.ContractTypeId);
        Assert.Equal(new DateOnly(2027, 1, 1), updated.EmploymentEndDate);
    }
}
