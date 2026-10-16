using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Common.Lookups;
using TransportationService.Api.Modules.Auditing.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Services;
using TransportationService.Api.Modules.Reference.Controllers;
using TransportationService.Api.Modules.Reference.Entities;
using TransportationService.Api.Modules.Reference.Services;
using TransportationService.Api.Modules.Tenancy.Entities;
using TransportationService.Api.Modules.Tenancy.Services;
using TransportationService.Api.Tests.TestSupport;
using Xunit;

namespace TransportationService.Api.Tests.Transport;

/// <summary>
/// Transport acceptance T-H1: the Planner (orders.create/edit, dossiers.manage/price) and the
/// Dispatcher (orders.view, dossiers.view) got 403 on GET /api/unit-types/options, so the goods
/// "Eenheid" and the sales-line unit dropdown were empty and every line was saved as "stuks".
/// Loading the catalogue is USE; administering it still requires unit_types.manage.
/// </summary>
public class UnitLookupPermissionTests
{
    private static readonly string[] PlannerCodes =
    [
        PermissionCodes.OrdersView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit,
        PermissionCodes.DossiersView, PermissionCodes.DossiersManage, PermissionCodes.DossiersPrice,
    ];

    private static readonly string[] DispatcherCodes = [PermissionCodes.OrdersView, PermissionCodes.DossiersView];

    private sealed class PermissionStub(params string[] codes) : IPermissionAuthorizationService
    {
        private readonly HashSet<string> _codes = new(codes, StringComparer.Ordinal);

        public Task<bool> UserHasPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken)
            => Task.FromResult(_codes.Contains(permissionCode));
    }

    private static (SqliteTestDbContext Db, UnitTypesController Controller) Build(params string[] codes)
    {
        var db = new SqliteTestDbContext();
        var tenantId = Guid.NewGuid();
        db.Context.Tenants.Add(new Tenant { Id = tenantId, Name = "Acme", Slug = "acme", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.Context.UnitTypes.AddRange(
            new UnitType { TenantId = tenantId, Code = "EUROPALLET", Name = "Europallet", SortOrder = 1 },
            new UnitType { TenantId = tenantId, Code = "COLLI", Name = "Colli", SortOrder = 2 });
        db.Context.SaveChanges();

        var tenant = new DevTenantContext(tenantId);
        var user = new DevCurrentUserContext(Guid.NewGuid());
        var audit = new AuditService(db.Context, tenant, user);
        var controller = new UnitTypesController(
            new LookupService<UnitType>(db.Context, tenant, audit), user, new PermissionStub(codes),
            db.Context, tenant, new UnitTypeMasterService(db.Context, tenant, audit));
        return (db, controller);
    }

    [Theory]
    [MemberData(nameof(OperationalRoles))]
    public async Task OperationalRoles_CanLoadUnitOptions(string role, string[] codes)
    {
        var (db, controller) = Build(codes);
        using var _ = db;

        var result = await controller.Options(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var options = Assert.IsAssignableFrom<IReadOnlyList<LookupOptionDto>>(ok.Value);
        Assert.Contains(options, o => o.Code == "EUROPALLET");
        Assert.True(options.Count == 2, role);
    }

    public static TheoryData<string, string[]> OperationalRoles => new()
    {
        { "planner", PlannerCodes },
        { "dispatcher", DispatcherCodes },
    };

    [Fact]
    public async Task LoadingOptions_DoesNotGrantMasterDataAdministration()
    {
        var (db, controller) = Build(PlannerCodes);
        using var _ = db;

        var create = await controller.Create(new CreateLookupRequest("NEW", "Nieuw", null, true, 9), CancellationToken.None);
        var forbidden = Assert.IsType<ObjectResult>(create.Result);
        Assert.Equal(403, forbidden.StatusCode);
    }

    [Fact]
    public async Task UserWithoutTransportRights_IsStillDenied()
    {
        var (db, controller) = Build(PermissionCodes.EmployeesView);
        using var _ = db;

        var result = await controller.Options(CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(403, forbidden.StatusCode);
    }
}
