using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Dtos;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>Business-feedback wave: HR fields, multiple emergency contacts, multi driver categories.</summary>
public class EmployeeHrAndDriverCategoryTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeService Employees, DriverService Drivers,
        Guid TenantId, Guid CategoryB, Guid CategoryC, Guid CategoryCe);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "MED-", DriverNumberPrefix = "CH-" });
        var catB = new DriverCategory { Id = Guid.NewGuid(), TenantId = tenantId, Code = "B", Name = "Categorie B", IsActive = true };
        var catC = new DriverCategory { Id = Guid.NewGuid(), TenantId = tenantId, Code = "C", Name = "Categorie C", IsActive = true, SortOrder = 1 };
        var catCe = new DriverCategory { Id = Guid.NewGuid(), TenantId = tenantId, Code = "CE", Name = "Categorie CE", IsActive = true, SortOrder = 2 };
        db.Context.DriverCategories.AddRange(catB, catC, catCe);
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(db.Context, tenant, new QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit, new CountryCodeValidator(db.Context), drivers, qualifications);
        return new Harness(db, employees, drivers, tenantId, catB.Id, catC.Id, catCe.Id);
    }

    private static CreateEmployeeRequest Request() => new(
        "Jan", "Janssen", new DateOnly(1990, 5, 1),
        "Kerkstraat", "1", "1000", "Brussel", "+3221234567", "jan@acme.example",
        new DateOnly(2020, 1, 1), EmploymentStatus.Active, CountryCode: "be");

    [Fact]
    public async Task Create_WithHrFields_PersistsAndValidatesDependents()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Employees.CreateAsync(Request() with
        {
            CivilStatus = CivilStatus.Married,
            DependentChildren = 2,
            DimonaNumber = "DIM-123",
            IdentityCardNumber = "592-1234567-89",
        }, canEditConfidential: true, CancellationToken.None);

        Assert.Equal(CivilStatus.Married, created.CivilStatus);
        Assert.Equal(2, created.DependentChildren);
        Assert.Equal("DIM-123", created.DimonaNumber);
        Assert.Equal("592-1234567-89", created.IdentityCardNumber);
    }

    [Fact]
    public async Task Create_InvalidDependentChildren_IsRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            h.Employees.CreateAsync(Request() with { DependentChildren = 99 }, canEditConfidential: true, CancellationToken.None));
    }

    [Fact]
    public async Task IdentityCardNumber_IsConfidential_RedactedWithoutPermission()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(Request() with { IdentityCardNumber = "592-1234567-89" },
            canEditConfidential: true, CancellationToken.None);

        var redacted = await h.Employees.GetByIdAsync(created.Id, includeConfidential: false, CancellationToken.None);
        Assert.Null(redacted!.IdentityCardNumber);

        var full = await h.Employees.GetByIdAsync(created.Id, includeConfidential: true, CancellationToken.None);
        Assert.Equal("592-1234567-89", full!.IdentityCardNumber);
    }

    [Fact]
    public async Task EmergencyContacts_MultipleOrderedByPriority_AndSyncLegacyPair()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(Request(), canEditConfidential: false, CancellationToken.None);

        var updated = await h.Employees.UpdateAsync(created.Id, Update(created) with
        {
            EmergencyContacts =
            [
                new EmployeeEmergencyContactInput(null, "Piet Peeters", "Broer", "+3231112222", null, null, 2),
                new EmployeeEmergencyContactInput(null, "Anna Janssen", "Echtgenote", "+3233334444", "+32470111222", "Eerste contact", 1),
            ],
        }, canEditConfidential: false, CancellationToken.None);

        Assert.Equal(2, updated!.EmergencyContacts!.Count);
        Assert.Equal("Anna Janssen", updated.EmergencyContacts![0].Name); // priority 1 first
        Assert.Equal("Piet Peeters", updated.EmergencyContacts[1].Name);

        // Legacy single pair mirrors the priority-1 contact.
        var stored = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("Anna Janssen", stored.EmergencyContactName);
        Assert.Equal("+3233334444", stored.EmergencyContactPhone);
    }

    [Fact]
    public async Task EmergencyContacts_Update_RemovesDroppedRows()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(Request(), canEditConfidential: false, CancellationToken.None);
        var first = await h.Employees.UpdateAsync(created.Id, Update(created) with
        {
            EmergencyContacts =
            [
                new EmployeeEmergencyContactInput(null, "A", null, null, null, null, 1),
                new EmployeeEmergencyContactInput(null, "B", null, null, null, null, 2),
            ],
        }, canEditConfidential: false, CancellationToken.None);
        var keepId = first!.EmergencyContacts!.Single(c => c.Name == "A").Id;

        var second = await h.Employees.UpdateAsync(created.Id, Update(created) with
        {
            EmergencyContacts = [new EmployeeEmergencyContactInput(keepId, "A", null, null, null, null, 1)],
        }, canEditConfidential: false, CancellationToken.None);

        Assert.Single(second!.EmergencyContacts!);
        Assert.Equal(1, await h.Db.Context.EmployeeEmergencyContacts.CountAsync(c => c.EmployeeId == created.Id && !c.IsDeleted));
    }

    [Fact]
    public async Task DriverProfile_MultipleCategories_PrimaryMirrorsFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(Request() with
        {
            DriverProfile = new CreateEmployeeDriverProfile(null, null, [h.CategoryCe, h.CategoryC]),
        }, canEditConfidential: false, CancellationToken.None);

        var driver = await h.Drivers.GetByIdAsync(created.DriverId!.Value, CancellationToken.None);
        Assert.Equal([h.CategoryCe, h.CategoryC], driver!.CategoryIds);
        Assert.Equal(h.CategoryCe, driver.CategoryId); // primary = first selected
    }

    [Fact]
    public async Task Driver_UpdateCategories_ReplacesSet_AndUnknownCategoryRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = await h.Drivers.CreateAsync(new CreateDriverRequest(
            await NewEmployeeIdAsync(h), h.CategoryB, DriverAvailabilityStatus.Available), CancellationToken.None);
        var driverId = create.Driver!.Id;

        var updated = await h.Drivers.UpdateAsync(driverId, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: [h.CategoryB, h.CategoryCe]), CancellationToken.None);
        Assert.Equal([h.CategoryB, h.CategoryCe], updated.Driver!.CategoryIds);

        var bad = await h.Drivers.UpdateAsync(driverId, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: [Guid.NewGuid()]), CancellationToken.None);
        Assert.Equal(DriverOperationOutcome.InvalidReference, bad.Outcome);
    }

    private async Task<Guid> NewEmployeeIdAsync(Harness h)
    {
        var created = await h.Employees.CreateAsync(Request() with { Email = $"{Guid.NewGuid():N}@acme.example" },
            canEditConfidential: false, CancellationToken.None);
        return created.Id;
    }

    private static UpdateEmployeeRequest Update(EmployeeDetailDto e) => new(
        e.FirstName, e.LastName, e.DateOfBirth, e.Street, e.HouseNumber, e.PostalCode, e.City,
        e.PhoneNumber, e.Email, e.EmploymentStartDate, e.EmploymentStatus, CountryCode: "be");
}
