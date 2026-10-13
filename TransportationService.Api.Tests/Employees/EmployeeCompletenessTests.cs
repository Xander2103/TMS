using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// HR maturity wave, task 2: the declarative dossier-completeness engine. Catalogue codes,
/// labels and sections come from spec §2.1 verbatim (see docs/superpowers/specs/2026-08-06-hr-
/// maturity-wave-design.md). Twelve requirements apply to every employee; a thirteenth
/// (driving_licence_document) applies only when the employee has a linked Driver profile.
/// </summary>
public class EmployeeCompletenessTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeCompletenessService Sut, Guid TenantId, Guid JobFunctionId);

    private static async Task<Harness> SeedAsync(Guid? tenantId = null)
    {
        var db = new SqliteTestDbContext();
        var tenant = tenantId ?? Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenant, Name = "Acme", Slug = $"acme-{tenant:N}", IsActive = true, CreatedAt = DateTime.UtcNow });
        var jobFunction = new JobFunction { Id = Guid.NewGuid(), TenantId = tenant, Code = "DRV", Name = "Chauffeur", IsActive = true };
        db.Context.JobFunctions.Add(jobFunction);
        await db.Context.SaveChangesAsync();

        var tenantContext = new DevTenantContext(tenant);
        var sut = new EmployeeCompletenessService(db.Context, tenantContext);
        return new Harness(db, sut, tenant, jobFunction.Id);
    }

    private static Employee MinimalEmployee(Guid tenantId, string firstName = "Ann", string lastName = "Peeters") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeNumber = $"MED-{Guid.NewGuid():N}"[..12],
        FirstName = firstName,
        LastName = lastName,
        IsActive = true,
    };

    /// <summary>Fills every non-document, non-emergency-contact field so only documents/contact
    /// requirements remain to be added by the caller.</summary>
    private static void FillScalarFields(Employee employee, Guid jobFunctionId)
    {
        employee.DateOfBirth = new DateOnly(1990, 1, 1);
        employee.NationalRegisterNumber = "90010112345";
        employee.Street = "Kerkstraat";
        employee.PostalCode = "2000";
        employee.City = "Antwerpen";
        employee.Email = "ann@acme.example";
        employee.Iban = "BE68539007547034";
        employee.EmploymentStartDate = new DateOnly(2020, 1, 1);
        employee.ContractTypeId = Guid.NewGuid();
        employee.DepartmentId = Guid.NewGuid();
        employee.JobFunctions.Add(new EmployeeJobFunction { EmployeeId = employee.Id, JobFunctionId = jobFunctionId });
    }

    private static EmployeeDocument Document(Guid tenantId, Guid employeeId, EmployeeDocumentCategory category, bool archived = false) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        Category = category,
        FileName = "doc.pdf",
        StorageKey = "k",
        IsArchived = archived,
    };

    private static EmployeeEmergencyContact EmergencyContact(Guid tenantId, Guid employeeId, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = employeeId,
        Name = "Piet Peeters",
        Priority = 1,
        IsDeleted = deleted,
    };

    [Fact]
    public async Task GetForEmployeeAsync_EmptyDossier_ReturnsLowPercentage_AndAllTwelveItemsMissing()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = MinimalEmployee(h.TenantId);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(employee.Id, CancellationToken.None);

        Assert.Equal(0, result.Percentage);
        Assert.False(result.IsComplete);
        Assert.Equal(12, result.MissingItems.Count);
        Assert.DoesNotContain(result.MissingItems, i => i.Code == "driving_licence_document");
        Assert.Contains(result.MissingItems, i => i.Code == "date_of_birth" && i.Label == "Geboortedatum" && i.Section == "algemeen");
        Assert.Contains(result.MissingItems, i => i.Code == "national_register_number" && i.Label == "Rijksregisternummer" && i.Section == "hr");
        Assert.Contains(result.MissingItems, i => i.Code == "address" && i.Label == "Adres" && i.Section == "algemeen");
        Assert.Contains(result.MissingItems, i => i.Code == "contact" && i.Label == "E-mail of telefoon" && i.Section == "algemeen");
        Assert.Contains(result.MissingItems, i => i.Code == "iban" && i.Label == "IBAN" && i.Section == "hr");
        Assert.Contains(result.MissingItems, i => i.Code == "employment_start" && i.Label == "Startdatum" && i.Section == "dienstverband");
        Assert.Contains(result.MissingItems, i => i.Code == "contract_type" && i.Label == "Contracttype" && i.Section == "dienstverband");
        Assert.Contains(result.MissingItems, i => i.Code == "department" && i.Label == "Afdeling" && i.Section == "dienstverband");
        Assert.Contains(result.MissingItems, i => i.Code == "job_function" && i.Label == "Functie" && i.Section == "dienstverband");
        Assert.Contains(result.MissingItems, i => i.Code == "emergency_contact" && i.Label == "Noodcontact" && i.Section == "noodcontacten");
        Assert.Contains(result.MissingItems, i => i.Code == "identity_document" && i.Label == "Identiteitsdocument" && i.Section == "documenten");
        Assert.Contains(result.MissingItems, i => i.Code == "contract_document" && i.Label == "Contractdocument" && i.Section == "documenten");
    }

    [Fact]
    public async Task GetForEmployeeAsync_FullDossier_NonDriver_Returns100AndIsComplete()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = MinimalEmployee(h.TenantId);
        FillScalarFields(employee, h.JobFunctionId);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Add(EmergencyContact(h.TenantId, employee.Id));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.IdentityCardFront));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.Contract));
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(employee.Id, CancellationToken.None);

        Assert.Equal(100, result.Percentage);
        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingItems);
    }

    [Fact]
    public async Task GetForEmployeeAsync_DriverWithoutLicenceDocument_AddsMissingDrivingLicenceItem()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = MinimalEmployee(h.TenantId);
        FillScalarFields(employee, h.JobFunctionId);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Add(EmergencyContact(h.TenantId, employee.Id));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.IdentityCardFront));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.Contract));
        h.Db.Context.Add(new Driver
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employee.Id,
            DriverNumber = "CH-0001", AvailabilityStatus = DriverAvailabilityStatus.Available, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(employee.Id, CancellationToken.None);

        Assert.False(result.IsComplete);
        var missing = Assert.Single(result.MissingItems);
        Assert.Equal("driving_licence_document", missing.Code);
        Assert.Equal("Rijbewijsdocument", missing.Label);
        Assert.Equal("documenten", missing.Section);
        // 12 of 13 applicable requirements satisfied -> round(100*12/13) = 92.
        Assert.Equal(92, result.Percentage);
    }

    [Fact]
    public async Task GetForEmployeeAsync_DriverWithLicenceDocument_IsComplete()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = MinimalEmployee(h.TenantId);
        FillScalarFields(employee, h.JobFunctionId);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Add(EmergencyContact(h.TenantId, employee.Id));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.IdentityCardFront));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.Contract));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.DrivingLicenceFront));
        h.Db.Context.Add(new Driver
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employee.Id,
            DriverNumber = "CH-0001", AvailabilityStatus = DriverAvailabilityStatus.Available, IsActive = true,
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(employee.Id, CancellationToken.None);

        Assert.Equal(100, result.Percentage);
        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingItems);
    }

    [Fact]
    public async Task GetForEmployeeAsync_ArchivedDocument_DoesNotCountTowardRequirement()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = MinimalEmployee(h.TenantId);
        FillScalarFields(employee, h.JobFunctionId);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Add(EmergencyContact(h.TenantId, employee.Id));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.IdentityCardFront, archived: true));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.Contract));
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(employee.Id, CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Contains(result.MissingItems, i => i.Code == "identity_document");
    }

    [Fact]
    public async Task GetForEmployeeAsync_SoftDeletedEmergencyContact_DoesNotCountTowardRequirement()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var employee = MinimalEmployee(h.TenantId);
        FillScalarFields(employee, h.JobFunctionId);
        h.Db.Context.Employees.Add(employee);
        h.Db.Context.Add(EmergencyContact(h.TenantId, employee.Id, deleted: true));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.IdentityCardFront));
        h.Db.Context.Add(Document(h.TenantId, employee.Id, EmployeeDocumentCategory.Contract));
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(employee.Id, CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Contains(result.MissingItems, i => i.Code == "emergency_contact");
    }

    [Fact]
    public async Task GetPercentagesAsync_BatchedForThreeEmployees_OtherTenantInvisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var empty = MinimalEmployee(h.TenantId, "Empty", "Dossier");
        var full = MinimalEmployee(h.TenantId, "Full", "Dossier");
        FillScalarFields(full, h.JobFunctionId);
        var partial = MinimalEmployee(h.TenantId, "Partial", "Dossier");
        partial.DateOfBirth = new DateOnly(1985, 3, 3);

        h.Db.Context.Employees.AddRange(empty, full, partial);
        h.Db.Context.Add(EmergencyContact(h.TenantId, full.Id));
        h.Db.Context.Add(Document(h.TenantId, full.Id, EmployeeDocumentCategory.IdentityCardFront));
        h.Db.Context.Add(Document(h.TenantId, full.Id, EmployeeDocumentCategory.Contract));

        // Other tenant, deliberately excluded from the ids we query so the "other tenant
        // invisible" assertion also holds even if a caller passed its id by mistake.
        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = DateTime.UtcNow });
        var foreignEmployee = MinimalEmployee(otherTenant, "Foreign", "Person");
        h.Db.Context.Employees.Add(foreignEmployee);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetPercentagesAsync(
            [empty.Id, full.Id, partial.Id, foreignEmployee.Id], CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(0, result[empty.Id]);
        Assert.Equal(100, result[full.Id]);
        Assert.True(result[partial.Id] > 0 && result[partial.Id] < 100);
        Assert.False(result.ContainsKey(foreignEmployee.Id));
    }

    [Fact]
    public async Task FindIncompleteEmployeeIdsAsync_ReturnsOnlyActiveIncompleteEmployees()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var completeActive = MinimalEmployee(h.TenantId, "Complete", "Active");
        FillScalarFields(completeActive, h.JobFunctionId);
        var incompleteActive = MinimalEmployee(h.TenantId, "Incomplete", "Active");
        var incompleteInactive = MinimalEmployee(h.TenantId, "Incomplete", "Inactive");
        incompleteInactive.IsActive = false;

        h.Db.Context.Employees.AddRange(completeActive, incompleteActive, incompleteInactive);
        h.Db.Context.Add(EmergencyContact(h.TenantId, completeActive.Id));
        h.Db.Context.Add(Document(h.TenantId, completeActive.Id, EmployeeDocumentCategory.IdentityCardFront));
        h.Db.Context.Add(Document(h.TenantId, completeActive.Id, EmployeeDocumentCategory.Contract));
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.FindIncompleteEmployeeIdsAsync(CancellationToken.None);

        Assert.Contains(incompleteActive.Id, result);
        Assert.DoesNotContain(completeActive.Id, result);
        Assert.DoesNotContain(incompleteInactive.Id, result);
    }
}
