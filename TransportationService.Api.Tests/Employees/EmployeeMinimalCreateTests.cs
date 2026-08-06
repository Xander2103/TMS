using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Master-data wave phase 8/9: only first + last name are required on an employee (spec §13),
/// e-mail is format-checked only when supplied, employment end may not precede start, and the
/// legacy Employee.Notes column is read-only (EmployeeNote records are the source of truth).
/// </summary>
public class EmployeeMinimalCreateTests
{
    private sealed record Harness(SqliteTestDbContext Db, EmployeeService Sut, Guid TenantId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "MED-", EmployeeNumberNextValue = 1 });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(null));
        var driverService = new Modules.Drivers.Services.DriverService(db.Context, tenant, audit,
            new Modules.Qualifications.Services.QualificationStatusCalculator(), TimeProvider.System);
        var qualificationService = new Modules.Qualifications.Services.QualificationService(
            db.Context, tenant, new Modules.Qualifications.Services.QualificationStatusCalculator(),
            TimeProvider.System, audit, new CountryCodeValidator(db.Context),
            new Modules.Qualifications.Services.LocalFileStorageService(
                Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var sut = new EmployeeService(db.Context, tenant, audit,
            new CountryCodeValidator(db.Context), driverService, qualificationService,
            new EmployeeCompletenessService(db.Context, tenant));
        return new Harness(db, sut, tenantId);
    }

    /// <summary>The absolute minimum the spec allows: nothing but a name (+ status enum).</summary>
    private static CreateEmployeeRequest MinimalRequest(string firstName = "Ann", string lastName = "Peeters") => new(
        firstName, lastName, null, null, null, null, null, null, null, null, EmploymentStatus.Active);

    private static UpdateEmployeeRequest MinimalUpdate(string firstName = "Ann", string lastName = "Peeters") => new(
        firstName, lastName, null, null, null, null, null, null, null, null, EmploymentStatus.Active);

    [Fact]
    public async Task Create_WithOnlyNames_Succeeds_GeneratesNumber_AndDetailReturnsNulls()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(MinimalRequest(), canEditConfidential: false, CancellationToken.None);

        Assert.Equal("MED-0001", created.EmployeeNumber);
        Assert.Equal("Ann", created.FirstName);
        Assert.Equal("Peeters", created.LastName);
        Assert.Null(created.DateOfBirth);
        Assert.Null(created.Street);
        Assert.Null(created.HouseNumber);
        Assert.Null(created.PostalCode);
        Assert.Null(created.City);
        Assert.Null(created.PhoneNumber);
        Assert.Null(created.Email);
        Assert.Null(created.EmploymentStartDate);
        Assert.True(created.IsActive);

        // Reload from a clean tracker: the persisted row round-trips identically.
        h.Db.Context.ChangeTracker.Clear();
        var reloaded = await h.Sut.GetByIdAsync(created.Id, includeConfidential: false, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded!.Email);
        Assert.Null(reloaded.EmploymentStartDate);

        var stored = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Null(stored.DateOfBirth);
        Assert.Null(stored.Email);
        Assert.Null(stored.PhoneNumber);
        Assert.Null(stored.Street);
        Assert.Null(stored.EmploymentStartDate);
    }

    [Fact]
    public async Task Create_WithoutNames_IsStillRefused()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(MinimalRequest(firstName: "  "), canEditConfidential: false, CancellationToken.None));

        Assert.Contains("Voornaam en achternaam", ex.Message);
    }

    [Fact]
    public async Task Create_WithInvalidEmailFormat_FailsWithEmailFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(MinimalRequest() with { Email = "geen-adres" }, canEditConfidential: false, CancellationToken.None));

        Assert.Contains("email", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_WithValidEmail_Passes_AndEmptyEmailPassesToo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var withEmail = await h.Sut.CreateAsync(MinimalRequest() with { Email = " ann@acme.example " },
            canEditConfidential: false, CancellationToken.None);
        Assert.Equal("ann@acme.example", withEmail.Email);

        // Whitespace-only counts as "not supplied" — no format error, stored as null.
        var withBlank = await h.Sut.CreateAsync(MinimalRequest(lastName: "Blanco") with { Email = "   " },
            canEditConfidential: false, CancellationToken.None);
        Assert.Null(withBlank.Email);
    }

    [Fact]
    public async Task Create_EndDateBeforeStartDate_FailsWithEmploymentEndDateFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(MinimalRequest() with
            {
                EmploymentStartDate = new DateOnly(2026, 5, 1),
                EmploymentEndDate = new DateOnly(2026, 4, 1),
            }, canEditConfidential: false, CancellationToken.None));

        Assert.Contains("employmentEndDate", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Update_EndDateBeforeStartDate_FailsWithEmploymentEndDateFieldError()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(MinimalRequest(), canEditConfidential: false, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.UpdateAsync(created.Id, MinimalUpdate() with
            {
                EmploymentStartDate = new DateOnly(2026, 5, 1),
                EmploymentEndDate = new DateOnly(2026, 4, 30),
            }, canEditConfidential: false, CancellationToken.None));

        Assert.Contains("employmentEndDate", ex.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Update_CanClearOptionalFields_BackToNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(MinimalRequest() with
        {
            Email = "ann@acme.example",
            PhoneNumber = "+3221234567",
            Street = "Kerkstraat",
            EmploymentStartDate = new DateOnly(2026, 1, 1),
        }, canEditConfidential: false, CancellationToken.None);
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Id, MinimalUpdate(), canEditConfidential: false, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Null(updated!.Email);
        Assert.Null(updated.PhoneNumber);
        Assert.Null(updated.Street);
        Assert.Null(updated.EmploymentStartDate);
    }

    [Fact]
    public async Task Create_WithNotes_LeavesLegacyColumnNull_AndCreatesNoEmployeeNoteRow()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var created = await h.Sut.CreateAsync(MinimalRequest() with { Notes = "Genegeerde notitie" },
            canEditConfidential: false, CancellationToken.None);

        Assert.Null(created.Notes);
        var stored = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Null(stored.Notes);
        // The value must not silently become an EmployeeNote record either.
        Assert.Equal(0, await h.Db.Context.EmployeeNotes.IgnoreQueryFilters()
            .CountAsync(n => n.EmployeeId == created.Id));
    }

    [Fact]
    public async Task Update_WithNotes_PreservesStoredLegacyValue()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(MinimalRequest(), canEditConfidential: false, CancellationToken.None);

        // Simulate a pre-migration legacy note that must survive profile edits untouched.
        var tracked = await h.Db.Context.Employees.SingleAsync(e => e.Id == created.Id);
        tracked.Notes = "Legacy notitie uit migratie";
        await h.Db.Context.SaveChangesAsync();
        h.Db.Context.ChangeTracker.Clear();

        var updated = await h.Sut.UpdateAsync(created.Id, MinimalUpdate() with { Notes = "Poging tot overschrijven" },
            canEditConfidential: false, CancellationToken.None);

        Assert.Equal("Legacy notitie uit migratie", updated!.Notes);
        var stored = await h.Db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == created.Id);
        Assert.Equal("Legacy notitie uit migratie", stored.Notes);
    }
}
