using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hr;

public class ExpiryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 6, 0, 0, TimeSpan.Zero);

    private static async Task<(SqliteTestDbContext Db, Guid TenantId, Guid UserId)> SeedAsync(int expiresInDays)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var qualTypeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, QualificationExpiryWarningDays = 30 });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "jan@acme.example", FirstName = "Jan", LastName = "J", EmployeeId = employeeId, IsActive = true });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "J",
            DateOfBirth = new(1990, 1, 1), Email = "jan@acme.example", PhoneNumber = "+321", Street = "S", HouseNumber = "1",
            PostalCode = "2000", City = "Antwerpen", EmploymentStartDate = new(2020, 1, 1), EmploymentStatus = EmploymentStatus.Active, IsActive = true,
        });
        db.Context.QualificationTypes.Add(new QualificationType { Id = qualTypeId, Code = "Code95", Name = "Code 95", Category = "Attest", RequiresExpiryDate = true, IsActive = true });
        db.Context.EmployeeQualifications.Add(new EmployeeQualification
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, QualificationTypeId = qualTypeId,
            ObtainedDate = new(2020, 1, 1), ExpiryDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(expiresInDays),
            Status = QualificationStatus.Valid,
        });
        await db.Context.SaveChangesAsync();
        return (db, tenantId, userId);
    }

    [Fact]
    public async Task Code95Policy_ExtendsLeadTimeTo365Days()
    {
        // Code 95 expires in 100 days: outside the 30-day tenant default, but within a 365-day policy.
        var (db, tenantId, userId) = await SeedAsync(expiresInDays: 100);
        using var _ = db;
        db.Context.ExpiryReminderPolicies.Add(new ExpiryReminderPolicy
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TargetKind = ExpiryReminderTargetKind.QualificationType,
            TargetCode = "Code95", LeadTimeDays = 365, IsActive = true,
        });
        await db.Context.SaveChangesAsync();

        await new ExpiryNotificationProducer(db.Context, new TestClock(Now)).ProduceForTenantAsync(tenantId, CancellationToken.None);

        Assert.Contains(db.Context.Notifications, n => n.Type == "qualification_expiring" && n.UserId == userId);
    }

    [Fact]
    public async Task WithoutPolicy_FarExpiry_ProducesNothing()
    {
        // 100 days out with only the 30-day tenant default: no notification.
        var (db, tenantId, _) = await SeedAsync(expiresInDays: 100);
        using var _ = db;

        await new ExpiryNotificationProducer(db.Context, new TestClock(Now)).ProduceForTenantAsync(tenantId, CancellationToken.None);

        Assert.DoesNotContain(db.Context.Notifications, n => n.Type == "qualification_expiring");
    }

    [Fact]
    public async Task Seeder_AddsDefaultPolicies_Idempotently()
    {
        var (db, tenantId, _) = await SeedAsync(expiresInDays: 10);
        using var _ = db;

        await ExpiryPolicySeeder.SeedAsync(db.Context);
        var afterFirst = await db.Context.ExpiryReminderPolicies.CountAsync(p => p.TenantId == tenantId);
        await ExpiryPolicySeeder.SeedAsync(db.Context);
        var afterSecond = await db.Context.ExpiryReminderPolicies.CountAsync(p => p.TenantId == tenantId);

        Assert.True(afterFirst >= 8);
        Assert.Equal(afterFirst, afterSecond);
        Assert.Contains(db.Context.ExpiryReminderPolicies,
            p => p.TargetKind == ExpiryReminderTargetKind.QualificationType && p.TargetCode == "Code95" && p.LeadTimeDays == 365);
    }
}
