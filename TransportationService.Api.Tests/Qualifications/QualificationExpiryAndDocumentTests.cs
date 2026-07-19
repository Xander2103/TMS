using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common.Models;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Drivers.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Qualifications;

public class QualificationExpiryAndDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    private sealed record Harness(
        SqliteTestDbContext Db, QualificationService Qualifications, DriverService Drivers,
        Guid TenantId, Guid EmployeeId, Guid DriverId, Guid TypeCode95Id, string StorageRoot);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var typeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, QualificationExpiryWarningDays = 30 });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen",
            Street = "A", HouseNumber = "1", PostalCode = "1000", City = "Brussel",
            PhoneNumber = "+32", Email = "jan@acme.example", DateOfBirth = new DateOnly(1990, 1, 1),
            EmploymentStartDate = new DateOnly(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = employeeId, IsActive = true });
        db.Context.QualificationTypes.Add(new QualificationType
        {
            Id = typeId, Code = "Code95", Name = "Code 95", Category = "Certificaat", RequiresExpiryDate = true, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-qual-tests", Guid.NewGuid().ToString("N"));
        var qualifications = new QualificationService(db.Context, tenant,
            new QualificationStatusCalculator(), new TestClock(Now), audit,
            new CountryCodeValidator(db.Context), new LocalFileStorageService(storageRoot));
        var drivers = new DriverService(db.Context, tenant, audit, new QualificationStatusCalculator(), new TestClock(Now));
        return new Harness(db, qualifications, drivers, tenantId, employeeId, driverId, typeId, storageRoot);
    }

    private static EmployeeQualification Qualification(Harness h, DateOnly? expiry, QualificationStatus status = QualificationStatus.Valid) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId, QualificationTypeId = h.TypeCode95Id,
        ObtainedDate = new DateOnly(2020, 1, 1), ExpiryDate = expiry, Status = status,
        CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
    };

    [Fact]
    public async Task Create_WithIssuingCountry_NormalizesAndPersists()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Qualifications.CreateAsync(h.EmployeeId, new CreateEmployeeQualificationRequest(
            h.TypeCode95Id, "DOC-1", new DateOnly(2024, 1, 1), Today.AddYears(2), null, IssuingCountryCode: "be"),
            CancellationToken.None);

        Assert.Equal("BE", created.IssuingCountryCode);
    }

    [Fact]
    public async Task ExpiringOverview_IncludesEmployeeIdentity_AndCountsPendingButNotSuspended()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Pending counts for the radar (needs attention regardless of verification state);
        // suspended does not (administratively inactive).
        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(10), QualificationStatus.Pending));
        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(12), QualificationStatus.Suspended));
        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(-5)));
        await h.Db.Context.SaveChangesAsync();

        var expiring = await h.Qualifications.ListExpiringWithinDaysAsync(30, CancellationToken.None);
        var expired = await h.Qualifications.ListExpiredAsync(CancellationToken.None);

        var soon = Assert.Single(expiring);
        Assert.Equal("Jan Janssen", soon.EmployeeName);
        Assert.Equal("MED-1", soon.EmployeeNumber);
        Assert.True(soon.EmployeeIsDriver);
        Assert.Single(expired);
    }

    [Fact]
    public async Task ExpiringOverview_WindowWiderThanWarningDays_StillFindsItems()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // Expires in 75 days: outside the 30-day warning window (EffectiveStatus = Valid),
        // but a 90-day radar must still surface it.
        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(75)));
        await h.Db.Context.SaveChangesAsync();

        Assert.Empty(await h.Qualifications.ListExpiringWithinDaysAsync(30, CancellationToken.None));
        Assert.Single(await h.Qualifications.ListExpiringWithinDaysAsync(90, CancellationToken.None));
    }

    [Fact]
    public async Task DriverSearch_FiltersOnExpiringAndExpiredQualifications()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(20)));
        await h.Db.Context.SaveChangesAsync();

        var expiring30 = await h.Drivers.SearchAsync(null, null, null, null, null, null, 30, false, false, PageRequest.Of(1, 25), CancellationToken.None);
        var expiring10 = await h.Drivers.SearchAsync(null, null, null, null, null, null, 10, false, false, PageRequest.Of(1, 25), CancellationToken.None);
        var expired = await h.Drivers.SearchAsync(null, null, null, null, null, null, null, true, false, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Single(expiring30.Items);
        Assert.Empty(expiring10.Items);
        Assert.Empty(expired.Items);
    }

    [Fact]
    public async Task DriverSearch_EligibleOnly_ExcludesExpiredAndBlocked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var eligible = await h.Drivers.SearchAsync(null, null, null, null, null, null, null, false, true, PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Single(eligible.Items);

        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(-1)));
        await h.Db.Context.SaveChangesAsync();

        var afterExpiry = await h.Drivers.SearchAsync(null, null, null, null, null, null, null, false, true, PageRequest.Of(1, 25), CancellationToken.None);
        Assert.Empty(afterExpiry.Items);
    }

    [Fact]
    public async Task DocumentLifecycle_UploadDownloadDelete_Roundtrips()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var qualification = Qualification(h, Today.AddYears(1));
        h.Db.Context.EmployeeQualifications.Add(qualification);
        await h.Db.Context.SaveChangesAsync();

        var payload = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
        using (var upload = new MemoryStream(payload))
        {
            var updated = await h.Qualifications.AttachDocumentAsync(
                h.EmployeeId, qualification.Id, "rijbewijs.pdf", upload, CancellationToken.None);
            Assert.NotNull(updated);
            Assert.True(updated!.HasDocument);
        }

        var document = await h.Qualifications.OpenDocumentAsync(h.EmployeeId, qualification.Id, CancellationToken.None);
        Assert.NotNull(document);
        await using (var content = document!.Value.Content)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            Assert.Equal(payload, buffer.ToArray());
        }
        Assert.EndsWith(".pdf", document.Value.FileName);

        Assert.True(await h.Qualifications.RemoveDocumentAsync(h.EmployeeId, qualification.Id, CancellationToken.None));
        Assert.Null(await h.Qualifications.OpenDocumentAsync(h.EmployeeId, qualification.Id, CancellationToken.None));

        try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Document_ScopedToEmployee_OtherEmployeeGetsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var qualification = Qualification(h, Today.AddYears(1));
        h.Db.Context.EmployeeQualifications.Add(qualification);
        await h.Db.Context.SaveChangesAsync();

        using var upload = new MemoryStream([1, 2, 3]);
        await h.Qualifications.AttachDocumentAsync(h.EmployeeId, qualification.Id, "doc.png", upload, CancellationToken.None);

        var wrongEmployee = await h.Qualifications.OpenDocumentAsync(Guid.NewGuid(), qualification.Id, CancellationToken.None);
        Assert.Null(wrongEmployee);

        try { Directory.Delete(h.StorageRoot, recursive: true); } catch { /* best effort */ }
    }
}
