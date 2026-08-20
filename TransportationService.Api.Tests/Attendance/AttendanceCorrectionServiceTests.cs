using TransportationService.Api.Modules.Attendance.Dtos;
using TransportationService.Api.Modules.Attendance.Entities;
using TransportationService.Api.Modules.Attendance.Services;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees.Entities;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Attendance;

/// <summary>
/// Correcties: verplichte reden, oude waarde altijd bewaard (correctierij + event +
/// auditlog), annuleren in plaats van verwijderen, manuele sessies, validaties
/// (chronologie, pauzes binnen sessie, geen overlap), Version-token en tenant-isolatie.
/// </summary>
public class AttendanceCorrectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    private sealed record Harness(SqliteTestDbContext Db, Guid TenantId, Guid EmployeeId, TestClock Clock)
    {
        public AttendanceCorrectionService Sut(Guid? tenantOverride = null)
        {
            var tenant = new DevTenantContext(tenantOverride ?? TenantId);
            return new AttendanceCorrectionService(Db.Context, tenant,
                new AuditService(Db.Context, tenant, new DevCurrentUserContext(null)), Clock);
        }

        public AttendanceService Attendance() => new(Db.Context, new DevTenantContext(TenantId), Clock);
    }

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, EmployeeNumber = "E-1",
            FirstName = "Jan", LastName = "Peeters", IsActive = true,
        });
        await db.Context.SaveChangesAsync();
        return new Harness(db, tenantId, employeeId, new TestClock(Now));
    }

    /// <summary>Volledige sessie 08:00–16:00 UTC met pauze 12:00–12:30, via de echte punchservice.</summary>
    private static async Task<AttendanceSession> SeedCompletedSessionAsync(Harness h)
    {
        var attendance = h.Attendance();
        var start = new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero);
        h.Clock.Advance(start - Now);
        await attendance.ClockInAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(4));
        await attendance.StartBreakAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await attendance.EndBreakAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(3.5));
        await attendance.ClockOutAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(2)); // correcties gebeuren later op de dag
        return h.Db.Context.AttendanceSessions.Single();
    }

    [Fact]
    public async Task Correction_RequiresReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);

        var result = await h.Sut().CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, session.ClockOutAt!.Value.AddMinutes(9), "  ", session.Version), CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task CorrectClockOut_KeepsOriginalTraceable_InCorrectionEventAndAudit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);
        var originalClockOut = session.ClockOutAt!.Value;
        var corrected = originalClockOut.AddMinutes(22);

        var result = await h.Sut().CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, corrected, "Medewerker vergat uit te punten.", session.Version),
            CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.Success, result.Outcome);
        Assert.Equal(corrected, result.Session!.ClockOutAt);
        Assert.True(result.Session.HasCorrections);

        var correction = h.Db.Context.AttendanceCorrections.Single();
        Assert.Equal(AttendanceCorrectionKind.ClockOut, correction.Kind);
        Assert.Equal(originalClockOut, correction.OldValue);
        Assert.Equal(corrected, correction.NewValue);
        Assert.Equal("Medewerker vergat uit te punten.", correction.Reason);

        var evt = h.Db.Context.AttendanceEvents.Single(e => e.EventType == AttendanceEventType.ManualCorrection);
        Assert.Equal(correction.Id, evt.CorrectionId);

        Assert.Contains(h.Db.Context.AuditLogs, a => a.EntityType == "AttendanceSession" && a.Action == "Corrected");
    }

    [Fact]
    public async Task CorrectSession_ValidatesChronology_Future_AndBreaksWithinBounds()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);
        var sut = h.Sut();

        var reversed = await sut.CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(session.ClockOutAt!.Value.AddHours(1), null, "Fout.", session.Version), CancellationToken.None);
        Assert.Equal(AttendanceCorrectionOutcome.ValidationFailed, reversed.Outcome);

        var future = await sut.CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, h.Clock.GetUtcNow().UtcDateTime.AddHours(2), "Fout.", session.Version), CancellationToken.None);
        Assert.Equal(AttendanceCorrectionOutcome.ValidationFailed, future.Outcome);

        // Uitpunt vóór de pauze leggen ⇒ pauze valt buiten de sessie.
        var beforeBreak = await sut.CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, session.ClockInAt.AddHours(3), "Fout.", session.Version), CancellationToken.None);
        Assert.Equal(AttendanceCorrectionOutcome.ValidationFailed, beforeBreak.Outcome);
    }

    [Fact]
    public async Task CorrectSession_StaleVersion_IsRejected()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);

        var result = await h.Sut().CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, session.ClockOutAt!.Value.AddMinutes(5), "Reden.", Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.StaleVersion, result.Outcome);
    }

    [Fact]
    public async Task CorrectBreak_AdjustsTimes_WithAudit()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);
        var brk = h.Db.Context.AttendanceBreaks.Single();
        var newEnd = brk.EndedAt!.Value.AddMinutes(15);

        var result = await h.Sut().CorrectBreakAsync(session.Id, brk.Id,
            new CorrectBreakRequest(null, newEnd, "Pauze duurde langer.", session.Version), CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.Success, result.Outcome);
        Assert.Equal(newEnd, h.Db.Context.AttendanceBreaks.Single().EndedAt);
        Assert.Single(h.Db.Context.AttendanceCorrections, c => c.Kind == AttendanceCorrectionKind.BreakEnd);

        var outside = await h.Sut().CorrectBreakAsync(session.Id, brk.Id,
            new CorrectBreakRequest(session.ClockInAt.AddHours(-1), null, "Fout.", null), CancellationToken.None);
        Assert.Equal(AttendanceCorrectionOutcome.ValidationFailed, outside.Outcome);
    }

    [Fact]
    public async Task CancelSession_MarksCancelled_InsteadOfDeleting()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);

        var result = await h.Sut().CancelSessionAsync(session.Id,
            new CancelSessionRequest("Dubbel geregistreerd.", session.Version), CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.Success, result.Outcome);
        var stored = h.Db.Context.AttendanceSessions.Single();
        Assert.Equal(AttendanceSessionStatus.Cancelled, stored.Status);
        Assert.False(stored.IsDeleted); // annuleren, niet verwijderen
        Assert.Contains(h.Db.Context.AttendanceEvents, e => e.EventType == AttendanceEventType.SessionCancelled);

        // Geannuleerde sessies tellen niet meer mee in de historie.
        var history = await h.Attendance().GetHistoryAsync(
            h.EmployeeId, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 20), CancellationToken.None);
        Assert.Equal(0, history.TotalNetMinutes);
    }

    [Fact]
    public async Task ManualSession_CreatesCompletedSessionWithBreaks_AndRejectsOverlap()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var existing = await SeedCompletedSessionAsync(h);
        var sut = h.Sut();

        // Overlap met bestaande registratie wordt geweigerd.
        var overlapping = await sut.CreateManualSessionAsync(new CreateManualSessionRequest(
            h.EmployeeId, existing.ClockInAt.AddHours(1), existing.ClockOutAt!.Value.AddHours(1), null, "Vergeten."),
            CancellationToken.None);
        Assert.Equal(AttendanceCorrectionOutcome.OverlapsOtherSession, overlapping.Outcome);

        // Gisteren: geldig, met pauze.
        var start = new DateTime(2026, 8, 19, 6, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 8, 19, 14, 0, 0, DateTimeKind.Utc);
        var result = await sut.CreateManualSessionAsync(new CreateManualSessionRequest(
            h.EmployeeId, start, end,
            [new ManualBreakRequest(start.AddHours(4), start.AddHours(4).AddMinutes(30))],
            "Prikklok was defect."), CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.Success, result.Outcome);
        Assert.Equal(450, result.Session!.NetMinutes);
        Assert.Equal(AttendanceSource.Manual, result.Session.ClockInSource);
        Assert.Contains(h.Db.Context.AttendanceEvents, e => e.EventType == AttendanceEventType.ManualSessionCreated);
        Assert.Contains(h.Db.Context.AuditLogs, a => a.Action == "ManualCreated");

        // Pauze buiten de sessie wordt geweigerd.
        var invalidBreak = await sut.CreateManualSessionAsync(new CreateManualSessionRequest(
            h.EmployeeId, start.AddDays(-1), end.AddDays(-1),
            [new ManualBreakRequest(start.AddDays(-1).AddHours(-1), start.AddDays(-1))],
            "Test."), CancellationToken.None);
        Assert.Equal(AttendanceCorrectionOutcome.ValidationFailed, invalidBreak.Outcome);
    }

    [Fact]
    public async Task CrossTenant_SessionIsInvisible()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var session = await SeedCompletedSessionAsync(h);
        var otherTenant = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        await h.Db.Context.SaveChangesAsync();

        var result = await h.Sut(otherTenant).CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, session.ClockOutAt!.Value.AddMinutes(1), "Poging.", null), CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task CorrectClockOut_OnActiveSessionWithOpenBreak_ClosesBreakTraceably()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var attendance = h.Attendance();
        await attendance.ClockInAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(4));
        await attendance.StartBreakAsync(h.EmployeeId, new AttendancePunchContext(AttendanceSource.Web), CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromHours(20)); // vergeten uit te punten, pauze bleef openstaan
        var session = h.Db.Context.AttendanceSessions.Single();
        var clockOut = session.ClockInAt.AddHours(8);

        var result = await h.Sut().CorrectSessionAsync(session.Id,
            new CorrectSessionRequest(null, clockOut, "Vergeten uit te punten.", session.Version), CancellationToken.None);

        Assert.Equal(AttendanceCorrectionOutcome.Success, result.Outcome);
        var stored = h.Db.Context.AttendanceSessions.Single();
        Assert.Equal(AttendanceSessionStatus.Completed, stored.Status);
        Assert.Equal(clockOut, h.Db.Context.AttendanceBreaks.Single().EndedAt);
        Assert.Equal(2, h.Db.Context.AttendanceCorrections.Count()); // ClockOut + BreakEnd
    }
}
