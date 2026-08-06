using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hr;

/// <summary>
/// HR maturity wave, task 4: automatic follow-up notifications for incomplete employee dossiers,
/// reusing HrReminderProducer's dedupe/recipient infrastructure. Completeness is delegated to
/// <c>EmployeeCompletenessService</c> (task 2); the age thresholds come from
/// <c>HrReminderSettings.DossierReminderDays</c> / <c>DossierEscalationDays</c> (task 3).
/// </summary>
public class EmployeeDossierReminderTests
{
    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid HrUserId, Guid ManagementUserId);

    private static async Task<Harness> SeedAsync(TestClock clock)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();
        var managementUserId = Guid.NewGuid();
        var hrRoleId = Guid.NewGuid();
        var managementRoleId = Guid.NewGuid();
        var now = clock.GetUtcNow().UtcDateTime;

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = now });
        db.Context.Roles.Add(new Role { Id = hrRoleId, TenantId = tenantId, Name = "HR", TemplateCode = "hr", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Context.Roles.Add(new Role { Id = managementRoleId, TenantId = tenantId, Name = "Management", TemplateCode = "management", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Context.Users.Add(new User { Id = hrUserId, TenantId = tenantId, Email = "hr@acme.example", FirstName = "H", LastName = "R", IsActive = true });
        db.Context.Users.Add(new User { Id = managementUserId, TenantId = tenantId, Email = "mgmt@acme.example", FirstName = "M", LastName = "T", IsActive = true });
        db.Context.UserRoles.Add(new UserRole { UserId = hrUserId, RoleId = hrRoleId });
        db.Context.UserRoles.Add(new UserRole { UserId = managementUserId, RoleId = managementRoleId });

        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, hrUserId, managementUserId);
    }

    /// <summary>An employee with no dossier data at all (0% complete).</summary>
    private static Employee IncompleteEmployee(Guid tenantId, DateTime createdAt, string first = "Jan", string last = "Janssen") => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeNumber = "MED-" + Guid.NewGuid().ToString("N")[..8],
        FirstName = first,
        LastName = last,
        IsActive = true,
        CreatedAt = createdAt,
    };

    /// <summary>An employee whose dossier satisfies every non-conditional requirement (100%).</summary>
    private static Employee CompleteEmployee(Guid tenantId, DateTime createdAt, Guid jobFunctionId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeNumber = "MED-" + Guid.NewGuid().ToString("N")[..8],
        FirstName = "Ann",
        LastName = "Peeters",
        IsActive = true,
        CreatedAt = createdAt,
        DateOfBirth = new DateOnly(1990, 1, 1),
        NationalRegisterNumber = "90010112345",
        Street = "Kerkstraat",
        PostalCode = "2000",
        City = "Antwerpen",
        Email = "ann@acme.example",
        Iban = "BE68539007547034",
        EmploymentStartDate = new DateOnly(2020, 1, 1),
        ContractTypeId = Guid.NewGuid(),
        DepartmentId = Guid.NewGuid(),
    };

    private static async Task<Guid> AddJobFunctionAsync(Harness h)
    {
        var jobFunction = new TransportationService.Api.Modules.Organization.Entities.JobFunction
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "DRV", Name = "Chauffeur", IsActive = true,
        };
        h.Db.Context.Add(jobFunction);
        await h.Db.Context.SaveChangesAsync();
        return jobFunction.Id;
    }

    private static void CompleteRemainder(Harness h, Employee employee, Guid jobFunctionId)
    {
        h.Db.Context.Add(new EmployeeJobFunction { EmployeeId = employee.Id, JobFunctionId = jobFunctionId });
        h.Db.Context.Add(new EmployeeEmergencyContact
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employee.Id, Name = "Piet Peeters", Priority = 1,
        });
        h.Db.Context.Add(new EmployeeDocument
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employee.Id,
            Category = EmployeeDocumentCategory.IdentityCardFront, FileName = "id.pdf", StorageKey = "k1",
        });
        h.Db.Context.Add(new EmployeeDocument
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = employee.Id,
            Category = EmployeeDocumentCategory.Contract, FileName = "contract.pdf", StorageKey = "k2",
        });
    }

    private static async Task SetSettingsAsync(Harness h, HrReminderSettings settings)
    {
        settings.Id = Guid.NewGuid();
        settings.TenantId = h.TenantId;
        h.Db.Context.HrReminderSettings.Add(settings);
        await h.Db.Context.SaveChangesAsync();
    }

    /// <summary>
    /// The production <c>AuditingSaveChangesInterceptor</c> always stamps <c>CreatedAt</c> with
    /// the real insert time on <see cref="DbContext.SaveChangesAsync"/> (and refuses to let a
    /// later Modified save touch it), so an employee's age for the reminder cutoffs cannot be
    /// backdated through the ORM. Raw SQL bypasses the interceptor entirely — same technique
    /// <c>InventoryStockTests</c> uses to force a stale concurrency token.
    /// </summary>
    private static async Task BackdateCreatedAtAsync(Harness h, Guid employeeId, DateTime createdAt) =>
        await h.Db.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Employees SET CreatedAt = {createdAt} WHERE Id = {employeeId}");

    [Fact]
    public async Task IncompleteDossier_OlderThanSevenDays_NotifiesHr_AndDedupesAcrossSweeps()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var createdAt = clock.GetUtcNow().UtcDateTime.AddDays(-8);
        var employee = IncompleteEmployee(h.TenantId, createdAt);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, employee.Id, createdAt);

        var producer = new HrReminderProducer(h.Db.Context, clock);
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None); // second sweep, same week

        var notifications = await h.Db.Context.Notifications
            .Where(n => n.Type == "employee_dossier_incomplete").ToListAsync();
        var notification = Assert.Single(notifications);
        Assert.Equal(h.HrUserId, notification.UserId);
        Assert.Equal(NotificationCategory.Hr, notification.Category);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
        Assert.Equal($"/employees/{employee.Id}", notification.LinkPath);
        Assert.Contains("Jan Janssen", notification.Message);
        Assert.Contains("0%", notification.Message);

        Assert.Single(await h.Db.Context.ReminderDispatchLogs.Where(l => l.Kind == "dossier_incomplete").ToListAsync());
    }

    [Fact]
    public async Task IncompleteDossier_YoungerThanSevenDays_ProducesNothingYet()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var createdAt = clock.GetUtcNow().UtcDateTime.AddDays(-2);
        var employee = IncompleteEmployee(h.TenantId, createdAt);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, employee.Id, createdAt);

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(await h.Db.Context.Notifications.Where(n => n.Type == "employee_dossier_incomplete").ToListAsync());
    }

    [Fact]
    public async Task CompleteDossier_OlderThanSevenDays_ProducesNothing()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var jobFunctionId = await AddJobFunctionAsync(h);
        var createdAt = clock.GetUtcNow().UtcDateTime.AddDays(-40);
        var employee = CompleteEmployee(h.TenantId, createdAt, jobFunctionId);
        h.Db.Context.Employees.Add(employee);
        CompleteRemainder(h, employee, jobFunctionId);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, employee.Id, createdAt);

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(await h.Db.Context.Notifications.Where(n =>
            n.Type == "employee_dossier_incomplete" || n.Type == "employee_dossier_incomplete_escalated").ToListAsync());
    }

    [Fact]
    public async Task IncompleteDossier_OlderThanThirtyDays_AlsoEscalates_ToHrAndManagement()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var createdAt = clock.GetUtcNow().UtcDateTime.AddDays(-31);
        var employee = IncompleteEmployee(h.TenantId, createdAt);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, employee.Id, createdAt);

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        var baseNotifications = await h.Db.Context.Notifications
            .Where(n => n.Type == "employee_dossier_incomplete").ToListAsync();
        Assert.Single(baseNotifications);

        var escalated = await h.Db.Context.Notifications
            .Where(n => n.Type == "employee_dossier_incomplete_escalated").ToListAsync();
        Assert.Equal(2, escalated.Count);
        Assert.Contains(escalated, n => n.UserId == h.HrUserId);
        Assert.Contains(escalated, n => n.UserId == h.ManagementUserId);
        Assert.All(escalated, n => Assert.Equal(NotificationSeverity.Critical, n.Severity));
        Assert.All(escalated, n => Assert.Equal(NotificationCategory.Hr, n.Category));

        Assert.Single(await h.Db.Context.ReminderDispatchLogs.Where(l => l.Kind == "dossier_escalated").ToListAsync());

        // Second sweep same week: no duplicates of either stage.
        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "employee_dossier_incomplete").ToListAsync());
        Assert.Equal(2, (await h.Db.Context.Notifications.Where(n => n.Type == "employee_dossier_incomplete_escalated").ToListAsync()).Count);
    }

    [Fact]
    public async Task DossierRemindersDisabled_ProducesNothing()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        await SetSettingsAsync(h, new HrReminderSettings
        {
            BirthdayEnabled = false, SeniorityEnabled = false, EmploymentEndEnabled = false,
            DossierRemindersEnabled = false,
        });
        var createdAt = clock.GetUtcNow().UtcDateTime.AddDays(-40);
        var employee = IncompleteEmployee(h.TenantId, createdAt);
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, employee.Id, createdAt);

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(await h.Db.Context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task InactiveEmployee_IncompleteAndOld_ProducesNothing()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 6, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var createdAt = clock.GetUtcNow().UtcDateTime.AddDays(-40);
        var employee = IncompleteEmployee(h.TenantId, createdAt);
        employee.IsActive = false;
        h.Db.Context.Employees.Add(employee);
        await h.Db.Context.SaveChangesAsync();
        await BackdateCreatedAtAsync(h, employee.Id, createdAt);

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(await h.Db.Context.Notifications.Where(n =>
            n.Type == "employee_dossier_incomplete" || n.Type == "employee_dossier_incomplete_escalated").ToListAsync());
    }
}
