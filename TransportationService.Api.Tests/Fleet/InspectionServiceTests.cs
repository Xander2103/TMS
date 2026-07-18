using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class InspectionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    private sealed record Harness(SqliteTestDbContext Db, InspectionService Sut, Guid TenantId, Guid VehicleId, Guid TrailerId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var trailerId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        db.Context.Trailers.Add(new Trailer { Id = trailerId, TenantId = tenantId, InternalNumber = "OPL-0001", LicensePlate = "O-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new InspectionService(db.Context, tenant,
            new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId, trailerId);
    }

    [Fact]
    public async Task Create_CraneInspection_DefaultsToThreeMonthInterval()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, new CreateInspectionRequest(
            InspectionType.CraneInspection, null, new DateOnly(2026, 8, 1), IntervalMonths: null, null, null), CancellationToken.None);

        Assert.Equal(InspectionOperationOutcome.Success, result.Outcome);
        Assert.Equal(3, result.Inspection!.IntervalMonths);
    }

    [Fact]
    public async Task Complete_Passed_SpawnsNextInspectionAtInterval()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, new CreateInspectionRequest(
            InspectionType.CraneInspection, null, new DateOnly(2026, 7, 20), null, null, null), CancellationToken.None);

        var completed = await h.Sut.CompleteAsync(created.Inspection!.Id, new CompleteInspectionRequest(
            new DateOnly(2026, 7, 19), InspectionResult.Passed, null), CancellationToken.None);

        Assert.Equal(InspectionOperationOutcome.Success, completed.Outcome);
        Assert.Equal(InspectionUrgency.Completed, completed.Inspection!.Urgency);
        Assert.NotNull(completed.FollowUp);
        Assert.Equal(new DateOnly(2026, 10, 19), completed.FollowUp!.DueDate);
        Assert.Equal(3, completed.FollowUp.IntervalMonths);
    }

    [Fact]
    public async Task Complete_Failed_DoesNotSpawnFollowUp()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForTrailerAsync(h.TrailerId, new CreateInspectionRequest(
            InspectionType.TrailerInspection, null, new DateOnly(2026, 7, 20), IntervalMonths: 12, null, null), CancellationToken.None);

        var completed = await h.Sut.CompleteAsync(created.Inspection!.Id, new CompleteInspectionRequest(
            new DateOnly(2026, 7, 19), InspectionResult.Failed, "Remmen afgekeurd"), CancellationToken.None);

        Assert.Equal(InspectionOperationOutcome.Success, completed.Outcome);
        Assert.Null(completed.FollowUp);
    }

    [Fact]
    public async Task Complete_Twice_ReturnsAlreadyCompleted()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateForVehicleAsync(h.VehicleId, new CreateInspectionRequest(
            InspectionType.VehicleInspection, null, new DateOnly(2026, 7, 20), null, null, null), CancellationToken.None);
        var request = new CompleteInspectionRequest(new DateOnly(2026, 7, 19), InspectionResult.Passed, null);
        await h.Sut.CompleteAsync(created.Inspection!.Id, request, CancellationToken.None);

        var second = await h.Sut.CompleteAsync(created.Inspection.Id, request, CancellationToken.None);

        Assert.Equal(InspectionOperationOutcome.AlreadyCompleted, second.Outcome);
    }

    [Theory]
    [InlineData("2026-07-17", null, InspectionUrgency.Overdue)]
    [InlineData("2026-07-30", null, InspectionUrgency.DueSoon)]   // inside default 30d
    [InlineData("2026-12-01", null, InspectionUrgency.Ok)]        // outside default 30d
    [InlineData("2026-07-30", 5, InspectionUrgency.Ok)]           // custom 5d window: not yet
    [InlineData("2026-07-21", 5, InspectionUrgency.DueSoon)]      // custom 5d window: inside
    public void ComputeUrgency_CoversBoundaries(string due, int? warningDays, InspectionUrgency expected)
    {
        Assert.Equal(expected, InspectionService.ComputeUrgency(DateOnly.Parse(due), null, warningDays, Today));
    }

    [Fact]
    public async Task ListDue_ExcludesCompleted_AndOtherTenants()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var open = await h.Sut.CreateForVehicleAsync(h.VehicleId, new CreateInspectionRequest(
            InspectionType.VehicleInspection, null, new DateOnly(2026, 7, 1), null, null, null), CancellationToken.None);
        var toComplete = await h.Sut.CreateForVehicleAsync(h.VehicleId, new CreateInspectionRequest(
            InspectionType.CraneInspection, null, new DateOnly(2026, 7, 5), IntervalMonths: null, null, null), CancellationToken.None);
        // Completing spawns a follow-up 3 months out — outside the 30-day window.
        await h.Sut.CompleteAsync(toComplete.Inspection!.Id, new CompleteInspectionRequest(
            new DateOnly(2026, 7, 6), InspectionResult.Passed, null), CancellationToken.None);

        var foreignTenant = Guid.NewGuid();
        var foreignVehicle = Guid.NewGuid();
        h.Db.Context.Tenants.Add(new Tenant { Id = foreignTenant, Name = "Other", Slug = "other", IsActive = true, CreatedAt = Now.UtcDateTime });
        h.Db.Context.Vehicles.Add(new Vehicle { Id = foreignVehicle, TenantId = foreignTenant, InternalNumber = "X", LicensePlate = "X-1", IsActive = true });
        h.Db.Context.Inspections.Add(new Inspection
        {
            Id = Guid.NewGuid(), TenantId = foreignTenant, VehicleId = foreignVehicle,
            InspectionType = InspectionType.VehicleInspection, DueDate = new DateOnly(2026, 7, 1),
        });
        await h.Db.Context.SaveChangesAsync();

        var due = await h.Sut.ListDueAsync(30, CancellationToken.None);

        Assert.Single(due);
        Assert.Equal(open.Inspection!.Id, due[0].Id);
        Assert.Equal(InspectionUrgency.Overdue, due[0].Urgency);
        Assert.Equal("VRT-0001", due[0].OwnerNumber);
    }

    [Fact]
    public async Task Create_OtherWithoutName_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForVehicleAsync(h.VehicleId, new CreateInspectionRequest(
            InspectionType.Other, null, new DateOnly(2026, 8, 1), null, null, null), CancellationToken.None);

        Assert.Equal(InspectionOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task Create_ForForeignTrailer_ReturnsOwnerNotFound()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateForTrailerAsync(Guid.NewGuid(), new CreateInspectionRequest(
            InspectionType.TrailerInspection, null, new DateOnly(2026, 8, 1), null, null, null), CancellationToken.None);

        Assert.Equal(InspectionOperationOutcome.OwnerNotFound, result.Outcome);
    }
}
