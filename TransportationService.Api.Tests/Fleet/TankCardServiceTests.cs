using TransportationService.Api.Common.Models;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Fleet.Dtos;
using TransportationService.Api.Modules.Fleet.Entities;
using TransportationService.Api.Modules.Fleet.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;

namespace TransportationService.Api.Tests.Fleet;

public class TankCardServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 07, 18);

    private sealed record Harness(SqliteTestDbContext Db, TankCardService Sut, Guid TenantId, Guid VehicleId);

    private static async Task<Harness> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = Now.UtcDateTime });
        db.Context.Vehicles.Add(new Vehicle { Id = vehicleId, TenantId = tenantId, InternalNumber = "VRT-0001", LicensePlate = "1-A-1", IsActive = true });
        await db.Context.SaveChangesAsync();

        var tenant = new DevTenantContext(tenantId);
        var sut = new TankCardService(db.Context, tenant, new AuditService(db.Context, tenant, new DevCurrentUserContext(null)), new TestClock(Now));
        return new Harness(db, sut, tenantId, vehicleId);
    }

    private static CreateTankCardRequest Request(string cardNumber = "7002-1111-2222-0001", Guid? vehicleId = null) =>
        new(cardNumber, "DKV", vehicleId, null, new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1), null);

    [Fact]
    public void ComputeStatus_CoversLifecycle()
    {
        Assert.Equal(TankCardStatus.Active, TankCardService.ComputeStatus(false, null, Today));
        Assert.Equal(TankCardStatus.Active, TankCardService.ComputeStatus(false, Today.AddDays(61), Today));
        Assert.Equal(TankCardStatus.ExpiringSoon, TankCardService.ComputeStatus(false, Today.AddDays(60), Today));
        Assert.Equal(TankCardStatus.Expired, TankCardService.ComputeStatus(false, Today.AddDays(-1), Today));
        // Blocked wins even when the card is also expired.
        Assert.Equal(TankCardStatus.Blocked, TankCardService.ComputeStatus(true, Today.AddDays(-1), Today));
    }

    [Fact]
    public async Task Create_WithVehicle_ResolvesVehicleInfo()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(vehicleId: h.VehicleId), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.Success, result.Outcome);
        Assert.Equal("VRT-0001", result.Card!.VehicleInternalNumber);
        Assert.Equal(TankCardStatus.Active, result.Card.Status);
    }

    [Fact]
    public async Task Create_DuplicateCardNumber_Conflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request(), CancellationToken.None);

        var duplicate = await h.Sut.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.DuplicateCardNumber, duplicate.Outcome);
    }

    [Fact]
    public async Task Create_ForeignVehicle_ReturnsInvalidReference()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(Request(vehicleId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.InvalidReference, result.Outcome);
    }

    [Fact]
    public async Task Create_ValidUntilBeforeValidFrom_FailsValidation()
    {
        var h = await SeedAsync();
        using var _ = h.Db;

        var result = await h.Sut.CreateAsync(
            new CreateTankCardRequest("X-1", "Shell", null, null, new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1), null),
            CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task SetBlocked_TogglesStatusAndReason()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request(), CancellationToken.None);

        var blocked = await h.Sut.SetBlockedAsync(created.Card!.Id,
            new SetTankCardBlockedRequest(true, "Kaart verloren"), CancellationToken.None);
        Assert.Equal(TankCardStatus.Blocked, blocked.Card!.Status);
        Assert.Equal("Kaart verloren", blocked.Card.BlockedReason);

        var unblocked = await h.Sut.SetBlockedAsync(created.Card.Id,
            new SetTankCardBlockedRequest(false, null), CancellationToken.None);
        Assert.Equal(TankCardStatus.Active, unblocked.Card!.Status);
        Assert.Null(unblocked.Card.BlockedReason);
    }

    [Fact]
    public async Task Search_FiltersByComputedStatus()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("ACTIVE-1"), CancellationToken.None);
        await h.Sut.CreateAsync(new CreateTankCardRequest("EXPIRED-1", "DKV", null, null, null, Today.AddDays(-1), null), CancellationToken.None);

        var expired = await h.Sut.SearchAsync(null, TankCardStatus.Expired, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, expired.TotalCount);
        Assert.Equal("EXPIRED-1", expired.Items[0].CardNumber);
    }

    [Fact]
    public async Task Search_MatchesVehicleNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("CARD-A", h.VehicleId), CancellationToken.None);
        await h.Sut.CreateAsync(Request("CARD-B"), CancellationToken.None);

        var byVehicle = await h.Sut.SearchAsync("vrt-0001", null, PageRequest.Of(1, 25), CancellationToken.None);

        Assert.Equal(1, byVehicle.TotalCount);
        Assert.Equal("CARD-A", byVehicle.Items[0].CardNumber);
    }

    [Fact]
    public async Task Update_ToExistingCardNumber_Conflicts()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        await h.Sut.CreateAsync(Request("CARD-A"), CancellationToken.None);
        var second = await h.Sut.CreateAsync(Request("CARD-B"), CancellationToken.None);

        var result = await h.Sut.UpdateAsync(second.Card!.Id,
            new UpdateTankCardRequest("CARD-A", "DKV", null, null, null, null, null), CancellationToken.None);

        Assert.Equal(TankCardOperationOutcome.DuplicateCardNumber, result.Outcome);
    }

    [Fact]
    public async Task Delete_SoftDeletes_AndFreesCardNumber()
    {
        var h = await SeedAsync();
        using var _ = h.Db;
        var created = await h.Sut.CreateAsync(Request("CARD-A"), CancellationToken.None);

        Assert.True(await h.Sut.DeleteAsync(created.Card!.Id, CancellationToken.None));
        Assert.Null(await h.Sut.GetByIdAsync(created.Card.Id, CancellationToken.None));

        // Filtered unique index: the number can be reused after soft delete.
        var recreated = await h.Sut.CreateAsync(Request("CARD-A"), CancellationToken.None);
        Assert.Equal(TankCardOperationOutcome.Success, recreated.Outcome);
    }
}
