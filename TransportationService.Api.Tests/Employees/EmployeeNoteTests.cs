using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Corrections wave §4: multiple free-text notes per employee, replacing the legacy single
/// Employee.Notes field, each individually pinnable to the company dashboard.
/// </summary>
public class EmployeeNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, EmployeeNoteService Sut, Guid TenantId, Guid EmployeeId, Guid UserId, TestClock Clock);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
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
        var clock = new TestClock(Now);
        var sut = new EmployeeNoteService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(userId)), new DevCurrentUserContext(userId), clock);
        return new Harness(db, sut, tenantId, employeeId, userId, clock);
    }

    [Fact]
    public async Task Create_ThenList_ReturnsNewestFirst()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var first = await h.Sut.CreateAsync(h.EmployeeId, "Eerste notitie", CancellationToken.None);
        await Task.Delay(5);
        var second = await h.Sut.CreateAsync(h.EmployeeId, "Tweede notitie", CancellationToken.None);

        var list = await h.Sut.ListAsync(h.EmployeeId, CancellationToken.None);

        Assert.Equal(2, list!.Count);
        Assert.Equal(second!.Id, list[0].Id);
        Assert.Equal(first!.Id, list[1].Id);
    }

    [Fact]
    public async Task Create_Trims_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var note = await h.Sut.CreateAsync(h.EmployeeId, "  Met spaties  ", CancellationToken.None);

        Assert.Equal("Met spaties", note!.Text);
        Assert.False(note.IsPinnedToDashboard);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a =>
            a.EntityType == "EmployeeNote" && a.EntityId == note.Id.ToString() && a.Action == "Created"));
    }

    [Fact]
    public async Task Create_RejectsBlankText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(h.EmployeeId, "   ", CancellationToken.None));
    }

    [Fact]
    public async Task Create_RejectsTooLongText()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.Sut.CreateAsync(h.EmployeeId, new string('x', 4001), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ChangesText_AndAuditsBeforeAfter()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var note = await h.Sut.CreateAsync(h.EmployeeId, "Origineel", CancellationToken.None);

        var updated = await h.Sut.UpdateAsync(h.EmployeeId, note!.Id, "Bijgewerkt", CancellationToken.None);

        Assert.Equal("Bijgewerkt", updated!.Text);
        var log = await h.Db.Context.AuditLogs.SingleAsync(a =>
            a.EntityType == "EmployeeNote" && a.EntityId == note.Id.ToString() && a.Action == "Updated");
        Assert.Contains("Origineel", log.OldValuesJson);
        Assert.Contains("Bijgewerkt", log.NewValuesJson);
    }

    [Fact]
    public async Task Delete_SoftDeletes_HidesFromList_AndAudits()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var note = await h.Sut.CreateAsync(h.EmployeeId, "Weg ermee", CancellationToken.None);

        Assert.True(await h.Sut.DeleteAsync(h.EmployeeId, note!.Id, CancellationToken.None));

        Assert.Empty((await h.Sut.ListAsync(h.EmployeeId, CancellationToken.None))!);
        var stored = await h.Db.Context.EmployeeNotes.IgnoreQueryFilters().SingleAsync(n => n.Id == note.Id);
        Assert.True(stored.IsDeleted);
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a =>
            a.EntityType == "EmployeeNote" && a.EntityId == note.Id.ToString() && a.Action == "Deleted"));
    }

    [Fact]
    public async Task Pin_ThenUnpin_TogglesFlag_AndAuditsBothActions()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var note = await h.Sut.CreateAsync(h.EmployeeId, "Belangrijk", CancellationToken.None);

        var pinned = await h.Sut.SetPinnedAsync(h.EmployeeId, note!.Id, true, CancellationToken.None);
        Assert.True(pinned!.IsPinnedToDashboard);

        var unpinned = await h.Sut.SetPinnedAsync(h.EmployeeId, note.Id, false, CancellationToken.None);
        Assert.False(unpinned!.IsPinnedToDashboard);

        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "EmployeeNote" && a.EntityId == note.Id.ToString() && a.Action == "Pinned"));
        Assert.True(await h.Db.Context.AuditLogs.AnyAsync(a => a.EntityType == "EmployeeNote" && a.EntityId == note.Id.ToString() && a.Action == "Unpinned"));
    }

    [Fact]
    public async Task Pin_SetsPinnedAtAndPinnedByUser_AndUnpinClearsBoth()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var note = await h.Sut.CreateAsync(h.EmployeeId, "Belangrijk", CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(5));

        var pinned = await h.Sut.SetPinnedAsync(h.EmployeeId, note!.Id, true, CancellationToken.None);
        Assert.Equal(Now.AddMinutes(5).UtcDateTime, pinned!.PinnedAt);
        Assert.Equal(h.UserId, pinned.PinnedByUserId);
        // PinnedAt must be distinct from (later than) CreatedAt — an old note pinned later must
        // be attributed to the pin action, not the original write.
        Assert.True(pinned.PinnedAt > pinned.CreatedAt);

        var unpinned = await h.Sut.SetPinnedAsync(h.EmployeeId, note.Id, false, CancellationToken.None);
        Assert.Null(unpinned!.PinnedAt);
        Assert.Null(unpinned.PinnedByUserId);
    }

    [Fact]
    public async Task UnknownEmployee_ReturnsNull()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        Assert.Null(await h.Sut.ListAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await h.Sut.CreateAsync(Guid.NewGuid(), "Tekst", CancellationToken.None));
    }

    [Fact]
    public async Task UnknownNote_ReturnsNullOrFalse()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var missingId = Guid.NewGuid();
        Assert.Null(await h.Sut.UpdateAsync(h.EmployeeId, missingId, "x", CancellationToken.None));
        Assert.False(await h.Sut.DeleteAsync(h.EmployeeId, missingId, CancellationToken.None));
        Assert.Null(await h.Sut.SetPinnedAsync(h.EmployeeId, missingId, true, CancellationToken.None));
    }

    [Fact]
    public async Task TenantIsolation_OtherTenantCannotSeeOrActOnNotes()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var note = await h.Sut.CreateAsync(h.EmployeeId, "Geheim", CancellationToken.None);

        var otherTenant = new DevTenantContext(Guid.NewGuid());
        var foreign = new EmployeeNoteService(h.Db.Context, otherTenant,
            new AuditService(h.Db.Context, otherTenant, new DevCurrentUserContext(null)), new DevCurrentUserContext(null), TimeProvider.System);

        Assert.Null(await foreign.ListAsync(h.EmployeeId, CancellationToken.None));
        Assert.Null(await foreign.UpdateAsync(h.EmployeeId, note!.Id, "Overschrijven", CancellationToken.None));
        Assert.False(await foreign.DeleteAsync(h.EmployeeId, note.Id, CancellationToken.None));
    }
}
