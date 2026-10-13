using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

/// <summary>HR wave 2026-09-12 §5/§6: expiring/expired documents and qualifications on the dossier.</summary>
public class EmployeeAttentionTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 9, 12);

    private sealed record Harness(SqliteTestDbContext Db, EmployeeAttentionService Sut, Guid TenantId, Guid EmployeeId, Guid TypeId);

    private static async Task<Harness> SeedAsync(int? qualificationWarningDays = 30)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = DateTime.UtcNow });
        if (qualificationWarningDays is { } days)
        {
            db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, QualificationExpiryWarningDays = days });
        }

        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Ann", LastName = "Peeters", IsActive = true,
        });
        db.Context.QualificationTypes.Add(new QualificationType
        {
            Id = typeId, Code = "Code95", Name = "Code 95", Category = "Certificaat", RequiresExpiryDate = true, IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        var sut = new EmployeeAttentionService(db.Context, new DevTenantContext(tenantId), new QualificationStatusCalculator(), new TestClock(Now));
        return new Harness(db, sut, tenantId, employeeId, typeId);
    }

    private static EmployeeDocument Document(Harness h, DateOnly? expiry, EmployeeDocumentCategory category = EmployeeDocumentCategory.IdentityCardFront,
        bool archived = false, string? label = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId, Category = category, CustomLabel = label,
        FileName = "doc.pdf", StorageKey = "k", ExpiryDate = expiry, IsArchived = archived,
    };

    private static EmployeeQualification Qualification(Harness h, DateOnly? expiry, QualificationStatus status = QualificationStatus.Valid) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.EmployeeId, QualificationTypeId = h.TypeId,
        ObtainedDate = new DateOnly(2021, 1, 1), ExpiryDate = expiry, Status = status, DocumentNumber = "C95-1",
        CreatedAt = Now, UpdatedAt = Now,
    };

    [Fact]
    public async Task NoExpiringRows_YieldsEmptySnapshot()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.EmployeeDocuments.Add(Document(h, Today.AddDays(200)));
        h.Db.Context.EmployeeDocuments.Add(Document(h, null));
        h.Db.Context.EmployeeQualifications.Add(Qualification(h, Today.AddDays(400)));
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        Assert.False(result.HasItems);
        Assert.Equal(0, result.DocumentsExpiring + result.DocumentsExpired + result.QualificationsExpiring + result.QualificationsExpired);
    }

    [Fact]
    public async Task Documents_ExpiringWithinDefault30Days_AndExpired_AreReported_ExpiredFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var expiring = Document(h, Today.AddDays(10), label: "Paspoort");
        var expired = Document(h, Today.AddDays(-3), EmployeeDocumentCategory.Contract);
        var boundary = Document(h, Today.AddDays(30), EmployeeDocumentCategory.Certificate);
        var outside = Document(h, Today.AddDays(31), EmployeeDocumentCategory.Other);
        h.Db.Context.EmployeeDocuments.AddRange(expiring, expired, boundary, outside);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        Assert.Equal(2, result.DocumentsExpiring);
        Assert.Equal(1, result.DocumentsExpired);
        Assert.Equal(expired.Id, result.Items[0].Id);
        Assert.Equal("expired", result.Items[0].State);
        Assert.Equal(-3, result.Items[0].DaysLeft);
        var passport = result.Items.Single(i => i.Id == expiring.Id);
        Assert.Equal("Paspoort", passport.Label);
        Assert.Equal("IdentityCardFront", passport.Detail);
        Assert.Equal(10, passport.DaysLeft);
        Assert.DoesNotContain(result.Items, i => i.Id == outside.Id);
    }

    [Fact]
    public async Task ArchivedDocuments_AreNeverReported()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.EmployeeDocuments.Add(Document(h, Today.AddDays(-1), archived: true));
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        Assert.False(result.HasItems);
    }

    [Fact]
    public async Task DocumentLeadTime_FollowsExistingExpiryPolicy_PerCategoryAndWildcard()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.ExpiryReminderPolicies.AddRange(
            new ExpiryReminderPolicy { Id = Guid.NewGuid(), TenantId = h.TenantId, TargetKind = ExpiryReminderTargetKind.EmployeeDocumentCategory, TargetCode = "Contract", LeadTimeDays = 90 },
            new ExpiryReminderPolicy { Id = Guid.NewGuid(), TenantId = h.TenantId, TargetKind = ExpiryReminderTargetKind.EmployeeDocumentCategory, TargetCode = "*", LeadTimeDays = 7 });
        var contract = Document(h, Today.AddDays(60), EmployeeDocumentCategory.Contract);   // inside 90-day policy
        var other = Document(h, Today.AddDays(20), EmployeeDocumentCategory.Other);         // outside 7-day wildcard
        h.Db.Context.EmployeeDocuments.AddRange(contract, other);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal(contract.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task Qualifications_UseTenantWarningWindow_AndSkipSuspendedOrPending()
    {
        var h = await SeedAsync(qualificationWarningDays: 60);
        using var _ = h.Db;
        var expiring = Qualification(h, Today.AddDays(45));
        var expired = Qualification(h, Today.AddDays(-10));
        var suspended = Qualification(h, Today.AddDays(-10), QualificationStatus.Suspended);
        var pending = Qualification(h, Today.AddDays(5), QualificationStatus.Pending);
        var fine = Qualification(h, Today.AddDays(61));
        h.Db.Context.EmployeeQualifications.AddRange(expiring, expired, suspended, pending, fine);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        Assert.Equal(1, result.QualificationsExpiring);
        Assert.Equal(1, result.QualificationsExpired);
        Assert.Equal(expired.Id, result.Items[0].Id);
        var soon = result.Items.Single(i => i.Id == expiring.Id);
        Assert.Equal("qualification", soon.Kind);
        Assert.Equal("Code 95", soon.Label);
        Assert.Equal("C95-1", soon.Detail);
        Assert.Equal("expiring", soon.State);
    }

    [Fact]
    public async Task OtherTenantsRows_AreInvisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var foreignTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "B", Slug = $"b-{foreignTenant:N}", IsActive = true, CreatedAt = DateTime.UtcNow });
        h.Db.Context.EmployeeDocuments.Add(new EmployeeDocument
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, EmployeeId = h.EmployeeId, Category = EmployeeDocumentCategory.Contract,
            FileName = "x.pdf", StorageKey = "x", ExpiryDate = Today.AddDays(-1),
        });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.GetForEmployeeAsync(h.EmployeeId, CancellationToken.None);

        Assert.False(result.HasItems);
    }
}
