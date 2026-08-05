using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TransportationService.Api.Common;
using TransportationService.Api.Common.Reference;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Auditing.Entities;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Drivers.Entities;
using TransportationService.Api.Modules.Employees.Dtos;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Hr.Dtos;
using TransportationService.Api.Modules.Hr.Entities;
using TransportationService.Api.Modules.Hr.Services;
using TransportationService.Api.Modules.Identity.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Organization.Entities;
using TransportationService.Api.Modules.Qualifications.Dtos;
using TransportationService.Api.Modules.Qualifications.Entities;
using TransportationService.Api.Modules.Qualifications.Services;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Employees;

/// <summary>
/// Corrections wave §4: the personnel history is a complete, readable audit trail — field-level
/// before/after with Dutch labels, actor and timestamp, child entities included, confidential
/// values masked, no misleading empty "updated" entries, tenant-isolated.
/// </summary>
public class EmployeeHistoryTests
{
    private sealed record Harness(
        SqliteTestDbContext Db, EmployeeService Employees, EmployeeHistoryService History,
        QualificationService Qualifications, LeaveBalanceService LeaveBalances, EmployeeNoteService Notes,
        Guid TenantId, Guid UserId, Guid DepartmentId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.TenantSettings.Add(new TenantSettings { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNumberPrefix = "MED-", EmployeeNumberNextValue = 1 });
        db.Context.Departments.Add(new Department { Id = departmentId, TenantId = tenantId, Code = "PLAN", Name = "Planning", IsActive = true });
        db.Context.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "ann@acme.example", FirstName = "Ann", LastName = "HR", IsActive = true });
        await db.Context.SaveChangesAsync();
        await CountrySeeder.SyncAsync(db.Context);

        var tenant = new DevTenantContext(tenantId);
        var audit = new AuditService(db.Context, tenant, new DevCurrentUserContext(userId));
        var driverService = new Modules.Drivers.Services.DriverService(db.Context, tenant, audit,
            new QualificationStatusCalculator(), TimeProvider.System);
        var qualifications = new QualificationService(
            db.Context, tenant, new QualificationStatusCalculator(), TimeProvider.System, audit,
            new CountryCodeValidator(db.Context),
            new LocalFileStorageService(Path.Combine(Path.GetTempPath(), "ts-tests", Guid.NewGuid().ToString("N"))));
        var employees = new EmployeeService(db.Context, tenant, audit,
            new CountryCodeValidator(db.Context), driverService, qualifications);
        var history = new EmployeeHistoryService(db.Context, tenant);
        var leaveBalances = new LeaveBalanceService(db.Context, tenant, audit);
        var notes = new EmployeeNoteService(db.Context, tenant, audit, new DevCurrentUserContext(userId), TimeProvider.System);
        return new Harness(db, employees, history, qualifications, leaveBalances, notes, tenantId, userId, departmentId);
    }

    private static CreateEmployeeRequest CreateRequest(string? notes = null) => new(
        "Jan", "Janssen", new DateOnly(1990, 5, 1),
        "Oude straat", "10", "1000", "Brussel",
        "0470 12 34 56", "jan@acme.example", new DateOnly(2020, 1, 1),
        EmploymentStatus.Active, CountryCode: "BE", Notes: notes);

    private static UpdateEmployeeRequest UpdateRequest(
        string phone = "0470 12 34 56", string street = "Oude straat",
        EmploymentStatus status = EmploymentStatus.Active, string? notes = null,
        string? iban = null) => new(
        "Jan", "Janssen", new DateOnly(1990, 5, 1),
        street, "10", "1000", "Brussel",
        phone, "jan@acme.example", new DateOnly(2020, 1, 1),
        status, CountryCode: "BE", Notes: notes, Iban: iban);

    [Fact]
    public async Task CreateWithLegacyNotesInput_IgnoresIt_EmployeeNoteRecordsAreTheSourceOfTruth()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest("Belangrijke afspraak"), false, CancellationToken.None);

        // The legacy Employee.Notes column is read-only historical data: the request value is
        // accepted (older clients) but never written, and no EmployeeNote row appears silently.
        Assert.Null(created.Notes);
        var detail = await h.Employees.GetByIdAsync(created.Id, false, CancellationToken.None);
        Assert.Null(detail!.Notes);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var entry = Assert.Single(history!.Items, e => e.Action == "Created");
        Assert.Equal("Profiel", entry.Category);
        Assert.DoesNotContain(entry.Changes, c => c.Field == "Notities");
    }

    [Fact]
    public async Task SingleFieldChange_ShowsLabelBeforeAfterActorAndTimestamp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(phone: "0485 98 76 54"), false, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var entry = history!.Items.First();
        Assert.Equal("Updated", entry.Action);
        Assert.Equal("Gewijzigd", entry.ActionLabel);
        Assert.Equal("Ann HR", entry.UserName);
        Assert.True(entry.Timestamp > DateTime.UtcNow.AddMinutes(-5));
        var change = Assert.Single(entry.Changes);
        Assert.Equal("Telefoonnummer", change.Field);
        Assert.Equal("0470 12 34 56", change.Before);
        Assert.Equal("0485 98 76 54", change.After);
        Assert.Equal("Telefoonnummer: 0470 12 34 56 → 0485 98 76 54", entry.Summary);
    }

    [Fact]
    public async Task MultipleChanges_InOneSave_GroupIntoOneReadableEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        // request.Notes is accepted-but-ignored, so it must never show up as a change.
        await h.Employees.UpdateAsync(created.Id, UpdateRequest(
            street: "Nieuwe straat", status: EmploymentStatus.OnLeave, notes: "Nieuwe notitie"), false, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var entry = history!.Items.First();
        Assert.Equal(2, entry.Changes.Count);
        Assert.Contains(entry.Changes, c => c.Field == "Straat" && c.Before == "Oude straat" && c.After == "Nieuwe straat");
        Assert.Contains(entry.Changes, c => c.Field == "Status tewerkstelling" && c.Before == "Actief" && c.After == "Met verlof");
        Assert.Equal("2 velden gewijzigd (Straat, Status tewerkstelling)", entry.Summary);
    }

    [Fact]
    public async Task NoopSave_NeverProducesAMisleadingEntry()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        var before = (await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None))!.TotalCount;

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(), false, CancellationToken.None);

        var after = (await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None))!.TotalCount;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ConfidentialChange_IsVisibleButMasked()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), true, CancellationToken.None);

        await h.Employees.UpdateAsync(created.Id, UpdateRequest(iban: "BE68 5390 0754 7034"), true, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var change = Assert.Single(history!.Items.First().Changes);
        Assert.Equal("IBAN", change.Field);
        Assert.StartsWith("•••", change.After);
        Assert.DoesNotContain("BE68539007547034", change.After);
        Assert.DoesNotContain("5390", change.After!);
    }

    [Fact]
    public async Task QualificationChanges_AppearAsKwalificatiesEntries()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        var type = new QualificationType { Id = Guid.NewGuid(), Code = "ADR", Name = "ADR-attest", IsActive = true };
        h.Db.Context.QualificationTypes.Add(type);
        await h.Db.Context.SaveChangesAsync();

        var qualification = await h.Qualifications.CreateAsync(created.Id, new CreateEmployeeQualificationRequest(
            type.Id, null, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), null, null), CancellationToken.None);
        await h.Qualifications.UpdateAsync(qualification.Id, new UpdateEmployeeQualificationRequest(
            null, new DateOnly(2024, 1, 1), new DateOnly(2027, 1, 1), null, null), CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var updateEntry = history!.Items.First(e => e.Category == "Kwalificaties" && e.Action == "Updated");
        Assert.Contains(updateEntry.Changes, c => c.Field == "Vervaldatum" && c.Before == "01-01-2026" && c.After == "01-01-2027");
    }

    [Fact]
    public async Task NoteActions_AppearAsNotitiesEntries_IncludingAfterSoftDelete()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        var note = await h.Notes.CreateAsync(created.Id, "Heeft hoogtevrees", CancellationToken.None);
        await h.Notes.UpdateAsync(created.Id, note!.Id, "Heeft hoogtevrees — nooit op kraanwerk", CancellationToken.None);
        await h.Notes.SetPinnedAsync(created.Id, note.Id, true, CancellationToken.None);
        await h.Notes.SetPinnedAsync(created.Id, note.Id, false, CancellationToken.None);
        await h.Notes.DeleteAsync(created.Id, note.Id, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, "Notities", EmployeeHistoryAccess.Full, CancellationToken.None);
        Assert.Equal(5, history!.TotalCount);
        Assert.All(history.Items, e => Assert.Equal("Notities", e.Category));
        Assert.Contains(history.Items, e => e.Action == "Created");
        var updated = history.Items.Single(e => e.Action == "Updated");
        Assert.Contains(updated.Changes, c => c.Field == "Tekst" && c.Before == "Heeft hoogtevrees" && c.After == "Heeft hoogtevrees — nooit op kraanwerk");
        Assert.Contains(history.Items, e => e.Action == "Pinned" && e.Summary == "Toegevoegd aan startscherm");
        Assert.Contains(history.Items, e => e.Action == "Unpinned" && e.Summary == "Verwijderd van startscherm");
        Assert.Contains(history.Items, e => e.Action == "Deleted");
    }

    [Fact]
    public async Task LeaveEntitlement_ShowsCategoryYearBeforeAfterDifferenceAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        await h.LeaveBalances.EnsureSeededAsync(CancellationToken.None);
        var balanceType = await h.Db.Context.LeaveBalanceTypes.FirstAsync(t => t.TenantId == h.TenantId && t.Code == "WETTELIJK");

        await h.LeaveBalances.SetEntitlementAsync(created.Id, 2027, new SetLeaveEntitlementRequest(
            balanceType.Id, 12m, 0m), CancellationToken.None);
        await h.LeaveBalances.SetEntitlementAsync(created.Id, 2027, new SetLeaveEntitlementRequest(
            balanceType.Id, 20m, 0m, Reason: "Jaarlijks saldo 2027 toegekend"), CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var entry = history!.Items.First(e => e.Category == "Verlofsaldo" && e.Action == "Updated");
        Assert.Equal("Ann HR", entry.UserName);
        Assert.Contains(entry.Changes, c => c.Field == "Basisrecht (dagen)" && c.Before == "12" && c.After == "20");
        Assert.Contains(entry.Changes, c => c.Field == "Verschil" && c.After == "8");
        Assert.Contains(entry.Changes, c => c.Field == "Reden" && c.After == "Jaarlijks saldo 2027 toegekend");
        Assert.Equal("Wettelijk verlof 2027: 12 → 20 dagen", entry.Summary);
    }

    [Fact]
    public async Task History_IsTenantIsolated()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        var foreignHistory = new EmployeeHistoryService(h.Db.Context, new DevTenantContext(Guid.NewGuid()));
        Assert.Null(await foreignHistory.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None));
    }

    /// <summary>No 8-4-4-4-12 hex pattern anywhere — a resolved id, a formatted date, or a masked value.</summary>
    private static readonly Regex RawGuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    private static void AssertNoRawGuidAnywhere(EmployeeHistoryPageDto history)
    {
        foreach (var entry in history.Items)
        {
            Assert.False(RawGuidPattern.IsMatch(entry.Summary), $"Summary leaked a raw id: {entry.Summary}");
            foreach (var change in entry.Changes)
            {
                Assert.False(change.Before is { } before && RawGuidPattern.IsMatch(before), $"Before leaked a raw id: {change.Before}");
                Assert.False(change.After is { } after && RawGuidPattern.IsMatch(after), $"After leaked a raw id: {change.After}");
            }
        }
    }

    [Fact]
    public async Task QualificationTypeId_AndVerifiedByUserId_ResolveToNames_NotRawGuids()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        var type = new QualificationType { Id = Guid.NewGuid(), Code = "ADR", Name = "ADR-attest", IsActive = true };
        h.Db.Context.QualificationTypes.Add(type);
        await h.Db.Context.SaveChangesAsync();

        var qualification = await h.Qualifications.CreateAsync(created.Id, new CreateEmployeeQualificationRequest(
            type.Id, null, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), null, null), CancellationToken.None);
        await h.Qualifications.VerifyAsync(qualification.Id, h.UserId, CancellationToken.None);

        var history = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);

        var createdEntry = history!.Items.First(e => e.Category == "Kwalificaties" && e.Action == "Created");
        Assert.Contains(createdEntry.Changes, c => c.Field == "Kwalificatietype" && c.After == "ADR-attest");

        var verifiedEntry = history.Items.First(e => e.Category == "Kwalificaties" && e.Action == "Verified");
        Assert.Contains(verifiedEntry.Changes, c => c.Field == "Geverifieerd door" && c.After == "Ann HR");

        AssertNoRawGuidAnywhere(history);
    }

    [Fact]
    public async Task LegacyIdFields_OnAbsenceAndLeaveBalance_ResolveToNames()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        var leaveType = new LeaveType { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "ZIEK", Name = "Ziekteverlof", IsActive = true };
        var leaveBalanceType = new LeaveBalanceType { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "ADV", Name = "ADV-dagen", IsActive = true };
        h.Db.Context.LeaveTypes.Add(leaveType);
        h.Db.Context.LeaveBalanceTypes.Add(leaveBalanceType);
        var absence = new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = created.Id, Type = AbsenceType.Sick,
            LeaveTypeId = leaveType.Id, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 2),
            Status = AbsenceStatus.Approved, DecidedByUserId = h.UserId,
        };
        var balanceRow = new EmployeeLeaveBalance
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = created.Id, CalendarYear = 2027,
            BalanceTypeId = leaveBalanceType.Id, BaseEntitlementDays = 5m, CarryOverDays = 0m,
        };
        h.Db.Context.Absences.Add(absence);
        h.Db.Context.EmployeeLeaveBalances.Add(balanceRow);
        await h.Db.Context.SaveChangesAsync();

        // These payload shapes ("LeaveTypeId"/"DecidedByUserId"/"BalanceTypeId" as raw ids) predate
        // the write-time name-resolution the corrections wave added elsewhere — legacy rows must
        // still resolve, driven purely by whichever ids the stored JSON happens to contain.
        h.Db.Context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.UserId, EntityType = "Absence",
            EntityId = absence.Id.ToString(), Action = "Approved", Timestamp = DateTime.UtcNow,
            NewValuesJson = JsonSerializer.Serialize(new { LeaveTypeId = leaveType.Id, DecidedByUserId = h.UserId, DepartmentId = h.DepartmentId }),
        });
        h.Db.Context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.UserId, EntityType = "EmployeeLeaveBalance",
            EntityId = balanceRow.Id.ToString(), Action = "Created", Timestamp = DateTime.UtcNow,
            NewValuesJson = JsonSerializer.Serialize(new { BalanceTypeId = leaveBalanceType.Id, EmployeeId = created.Id }),
        });
        await h.Db.Context.SaveChangesAsync();

        var history = await h.History.GetHistoryAsync(created.Id, 1, 50, null, EmployeeHistoryAccess.Full, CancellationToken.None);

        var absenceEntry = history!.Items.First(e => e.Category == "Afwezigheden" && e.Action == "Approved");
        Assert.Contains(absenceEntry.Changes, c => c.Field == "Verloftype" && c.After == "Ziekteverlof");
        Assert.Contains(absenceEntry.Changes, c => c.Field == "Beslist door" && c.After == "Ann HR");
        Assert.Contains(absenceEntry.Changes, c => c.Field == "Afdeling" && c.After == "Planning");

        var balanceEntry = history.Items.First(e => e.Category == "Verlofsaldo" && e.Action == "Created");
        Assert.Contains(balanceEntry.Changes, c => c.Field == "Saldotype" && c.After == "ADV-dagen");

        AssertNoRawGuidAnywhere(history);
    }

    [Fact]
    public async Task EnumValues_TranslateToDutchLabels_ForIssuedItemsDocumentsAndDriverProfile()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        var issuedItem = new EmployeeIssuedItem { Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = created.Id, NameSnapshot = "Werkjas", Status = IssuedItemStatus.Issued };
        var document = new EmployeeDocument { Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = created.Id, Category = EmployeeDocumentCategory.MedicalDocument, FileName = "keuring.pdf", StorageKey = "key-1" };
        var driver = new Driver { Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = created.Id, DriverNumber = "D-1", AvailabilityStatus = DriverAvailabilityStatus.OnTrip };
        h.Db.Context.EmployeeIssuedItems.Add(issuedItem);
        h.Db.Context.EmployeeDocuments.Add(document);
        h.Db.Context.Drivers.Add(driver);
        await h.Db.Context.SaveChangesAsync();

        h.Db.Context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.UserId, EntityType = "EmployeeIssuedItem",
            EntityId = issuedItem.Id.ToString(), Action = "StatusChanged", Timestamp = DateTime.UtcNow,
            OldValuesJson = JsonSerializer.Serialize(new { Status = "NotIssued" }),
            NewValuesJson = JsonSerializer.Serialize(new { Status = "Issued" }),
        });
        h.Db.Context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.UserId, EntityType = "EmployeeDocument",
            EntityId = document.Id.ToString(), Action = "Uploaded", Timestamp = DateTime.UtcNow,
            NewValuesJson = JsonSerializer.Serialize(new { Category = "MedicalDocument", document.FileName }),
        });
        h.Db.Context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.UserId, EntityType = "Driver",
            EntityId = driver.Id.ToString(), Action = "StatusChanged", Timestamp = DateTime.UtcNow,
            OldValuesJson = JsonSerializer.Serialize(new { AvailabilityStatus = "Available" }),
            NewValuesJson = JsonSerializer.Serialize(new { AvailabilityStatus = "OnTrip" }),
        });
        await h.Db.Context.SaveChangesAsync();

        var history = await h.History.GetHistoryAsync(created.Id, 1, 50, null, EmployeeHistoryAccess.Full, CancellationToken.None);

        var itemEntry = history!.Items.First(e => e.Category == "Bedrijfsmiddelen");
        Assert.Contains(itemEntry.Changes, c => c.Field == "Status" && c.Before == "Niet uitgereikt" && c.After == "Uitgereikt");

        var docEntry = history.Items.First(e => e.Category == "Documenten");
        Assert.Contains(docEntry.Changes, c => c.Field == "Categorie" && c.After == "Medisch document");

        var driverEntry = history.Items.First(e => e.Category == "Chauffeursprofiel");
        Assert.Contains(driverEntry.Changes, c => c.Field == "Beschikbaarheid" && c.Before == "Beschikbaar" && c.After == "Onderweg");
    }

    [Fact]
    public async Task UnknownLookupId_FallsBackToOnbekend_ButSoftDeletedLookupStillResolvesByName()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);

        var leaveType = new LeaveType { Id = Guid.NewGuid(), TenantId = h.TenantId, Code = "KREDIET", Name = "Tijdskrediet", IsActive = true };
        h.Db.Context.LeaveTypes.Add(leaveType);
        var absence = new Absence
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, EmployeeId = created.Id, Type = AbsenceType.Other,
            LeaveTypeId = leaveType.Id, StartDate = new DateOnly(2027, 5, 1), EndDate = new DateOnly(2027, 5, 1),
        };
        h.Db.Context.Absences.Add(absence);
        await h.Db.Context.SaveChangesAsync();

        // Soft-delete the leave type — its row still exists (IsDeleted flag), so its name must
        // still resolve. A never-existing id, by contrast, must fall back to the placeholder.
        h.Db.Context.LeaveTypes.Remove(leaveType);
        await h.Db.Context.SaveChangesAsync();

        var neverExistedId = Guid.NewGuid();
        h.Db.Context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, UserId = h.UserId, EntityType = "Absence",
            EntityId = absence.Id.ToString(), Action = "Updated", Timestamp = DateTime.UtcNow,
            OldValuesJson = JsonSerializer.Serialize(new { LeaveTypeId = neverExistedId }),
            NewValuesJson = JsonSerializer.Serialize(new { LeaveTypeId = leaveType.Id }),
        });
        await h.Db.Context.SaveChangesAsync();

        var history = await h.History.GetHistoryAsync(created.Id, 1, 50, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        var entry = history!.Items.First(e => e.Action == "Updated" && e.Category == "Afwezigheden");
        var change = Assert.Single(entry.Changes);
        Assert.Equal("Verloftype", change.Field);
        Assert.Equal("Onbekend (verwijderd)", change.Before);
        Assert.Equal("Tijdskrediet", change.After);
    }

    [Fact]
    public async Task CategoryFilter_ReturnsOnlyMatchingEntries_AndRejectsUnknownCategory()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Employees.CreateAsync(CreateRequest(), false, CancellationToken.None);
        var type = new QualificationType { Id = Guid.NewGuid(), Code = "ADR", Name = "ADR-attest", IsActive = true };
        h.Db.Context.QualificationTypes.Add(type);
        await h.Db.Context.SaveChangesAsync();
        await h.Qualifications.CreateAsync(created.Id, new CreateEmployeeQualificationRequest(
            type.Id, null, new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), null, null), CancellationToken.None);

        var filtered = await h.History.GetHistoryAsync(created.Id, 1, 25, "Kwalificaties", EmployeeHistoryAccess.Full, CancellationToken.None);
        Assert.NotEmpty(filtered!.Items);
        Assert.All(filtered.Items, e => Assert.Equal("Kwalificaties", e.Category));

        var unfiltered = await h.History.GetHistoryAsync(created.Id, 1, 25, null, EmployeeHistoryAccess.Full, CancellationToken.None);
        Assert.True(unfiltered!.TotalCount > filtered.TotalCount);

        await Assert.ThrowsAsync<DomainValidationException>(
            () => h.History.GetHistoryAsync(created.Id, 1, 25, "GeenBestaandeCategorie", EmployeeHistoryAccess.Full, CancellationToken.None));
    }
}
