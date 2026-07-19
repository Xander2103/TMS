using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Authentication.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Notifications.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Planning.Entities;
using TransportationService.Api.Modules.Portal.Dtos;
using TransportationService.Api.Modules.Portal.Services;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Portal;

/// <summary>
/// Wave 5: the employee portal is strictly self-scoped — profile, absences, qualifications,
/// dashboard and password change always resolve through the logged-in user's employee link.
/// </summary>
public class PortalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        SqliteTestDbContext Db, PortalService Sut, string StorageRoot, Guid TenantId,
        Guid MeUserId, Guid MeEmployeeId, Guid OtherEmployeeId, Guid OtherQualificationId, Guid DriverId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var meEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var meUserId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var qualTypeId = Guid.NewGuid();
        var otherQualificationId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "TRANS", Name = "Transport", IsActive = true });
        db.Context.Employees.AddRange(
            new Employee
            {
                Id = meEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-1",
                FirstName = "Jan", LastName = "Jansen", Email = "jan@acme.be", PhoneNumber = "0470",
                Street = "Dorpstraat", HouseNumber = "1", PostalCode = "9000", City = "Gent",
                DepartmentId = departmentId,
                EmploymentStartDate = new(2020, 1, 1),
                CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new Employee
            {
                Id = otherEmployeeId, TenantId = tenantId, EmployeeNumber = "MED-2",
                FirstName = "Piet", LastName = "Peters", CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Drivers.Add(new Driver { Id = driverId, TenantId = tenantId, DriverNumber = "CH-1", EmployeeId = meEmployeeId, IsActive = true });
        db.Context.Users.Add(new User
        {
            Id = meUserId, TenantId = tenantId, Email = "jan@acme.be", PasswordHash = new PasswordHasher().Hash("OudWachtwoord1!"),
            FirstName = "Jan", LastName = "Jansen", EmployeeId = meEmployeeId, IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        db.Context.QualificationTypes.Add(new QualificationType
        {
            Id = qualTypeId, Code = "code95", Name = "Code 95", Category = "Rijbewijs", RequiresExpiryDate = true, IsActive = true,
        });
        db.Context.EmployeeQualifications.AddRange(
            new EmployeeQualification
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = meEmployeeId, QualificationTypeId = qualTypeId,
                ObtainedDate = new(2022, 1, 1), ExpiryDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(10),
                Status = QualificationStatus.Valid, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            },
            new EmployeeQualification
            {
                Id = otherQualificationId, TenantId = tenantId, EmployeeId = otherEmployeeId, QualificationTypeId = qualTypeId,
                ObtainedDate = new(2022, 1, 1), ExpiryDate = new(2030, 1, 1),
                Status = QualificationStatus.Valid, CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            });
        db.Context.Absences.Add(new Absence
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = otherEmployeeId,
            Type = AbsenceType.Vacation, StartDate = new(2026, 8, 1), EndDate = new(2026, 8, 5),
            Status = AbsenceStatus.Requested,
        });
        db.Context.Trips.Add(new Trip
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TripNumber = "RIT-0001",
            TripDate = DateOnly.FromDateTime(Now.UtcDateTime), DriverId = driverId, Status = TripStatus.Planned,
        });
        await db.Context.SaveChangesAsync();

        var storageRoot = Path.Combine(Path.GetTempPath(), "ts-portal-tests", Guid.NewGuid().ToString("N"));
        return new Harness(db, CreateSut(db, tenantId, meUserId, storageRoot), storageRoot,
            tenantId, meUserId, meEmployeeId, otherEmployeeId, otherQualificationId, driverId);
    }

    private static PortalService CreateSut(SqliteTestDbContext db, Guid tenantId, Guid userId, string storageRoot)
    {
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(userId);
        var audit = new AuditService(db.Context, tenant, user);
        var clock = new TestClock(Now);
        var notifications = new NotificationService(db.Context, tenant, user, clock);
        var absences = new AbsenceService(db.Context, tenant, user, audit, notifications,
            new LocalFileStorageService(storageRoot),
            new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(),
            new TransportationService.Api.Modules.Messaging.Services.MessageOutboxService(db.Context, tenant, clock),
            clock);
        var qualifications = new QualificationService(
            db.Context, tenant, new QualificationStatusCalculator(), clock, audit,
            new TransportationService.Api.Common.Reference.CountryCodeValidator(db.Context),
            new LocalFileStorageService(storageRoot));
        return new PortalService(
            db.Context, tenant, user, absences, qualifications, notifications,
            new QualificationStatusCalculator(), new PasswordHasher(), audit,
            new TransportationService.Api.Modules.EmployeePlanning.Services.ShiftService(
                db.Context, tenant, audit, notifications,
                new TransportationService.Api.Modules.Integrations.Services.NoOpCalendarSyncService(), clock),
            clock);
    }

    [Fact]
    public async Task Profile_ResolvesOwnEmployee_WithDriverLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var profile = await h.Sut.GetMyProfileAsync(CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("MED-1", profile!.EmployeeNumber);
        Assert.Equal("Jan", profile.FirstName);
        Assert.Equal("Transport", profile.DepartmentName);
        Assert.True(profile.IsDriver);
        Assert.Equal("CH-1", profile.DriverNumber);
    }

    [Fact]
    public async Task Profile_NullWithoutEmployeeLink()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var strangerUser = Guid.NewGuid();
        h.Db.Context.Users.Add(new User
        {
            Id = strangerUser, TenantId = h.TenantId, Email = "los@acme.be", PasswordHash = "x",
            FirstName = "Los", LastName = "Account", IsActive = true,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await h.Db.Context.SaveChangesAsync();

        var stranger = CreateSut(h.Db, h.TenantId, strangerUser, h.StorageRoot);
        Assert.Null(await stranger.GetMyProfileAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Absences_ListCreateCancel_AreSelfScoped()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateMyAbsenceAsync(
            new TransportationService.Api.Modules.Hr.Dtos.CreateAbsenceRequest(
                AbsenceType.Vacation, new(2026, 9, 1), new(2026, 9, 5), "Vakantie"),
            CancellationToken.None);
        Assert.Equal(PortalOutcome.Success, created.Outcome);

        var mine = await h.Sut.ListMyAbsencesAsync(CancellationToken.None);
        var single = Assert.Single(mine!);
        Assert.Equal(h.MeEmployeeId, single.EmployeeId);

        // Withdrawing an own pending request works; other people's requests are invisible.
        var cancelled = await h.Sut.CancelMyAbsenceAsync(single.Id, CancellationToken.None);
        Assert.Equal(PortalOutcome.Success, cancelled.Outcome);

        var foreignAbsenceId = h.Db.Context.Absences.Single(a => a.EmployeeId == h.OtherEmployeeId).Id;
        var foreign = await h.Sut.CancelMyAbsenceAsync(foreignAbsenceId, CancellationToken.None);
        Assert.Equal(PortalOutcome.NotFound, foreign.Outcome);
    }

    [Fact]
    public async Task Absences_CancelOnlyPending()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var approved = new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = h.MeEmployeeId,
            Type = AbsenceType.Vacation, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 3),
            Status = AbsenceStatus.Approved,
        };
        h.Db.Context.Absences.Add(approved);
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut.CancelMyAbsenceAsync(approved.Id, CancellationToken.None);

        Assert.Equal(PortalOutcome.InvalidState, result.Outcome);
    }

    [Fact]
    public async Task Qualifications_OwnOnly_WithDocumentGuard()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var mine = await h.Sut.ListMyQualificationsAsync(CancellationToken.None);
        var single = Assert.Single(mine!);
        Assert.Equal("Code 95", single.QualificationTypeName);

        // A qualification of a colleague is unreachable through the portal.
        var foreignDoc = await h.Sut.OpenMyQualificationDocumentAsync(h.OtherQualificationId, CancellationToken.None);
        Assert.Null(foreignDoc);
    }

    [Fact]
    public async Task Dashboard_AggregatesOwnData()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        // One unread notification + one pending own request.
        var tenant = new DevTenantContext(h.TenantId);
        var notifications = new NotificationService(h.Db.Context, tenant, new DevCurrentUserContext(h.MeUserId), new TestClock(Now));
        await notifications.NotifyAsync(h.MeUserId, "test", "Titel", "Bericht", null, CancellationToken.None);
        await h.Sut.CreateMyAbsenceAsync(
            new TransportationService.Api.Modules.Hr.Dtos.CreateAbsenceRequest(
                AbsenceType.Vacation, new(2026, 9, 1), new(2026, 9, 5), null),
            CancellationToken.None);

        var dashboard = await h.Sut.GetMyDashboardAsync(CancellationToken.None);

        Assert.NotNull(dashboard);
        Assert.Equal("Jan", dashboard!.FirstName);
        Assert.Equal(1, dashboard.UnreadNotifications);
        Assert.Equal(1, dashboard.OpenAbsenceRequests);
        Assert.True(dashboard.IsDriver);
        Assert.Equal(1, dashboard.TripsToday);
        Assert.Equal(1, dashboard.ExpiringQualifications);
    }

    [Fact]
    public async Task ChangePassword_RequiresCorrectCurrent()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var wrong = await h.Sut.ChangeMyPasswordAsync(
            new ChangeMyPasswordRequest("Fout!", "NieuwWachtwoord1!"), CancellationToken.None);
        Assert.Equal(PortalOutcome.ValidationFailed, wrong.Outcome);

        var tooShort = await h.Sut.ChangeMyPasswordAsync(
            new ChangeMyPasswordRequest("OudWachtwoord1!", "kort"), CancellationToken.None);
        Assert.Equal(PortalOutcome.ValidationFailed, tooShort.Outcome);

        var changed = await h.Sut.ChangeMyPasswordAsync(
            new ChangeMyPasswordRequest("OudWachtwoord1!", "NieuwWachtwoord1!"), CancellationToken.None);
        Assert.Equal(PortalOutcome.Success, changed.Outcome);

        var hash = h.Db.Context.Users.Single(u => u.Id == h.MeUserId).PasswordHash;
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher().Verify(hash, "NieuwWachtwoord1!"));
    }
}
