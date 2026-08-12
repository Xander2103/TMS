using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 8 (L5/L6): the permission catalogue matches reality. A permission nobody checks is dead
/// weight that only SUGGESTS a capability boundary — it gets removed, and this test keeps the
/// catalogue honest going forward. Plus: development credentials never reach a log line.
/// </summary>
public class Phase8SupplyChainTests
{
    /// <summary>
    /// Codes that are genuinely enforced, but OUTSIDE a [RequirePermission] attribute — inside a
    /// service or filter. Every entry must name its enforcement site; an entry without a real
    /// site is exactly the dead-permission problem this test exists to catch.
    /// </summary>
    private static readonly Dictionary<string, string> ServiceSideEnforcedCodes = new(StringComparer.Ordinal)
    {
        [PermissionCodes.EmployeesViewConfidential] = "EmployeesController.HasConfidentialAccessAsync / EmployeeService confidential gating",
        [PermissionCodes.EmployeeDocumentsViewSensitive] = "EmployeeDocumentsController.HasSensitiveAccessAsync / EmployeeDocumentService",
        [PermissionCodes.AbsencesViewMedical] = "AbsenceService.MedicalScopeAsync (M7)",
        [PermissionCodes.UsersResetPassword] = "UsersController password-reset gate (C2)",
        [PermissionCodes.OrdersOverridePrice] = "TransportOrderService manual-price override (L7)",
        [PermissionCodes.OrdersLockPrice] = "TransportOrderService pricing-status transitions (L7)",
        [PermissionCodes.OrdersConfirmIncompletePrice] = "TransportOrderService.ConfirmOrderPricingAsync unpriced-goods override gate (wave 2026-08-04 §10)",
        [PermissionCodes.DossiersOverrideEntity] = "DossierService.ChangeLegalEntityAsync + TransportOrderService entity-change gate (Wave 2 §2)",
        [PermissionCodes.ProblemsApproveCharge] = "IncidentService.DecideChargeAsync approval gate (Wave 6 §2)",
        [PermissionCodes.EmployeePlanningConflictOverride] = "planning conflict-override flow",
        [PermissionCodes.ScanningOverride] = "scan pipeline override gate",
        [PermissionCodes.PackagesRelabel] = "PackagesController.PrintLabels reprint gate",
        [PermissionCodes.InventoryOverrideNegativeStock] = "NegativeStockGuard.EnsureAllowedAsync confirmation gate",
        [PermissionCodes.InventoryLowStockAlerts] = "InventoryAlertService recipient selector",
        [PermissionCodes.MessagesSendBulk] = "InternalMessageService.SendAsync bulk-targeting gate",
        [PermissionCodes.PortalMessagesSendBulk] = "PortalMessageService.SendAsync multi-customer gate",
        [PermissionCodes.OrdersAssign] = "TripsController order-to-trip assignment gate",

        // Lookup-/settings-screens: enforced fail-closed inside LookupControllerBase (View on
        // Search/Options/GetById, Manage on Create/Update/Delete) — the code varies per concrete
        // controller, hence no attribute. Phase 10 classifies these controllers accordingly.
        [PermissionCodes.TasksManageCategories] = "TaskCategoriesController : LookupControllerBase.ManagePermission",
        [PermissionCodes.DepartmentsView] = "DepartmentsController : LookupControllerBase.ViewPermission",
        [PermissionCodes.DepartmentsManage] = "DepartmentsController : LookupControllerBase.ManagePermission",
        [PermissionCodes.JobFunctionsView] = "JobFunctionsController : LookupControllerBase.ViewPermission",
        [PermissionCodes.JobFunctionsManage] = "JobFunctionsController : LookupControllerBase.ManagePermission",
        [PermissionCodes.VehicleCategoriesView] = "VehicleCategoriesController : LookupControllerBase.ViewPermission",
        [PermissionCodes.VehicleCategoriesManage] = "VehicleCategoriesController : LookupControllerBase.ManagePermission",
        [PermissionCodes.TrailerCategoriesView] = "TrailerCategoriesController : LookupControllerBase.ViewPermission",
        [PermissionCodes.TrailerCategoriesManage] = "TrailerCategoriesController : LookupControllerBase.ManagePermission",
        [PermissionCodes.DriverCategoriesView] = "DriverCategoriesController : LookupControllerBase.ViewPermission",
        [PermissionCodes.DriverCategoriesManage] = "DriverCategoriesController : LookupControllerBase.ManagePermission",
        [PermissionCodes.CustomerCategoriesView] = "CustomerCategoriesController : LookupControllerBase.ViewPermission",
        [PermissionCodes.CustomerCategoriesManage] = "CustomerCategoriesController : LookupControllerBase.ManagePermission",
        [PermissionCodes.ContactDepartmentsView] = "ContactDepartmentsController : LookupControllerBase.ViewPermission",
        [PermissionCodes.ContactDepartmentsManage] = "ContactDepartmentsController : LookupControllerBase.ManagePermission",
        [PermissionCodes.ReferenceDataView] = "Languages-/Nationalities-/ContractTypesController : LookupControllerBase.ViewPermission",
        [PermissionCodes.ReferenceDataManage] = "Languages-/Nationalities-/ContractTypesController : LookupControllerBase.ManagePermission",

        // Runtime-checked gates outside attributes.
        [PermissionCodes.CustomersManageFiscal] = "CustomersController fiscal-section gate (UserHasPermissionAsync)",
        [PermissionCodes.LocationsViewSensitive] = "LocationsController.HasSensitiveAccessAsync / LocationService access-code gating",
        [PermissionCodes.WarehouseReleaseTrip] = "TripsController release gate (UserHasPermissionAsync)",
        [PermissionCodes.WarehouseConflictOverride] = "DockPlanningController conflict-override gate (UserHasPermissionAsync)",
    };

