using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Messaging.Entities;
using TransportationService.Api.Modules.Notifications.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Hr;

public class HrReminderTests
{
    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid HrUserId);

    private static async Task<Harness> SeedAsync(TestClock clock, params Employee[] employees)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();
        var hrRoleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = clock.GetUtcNow().UtcDateTime });
        db.Context.Roles.Add(new Role { Id = hrRoleId, TenantId = tenantId, Name = "HR", TemplateCode = "hr", IsActive = true, CreatedAt = clock.GetUtcNow().UtcDateTime, UpdatedAt = clock.GetUtcNow().UtcDateTime });
        db.Context.Users.Add(new User { Id = hrUserId, TenantId = tenantId, Email = "hr@acme.example", FirstName = "H", LastName = "R", IsActive = true });
        db.Context.UserRoles.Add(new UserRole { UserId = hrUserId, RoleId = hrRoleId });
        foreach (var employee in employees)
        {
            employee.TenantId = tenantId;
            db.Context.Employees.Add(employee);
        }

        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, hrUserId);
    }

    private static Employee Employee(DateOnly dob, DateOnly start, DateOnly? end = null, string? email = "jan@acme.example") => new()
    {
        Id = Guid.NewGuid(), EmployeeNumber = "MED-" + Guid.NewGuid().ToString("N")[..4],
        FirstName = "Jan", LastName = "Janssen", DateOfBirth = dob,
        Email = email ?? string.Empty, PhoneNumber = "+3231112233",
        Street = "Straat", HouseNumber = "1", PostalCode = "2000", City = "Antwerpen",
        EmploymentStartDate = start, EmploymentEndDate = end,
        EmploymentStatus = EmploymentStatus.Active, IsActive = true,
    };

    private static async Task SetSettingsAsync(Harness h, HrReminderSettings settings)
    {
        settings.Id = Guid.NewGuid();
        settings.TenantId = h.TenantId;
        h.Db.Context.HrReminderSettings.Add(settings);
        await h.Db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Birthday_NotifiesHr_OnTheDay_AndDedupesAcrossSweeps()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock, Employee(new DateOnly(1990, 7, 21), new DateOnly(2020, 1, 1)));
        using var _ = h.Db;
        var producer = new HrReminderProducer(h.Db.Context, clock);

        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        await producer.ProduceForTenantAsync(h.TenantId, CancellationToken.None); // second sweep same day

        var notifications = await h.Db.Context.Notifications.Where(n => n.Type == "hr_birthday").ToListAsync();
        Assert.Single(notifications);
        Assert.Equal(h.HrUserId, notifications[0].UserId);
        Assert.Single(await h.Db.Context.ReminderDispatchLogs.Where(l => l.Kind == "birthday").ToListAsync());
    }

    [Fact]
    public async Task Birthday_LeapDay_FiresOn28FebInNonLeapYear()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 2, 28, 6, 0, 0, TimeSpan.Zero)); // 2026 not a leap year
        var h = await SeedAsync(clock, Employee(new DateOnly(1992, 2, 29), new DateOnly(2020, 1, 1)));
        using var _ = h.Db;

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "hr_birthday").ToListAsync());
    }

    [Fact]
    public async Task Birthday_Disabled_ProducesNothing()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock, Employee(new DateOnly(1990, 7, 21), new DateOnly(2020, 1, 1)));
        using var _ = h.Db;
        await SetSettingsAsync(h, new HrReminderSettings { BirthdayEnabled = false, SeniorityEnabled = false, EmploymentEndEnabled = false });

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(await h.Db.Context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Seniority_WarnsHr_60DaysBefore_AndEmailsEmployee_OnDate()
    {
        // Employee started 2016-09-19 → 10 years on 2026-09-19; warning 60 days before = 2026-07-21.
        var warnDay = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(warnDay, Employee(new DateOnly(1985, 1, 1), new DateOnly(2016, 9, 19)));
        using var _ = h.Db;

        await new HrReminderProducer(h.Db.Context, warnDay).ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "hr_seniority").ToListAsync());
        Assert.Empty(await h.Db.Context.OutboxMessages.Where(m => m.Kind == MessageKinds.HrSeniority).ToListAsync());

        // Advance to the milestone date: the employee gets an e-mail.
        warnDay.Advance(TimeSpan.FromDays(60));
        await new HrReminderProducer(h.Db.Context, warnDay).ProduceForTenantAsync(h.TenantId, CancellationToken.None);
        var mail = Assert.Single(await h.Db.Context.OutboxMessages.Where(m => m.Kind == MessageKinds.HrSeniority).ToListAsync());
        Assert.Equal(MessageOwnerType.Employee, mail.OwnerType);
        Assert.Contains("10 jaar", mail.Body);
    }

    [Fact]
    public async Task EmploymentEnd_WarnsHr_OneWeekBefore_ByDefault()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock, Employee(new DateOnly(1985, 1, 1), new DateOnly(2020, 1, 1), end: new DateOnly(2026, 7, 28)));
        using var _ = h.Db;

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        var notification = Assert.Single(await h.Db.Context.Notifications.Where(n => n.Type == "hr_employment_end").ToListAsync());
        Assert.Equal(h.HrUserId, notification.UserId);
    }

    [Fact]
    public async Task Seniority_NonMilestoneYear_ProducesNothing()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        // 3 years of service is not a configured milestone (1,10,15,20,25,30).
        var h = await SeedAsync(clock, Employee(new DateOnly(1985, 1, 1), new DateOnly(2023, 7, 21)));
        using var _ = h.Db;

        await new HrReminderProducer(h.Db.Context, clock).ProduceForTenantAsync(h.TenantId, CancellationToken.None);

        Assert.Empty(await h.Db.Context.Notifications.Where(n => n.Type == "hr_seniority").ToListAsync());
    }

    [Fact]
    public async Task ConfigService_UpdatesSettings_AndManagesPolicies()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new HrReminderConfigService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));

        var settings = await sut.GetSettingsAsync(CancellationToken.None);
        Assert.True(settings.BirthdayEnabled);
        Assert.Equal("1,10,15,20,25,30", settings.SeniorityMilestoneYears);

        var updated = await sut.UpdateSettingsAsync(settings with { BirthdayDaysBefore = 3, SeniorityMilestoneYears = "5, 10, bad, 10" }, CancellationToken.None);
        Assert.Equal(3, updated.BirthdayDaysBefore);
        Assert.Equal("5,10", updated.SeniorityMilestoneYears); // normalized + deduped

        var policy = await sut.CreatePolicyAsync(new SaveExpiryReminderPolicyRequest(
            ExpiryReminderTargetKind.EmployeeDocumentCategory, "IdentityCardFront", 30, null, true, true, false, false, false, true), CancellationToken.None);
        Assert.Equal(30, policy.LeadTimeDays);

        var list = await sut.ListPoliciesAsync(CancellationToken.None);
        Assert.Single(list);
        Assert.True(await sut.DeletePolicyAsync(policy.Id, CancellationToken.None));
        Assert.Empty(await sut.ListPoliciesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConfigService_DossierReminderDefaults_AreSevenAndThirtyEnabled()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new HrReminderConfigService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));

        var settings = await sut.GetSettingsAsync(CancellationToken.None);

        Assert.True(settings.DossierRemindersEnabled);
        Assert.Equal(7, settings.DossierReminderDays);
        Assert.Equal(30, settings.DossierEscalationDays);
    }

    [Fact]
    public async Task ConfigService_UpdatesDossierReminderSettings_WhenValid()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new HrReminderConfigService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));

        var settings = await sut.GetSettingsAsync(CancellationToken.None);
        var updated = await sut.UpdateSettingsAsync(settings with
        {
            DossierRemindersEnabled = false,
            DossierReminderDays = 14,
            DossierEscalationDays = 45,
        }, CancellationToken.None);

        Assert.False(updated.DossierRemindersEnabled);
        Assert.Equal(14, updated.DossierReminderDays);
        Assert.Equal(45, updated.DossierEscalationDays);
    }

    [Fact]
    public async Task ConfigService_RejectsDossierEscalation_WhenNotLaterThanReminder()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new HrReminderConfigService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));

        var settings = await sut.GetSettingsAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => sut.UpdateSettingsAsync(settings with
        {
            DossierReminderDays = 30,
            DossierEscalationDays = 30,
        }, CancellationToken.None));

        Assert.NotNull(ex.FieldErrors);
        Assert.True(ex.FieldErrors!.ContainsKey("dossierEscalationDays"));
        Assert.Equal("Escalatie moet later vallen dan de eerste melding.", ex.FieldErrors["dossierEscalationDays"][0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task ConfigService_RejectsDossierReminderDays_OutsideOneToThreeSixtyFive(int invalidDays)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new HrReminderConfigService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));

        var settings = await sut.GetSettingsAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => sut.UpdateSettingsAsync(settings with
        {
            DossierReminderDays = invalidDays,
            DossierEscalationDays = 200,
        }, CancellationToken.None));

        Assert.NotNull(ex.FieldErrors);
        Assert.True(ex.FieldErrors!.ContainsKey("dossierReminderDays"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task ConfigService_RejectsDossierEscalationDays_OutsideOneToThreeSixtyFive(int invalidDays)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 7, 21, 6, 0, 0, TimeSpan.Zero));
        var h = await SeedAsync(clock);
        using var _ = h.Db;
        var tenant = new DevTenantContext(h.TenantId);
        var sut = new HrReminderConfigService(h.Db.Context, tenant, new AuditService(h.Db.Context, tenant, new DevCurrentUserContext(null)));

        var settings = await sut.GetSettingsAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => sut.UpdateSettingsAsync(settings with
        {
            DossierReminderDays = 5,
            DossierEscalationDays = invalidDays,
        }, CancellationToken.None));

        Assert.NotNull(ex.FieldErrors);
        Assert.True(ex.FieldErrors!.ContainsKey("dossierEscalationDays"));
    }
}
