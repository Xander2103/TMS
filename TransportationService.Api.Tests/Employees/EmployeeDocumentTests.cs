using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

public class EmployeeDocumentTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeDocumentService Sut, Guid TenantId, Guid EmployeeId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            DateOfBirth = new(1990, 1, 1), Email = "jan@acme.example", PhoneNumber = "+3231112233",
            Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
            EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"));
        var sut = new EmployeeDocumentService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)),
            new LocalFileStorageService(storageRoot));
        return new Harness(db, sut, tenantId, employeeId);
    }

    private static Task<EmployeeDocumentDto?> UploadAsync(Harness h, EmployeeDocumentCategory category, string name = "doc.pdf")
    {
        var stream = new MemoryStream([1, 2, 3]);
        return h.Sut.UploadAsync(h.EmployeeId,
            new SaveEmployeeDocumentMetadata(category, null, null, null),
            name, "application/pdf", 3, stream, CancellationToken.None);
    }

    [Fact]
    public async Task Upload_And_List_MarksSensitiveCategories()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await UploadAsync(h, EmployeeDocumentCategory.IdentityCardFront);
        await UploadAsync(h, EmployeeDocumentCategory.EmploymentDocument);

        var all = await h.Sut.ListAsync(h.EmployeeId, includeSensitive: true, includeArchived: false, CancellationToken.None);

        Assert.Equal(2, all!.Count);
        Assert.True(all.Single(d => d.Category == EmployeeDocumentCategory.IdentityCardFront).IsSensitive);
        Assert.False(all.Single(d => d.Category == EmployeeDocumentCategory.EmploymentDocument).IsSensitive);
    }

    [Fact]
    public async Task List_WithoutSensitiveAccess_HidesSensitiveDocuments()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await UploadAsync(h, EmployeeDocumentCategory.MedicalDocument);
        await UploadAsync(h, EmployeeDocumentCategory.Certificate);

        var visible = await h.Sut.ListAsync(h.EmployeeId, includeSensitive: false, includeArchived: false, CancellationToken.None);

        var single = Assert.Single(visible!);
        Assert.Equal(EmployeeDocumentCategory.Certificate, single.Category);
    }

    [Fact]
    public async Task SensitiveClassification_CoversIdMedicalContract()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.True(h.Sut.IsSensitive(EmployeeDocumentCategory.IdentityCardFront));
        Assert.True(h.Sut.IsSensitive(EmployeeDocumentCategory.IdentityCardBack));
        Assert.True(h.Sut.IsSensitive(EmployeeDocumentCategory.MedicalDocument));
        Assert.True(h.Sut.IsSensitive(EmployeeDocumentCategory.Contract));
        Assert.False(h.Sut.IsSensitive(EmployeeDocumentCategory.DrivingLicenceFront));
        Assert.False(h.Sut.IsSensitive(EmployeeDocumentCategory.Certificate));
    }

    [Fact]
    public async Task Archive_HidesFromDefaultList_ButShownWhenRequested()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var doc = await UploadAsync(h, EmployeeDocumentCategory.Certificate);

        await h.Sut.SetArchivedAsync(h.EmployeeId, doc!.Id, true, CancellationToken.None);

        Assert.Empty((await h.Sut.ListAsync(h.EmployeeId, true, includeArchived: false, CancellationToken.None))!);
        Assert.Single((await h.Sut.ListAsync(h.EmployeeId, true, includeArchived: true, CancellationToken.None))!);
    }

    [Fact]
    public async Task Delete_RemovesDocument_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var doc = await UploadAsync(h, EmployeeDocumentCategory.Certificate);

        Assert.True(await h.Sut.DeleteAsync(h.EmployeeId, doc!.Id, CancellationToken.None));
        Assert.Empty((await h.Sut.ListAsync(h.EmployeeId, true, false, CancellationToken.None))!);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "EmployeeDocument" && a.Action == "Deleted"));
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantCannotList()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var foreign = new EmployeeDocumentService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));

        Assert.Null(await foreign.ListAsync(h.EmployeeId, true, false, CancellationToken.None));
    }
}