    /// <summary>
    /// Historically this held codes enforced only by frontend feature gating. That hardening gap
    /// is closed: every former entry is enforced server-side (LookupControllerBase or a runtime
    /// gate, see <see cref="ServiceSideEnforcedCodes"/>). The set stays to keep the invariant
    /// explicit — it must remain EMPTY; new frontend-only codes are not acceptable.
    /// </summary>
    private static readonly HashSet<string> FrontendGatedCodes = new(StringComparer.Ordinal);

    private static IReadOnlyList<string> AttributeCheckedCodes()
    {
        var controllerTypes = typeof(PermissionCodes).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var codes = new List<string>();
        foreach (var controller in controllerTypes)
        {
            codes.AddRange(controller.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
                .SelectMany(a => a.PermissionCodes));
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                codes.AddRange(method.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
                    .SelectMany(a => a.PermissionCodes));
            }
        }

        return codes;
    }

    [Fact]
    public void NoPermission_IsOnlyFrontendGated()
    {
        Assert.Empty(FrontendGatedCodes);
    }

    [Fact]
    public void EveryCataloguePermission_IsActuallyCheckedSomewhere()
    {
        var checkedCodes = AttributeCheckedCodes()
            .Concat(ServiceSideEnforcedCodes.Keys)
            .Concat(FrontendGatedCodes)
            .ToHashSet(StringComparer.Ordinal);

        var dead = PermissionCodes.All
            .Select(p => p.Code)
            .Where(code => !checkedCodes.Contains(code))
            // Portal add-on roles are resolved by role template (klantportaal_*), their codes gate
            // portal features via the shared portal permission checks.
            .Where(code => !code.StartsWith("customer_portal.", StringComparison.Ordinal))
            .ToList();

        Assert.True(dead.Count == 0,
            "Catalogue permissions that nothing checks (remove them, or register their service-side "
            + "enforcement site in ServiceSideEnforcedCodes): " + string.Join(", ", dead));
    }

    [Fact]
    public void RetiredPermissions_AreGoneFromTheCatalogue()
    {
        string[] retired =
        [
            "users.delete", "qualification_types.manage", "qualification_types.view", "packages.export",
        ];
        foreach (var code in retired)
        {
            Assert.DoesNotContain(PermissionCodes.All, p => p.Code == code);
        }
    }

    [Fact]
    public async Task CatalogSeeder_RemovesRetiredPermissions_AndTheirGrants()
    {
        using var db = new TestSupport.SqliteTestDbContext();
        var role = new Modules.Identity.Entities.Role
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Name = "Rest", IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var retired = new Modules.Identity.Entities.Permission
        {
            Id = Guid.NewGuid(), Code = "packages.export", Module = "packages", Action = "export", Description = "dood",
        };
        db.Context.Roles.Add(role);
        db.Context.Permissions.Add(retired);
        db.Context.RolePermissions.Add(new Modules.Identity.Entities.RolePermission
        {
            RoleId = role.Id, PermissionId = retired.Id,
        });
        await db.Context.SaveChangesAsync();

        await TransportationService.Api.Data.PermissionCatalogSeeder.SyncAsync(db.Context);

        Assert.False(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(db.Context.Permissions, p => p.Code == "packages.export"));
        Assert.False(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .AnyAsync(db.Context.RolePermissions, rp => rp.PermissionId == retired.Id));
    }

    [Fact]
    public void DevAdminSeeder_NeverLogsThePassword()
    {
        // L6 conventietest: the literal dev password may exist as a constant, but no log template
        // in the seeder may interpolate it.
        var source = File.ReadAllText(FindRepoFile("TransportationService.Api/Data/DevAdminSeeder.cs"));
        Assert.DoesNotContain("{Password}", source);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar))))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
