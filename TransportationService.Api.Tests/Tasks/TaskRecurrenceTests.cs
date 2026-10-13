using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Tasks.Entities;
using TransportationService.Api.Modules.Tasks.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Tasks;

/// <summary>Sprint fase 10: idempotente generatie van terugkerende taken met dedupe keys.</summary>
public class TaskRecurrenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 08, 03, 6, 0, 0, TimeSpan.Zero); // maandag

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, Guid UserId, Guid TemplateId, Guid ItemId)
    {
        public TaskRecurrenceGenerator Generator()
        {
            var tenant = new DevTenantContext(TenantId);
            return new TaskRecurrenceGenerator(Db.Context, tenant,
                new NotificationService(Db.Context, tenant, new DevCurrentUserContext(null), new TestClock(Now)));
        }
    }

    private static async Task<Harness> SeedAsync(bool employeeActive = true)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "MED-1", FirstName = "Jan", LastName = "Janssen", IsActive = employeeActive,
        });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "jan@acme.be", FirstName = "Jan", LastName = "Janssen", EmployeeId = employeeId, IsActive = true });
        db.Context.TaskTemplates.Add(new TaskTemplate { Id = templateId, TenantId = tenantId, Name = "Weekcontrole", IsActive = true });
        db.Context.TaskTemplateItems.Add(new TaskTemplateItem
        {
            Id = itemId, TenantId = tenantId, TemplateId = templateId, Title = "Voorraad tellen", DueInDays = 2,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, employeeId, userId, templateId, itemId);
    }

    private static TaskRecurrence Weekly(Harness h) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.TenantId, TemplateId = h.TemplateId,
        AssignedEmployeeId = h.EmployeeId, Interval = TaskRecurrenceInterval.Weekly,
        StartDate = new DateOnly(2026, 7, 1), IsActive = true,
    };

    [Fact]
    public async Task Generate_CreatesTaskOncePerPeriod_AndNotifies()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TaskRecurrences.Add(Weekly(h));
        await h.Db.Context.SaveChangesAsync();
        var today = new DateOnly(2026, 8, 3);

        Assert.Equal(1, await h.Generator().GenerateDueAsync(today, CancellationToken.None));
        // Herhaalde runs binnen dezelfde periode genereren niets nieuws.
        Assert.Equal(0, await h.Generator().GenerateDueAsync(today, CancellationToken.None));
        Assert.Equal(0, await h.Generator().GenerateDueAsync(today.AddDays(3), CancellationToken.None));

        var task = Assert.Single(await h.Db.Context.EmployeeTasks.ToListAsync());
        Assert.Equal("Voorraad tellen", task.Title);
        Assert.Equal(new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc), task.DueAt); // maandag + 2
        Assert.NotNull(task.RecurrenceDedupeKey);
        Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "task_assigned").ToListAsync());

        // Volgende week wél een nieuwe taak.
        Assert.Equal(1, await h.Generator().GenerateDueAsync(today.AddDays(7), CancellationToken.None));
        Assert.Equal(2, await h.Db.Context.EmployeeTasks.CountAsync());
    }

    [Fact]
    public async Task Generate_SkipsInactiveTemplateRecurrenceAndEmployee()
    {
        var h = await SeedAsync(employeeActive: false);
        using var _ = h.Db;
        var recurrence = Weekly(h);
        h.Db.Context.TaskRecurrences.Add(recurrence);
        await h.Db.Context.SaveChangesAsync();

        // Inactieve medewerker → niets.
        Assert.Equal(0, await h.Generator().GenerateDueAsync(new DateOnly(2026, 8, 3), CancellationToken.None));

        var employee = await h.Db.Context.Employees.SingleAsync();
        employee.IsActive = true;
        recurrence.IsActive = false;
        await h.Db.Context.SaveChangesAsync();

        // Inactieve herhaling → niets.
        Assert.Equal(0, await h.Generator().GenerateDueAsync(new DateOnly(2026, 8, 3), CancellationToken.None));
    }

    [Fact]
    public async Task Generate_TemplateEdit_DoesNotTouchExistingTasks()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        h.Db.Context.TaskRecurrences.Add(Weekly(h));
        await h.Db.Context.SaveChangesAsync();
        await h.Generator().GenerateDueAsync(new DateOnly(2026, 8, 3), CancellationToken.None);

        var item = await h.Db.Context.TaskTemplateItems.SingleAsync();
        item.Title = "Volledig hertellen";
        await h.Db.Context.SaveChangesAsync();
        await h.Generator().GenerateDueAsync(new DateOnly(2026, 8, 10), CancellationToken.None);

        var titles = (await h.Db.Context.EmployeeTasks.OrderBy(t => t.CreatedAt).Select(t => t.Title).ToListAsync());
        Assert.Equal(["Voorraad tellen", "Volledig hertellen"], titles);
    }

    [Fact]
    public void PeriodStart_ComputesPerInterval()
    {
        var recurrence = new TaskRecurrence { StartDate = new DateOnly(2026, 7, 10), CustomIntervalDays = 10 };
        var wednesday = new DateOnly(2026, 8, 5);

        recurrence.Interval = TaskRecurrenceInterval.Daily;
        Assert.Equal(wednesday, TaskRecurrenceGenerator.PeriodStartFor(recurrence, wednesday));
        recurrence.Interval = TaskRecurrenceInterval.Weekly;
        Assert.Equal(new DateOnly(2026, 8, 3), TaskRecurrenceGenerator.PeriodStartFor(recurrence, wednesday));
        recurrence.Interval = TaskRecurrenceInterval.Monthly;
        Assert.Equal(new DateOnly(2026, 8, 1), TaskRecurrenceGenerator.PeriodStartFor(recurrence, wednesday));
        recurrence.Interval = TaskRecurrenceInterval.Yearly;
        Assert.Equal(new DateOnly(2026, 1, 1), TaskRecurrenceGenerator.PeriodStartFor(recurrence, wednesday));
        recurrence.Interval = TaskRecurrenceInterval.CustomDays;
        Assert.Equal(new DateOnly(2026, 7, 30), TaskRecurrenceGenerator.PeriodStartFor(recurrence, wednesday)); // 10 juli + 2×10
    }
}
