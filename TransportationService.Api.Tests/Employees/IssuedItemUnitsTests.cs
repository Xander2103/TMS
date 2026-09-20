using TransportationService.Api.Common;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Employees;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Employees;

/// <summary>HR wave 2026-09-12 §8: issued-item templates use a fixed unit catalogue, not UnitType master data.</summary>
public class IssuedItemUnitsTests
{
    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private static async Task<(SqliteTestDbContext Db, IssuedItemService Sut)> SeedAsync()
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = $"acme-{tenantId:N}", IsActive = true, CreatedAt = DateTime.UtcNow });
        await db.Context.SaveChangesAsync();
        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var inventory = new InventoryService(db.Context, tenant, user, audit, InventoryTestFactory.Guard(user));
        return (db, new IssuedItemService(db.Context, tenant, user, audit, inventory, new AllowAllPermissions(), InventoryTestFactory.Guard(user)));
    }

    private static SaveIssuedItemTemplateRequest Template(string? unit) =>
        new("Veiligheidsschoenen", "Kleding", null, 1, false, true, true, true, 0, Unit: unit);

    [Fact]
    public void Catalogue_IsTheFixedListInUiOrder_WithPieceAsDefault()
    {
        Assert.Equal(["piece", "pair", "set", "box", "pack", "roll", "meter", "liter", "kilogram", "other"], IssuedItemUnits.Codes);
        Assert.Equal("piece", IssuedItemUnits.Default);
        Assert.All(IssuedItemUnits.Codes, code => Assert.True(IssuedItemUnits.IsValid(code)));
        Assert.False(IssuedItemUnits.IsValid("Paar"));
    }

    [Theory]
    [InlineData(null, "piece")]
    [InlineData("", "piece")]
    [InlineData("pair", "pair")]
    [InlineData("PAIR", "pair")]
    [InlineData("Paar", "pair")]
    [InlineData("Stuks", "piece")]
    [InlineData("kg", "kilogram")]
    [InlineData("Doos", "box")]
    [InlineData("Rol", "roll")]
    public void TryParse_AcceptsCodesAndLegacyNames(string? input, string expected)
    {
        Assert.Equal(expected, IssuedItemUnits.TryParse(input));
    }

    [Theory]
    [InlineData("europallet")]
    [InlineData("laadmeter")]
    [InlineData("PIECES2")]
    public void TryParse_RejectsUnknownValues_AndNormalizeMapsThemToOther(string input)
    {
        Assert.Null(IssuedItemUnits.TryParse(input));
        Assert.Equal("other", IssuedItemUnits.Normalize(input));
    }

    [Fact]
    public async Task Template_WithoutUnit_DefaultsToPiece()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var created = await sut.CreateTemplateAsync(Template(null), CancellationToken.None);

        Assert.Equal("piece", created.Unit);
    }

    [Fact]
    public async Task Template_WithLegacyName_IsStoredAsCatalogueCode()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var created = await sut.CreateTemplateAsync(Template("Paar"), CancellationToken.None);
        var updated = await sut.UpdateTemplateAsync(created.Id, Template("set"), CancellationToken.None);

        Assert.Equal("pair", created.Unit);
        Assert.Equal("set", updated!.Unit);
    }

    [Fact]
    public async Task Template_WithUnknownUnit_IsRejected()
    {
        var (db, sut) = await SeedAsync();
        using var _ = db;

        var error = await Assert.ThrowsAsync<DomainValidationException>(() =>
            sut.CreateTemplateAsync(Template("Europallet"), CancellationToken.None));

        Assert.Contains("eenheid", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The general UnitType master data ("Eenheden") is no longer an input for company assets.</summary>
    [Fact]
    public void UnitTypesController_NoLongerExposesInventoryOptions()
    {
        var controller = typeof(TransportationService.Api.Modules.Reference.Controllers.UnitTypesController);
        Assert.Null(controller.GetMethod("InventoryOptions"));
    }
}
