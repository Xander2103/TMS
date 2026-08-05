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

    [Fact]
    public async Task Driver_UpdateCategories_ReorderAndReAddAfterRemove_RoundTripsWithPrimaryMirror()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = await h.Drivers.CreateAsync(new CreateDriverRequest(
            await NewEmployeeIdAsync(h), null, DriverAvailabilityStatus.Available,
            DriverCategoryIds: [h.CategoryB, h.CategoryC]), CancellationToken.None);
        var driverId = create.Driver!.Id;

        // Remove B, keep C, add CE — order counts: C becomes primary.
        var second = await h.Drivers.UpdateAsync(driverId, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: [h.CategoryC, h.CategoryCe]), CancellationToken.None);
        Assert.Equal([h.CategoryC, h.CategoryCe], second.Driver!.CategoryIds);
        Assert.Equal(h.CategoryC, second.Driver.CategoryId);

        // Re-add previously removed B (revives the soft-deleted join row — the unique index
        // on (TenantId, DriverId, DriverCategoryId) is unfiltered) and reorder: B is primary.
        var third = await h.Drivers.UpdateAsync(driverId, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: [h.CategoryB, h.CategoryCe, h.CategoryC]), CancellationToken.None);
        Assert.Equal([h.CategoryB, h.CategoryCe, h.CategoryC], third.Driver!.CategoryIds);
        Assert.Equal(h.CategoryB, third.Driver.CategoryId);
        Assert.Equal(["Categorie B", "Categorie CE", "Categorie C"], third.Driver.CategoryNames);

        // No duplicate physical rows: one row per category, including soft-deleted history.
        var rawRows = await h.Db.Context.Set<DriverDriverCategory>().IgnoreQueryFilters()
            .Where(c => c.DriverId == driverId).ToListAsync();
        Assert.Equal(3, rawRows.Count);
        Assert.All(rawRows, r => Assert.False(r.IsDeleted));
    }

    [Fact]
    public async Task Driver_UpdateCategories_NullList_LeavesCategoriesUnchanged()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = await h.Drivers.CreateAsync(new CreateDriverRequest(
            await NewEmployeeIdAsync(h), null, DriverAvailabilityStatus.Available,
            DriverCategoryIds: [h.CategoryB, h.CategoryC]), CancellationToken.None);

        // Neither the multi-list nor the legacy single id is supplied: categories stay as-is.
        var updated = await h.Drivers.UpdateAsync(create.Driver!.Id, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Unavailable, true), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.Success, updated.Outcome);
        Assert.Equal([h.CategoryB, h.CategoryC], updated.Driver!.CategoryIds);
        Assert.Equal(h.CategoryB, updated.Driver.CategoryId);
        Assert.Equal(DriverAvailabilityStatus.Unavailable, updated.Driver.AvailabilityStatus);
    }

    [Fact]
    public async Task Driver_UpdateCategories_EmptyList_ClearsAll_AndPrimaryBecomesNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = await h.Drivers.CreateAsync(new CreateDriverRequest(
            await NewEmployeeIdAsync(h), null, DriverAvailabilityStatus.Available,
            DriverCategoryIds: [h.CategoryB, h.CategoryC]), CancellationToken.None);

        var updated = await h.Drivers.UpdateAsync(create.Driver!.Id, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: []), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.Success, updated.Outcome);
        Assert.Empty(updated.Driver!.CategoryIds!);
        Assert.Null(updated.Driver.CategoryId);
    }

    [Fact]
    public async Task Driver_UpdateCategories_ForeignTenantCategory_IsRejected_AndNothingChanges()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignCategory = new DriverCategory
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Code = "VR", Name = "Vreemd", IsActive = true,
        };
        h.Db.Context.DriverCategories.Add(foreignCategory);
        await h.Db.Context.SaveChangesAsync();
        var create = await h.Drivers.CreateAsync(new CreateDriverRequest(
            await NewEmployeeIdAsync(h), null, DriverAvailabilityStatus.Available,
            DriverCategoryIds: [h.CategoryB]), CancellationToken.None);

        var bad = await h.Drivers.UpdateAsync(create.Driver!.Id, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: [foreignCategory.Id]), CancellationToken.None);

        Assert.Equal(DriverOperationOutcome.InvalidReference, bad.Outcome);
        var unchanged = await h.Drivers.GetByIdAsync(create.Driver.Id, CancellationToken.None);
        Assert.Equal([h.CategoryB], unchanged!.CategoryIds);
    }

    [Fact]
    public async Task Driver_UpdateCategories_AuditsResolvedReadableNames()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var create = await h.Drivers.CreateAsync(new CreateDriverRequest(
            await NewEmployeeIdAsync(h), null, DriverAvailabilityStatus.Available,
            DriverCategoryIds: [h.CategoryB]), CancellationToken.None);

        await h.Drivers.UpdateAsync(create.Driver!.Id, new UpdateDriverRequest(
            null, DriverAvailabilityStatus.Available, true, DriverCategoryIds: [h.CategoryC, h.CategoryB]), CancellationToken.None);

        var entry = await h.Db.Context.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "Driver" && l.EntityId == create.Driver.Id.ToString() && l.Action == "Updated")
            .OrderByDescending(l => l.Timestamp).FirstAsync();
        Assert.Contains("Categorie B", entry.OldValuesJson!);
        Assert.Contains("Categorie C, Categorie B", entry.NewValuesJson!);
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
