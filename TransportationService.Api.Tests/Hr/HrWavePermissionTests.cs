using System.Reflection;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Controllers;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Organization.Controllers;
using TransportationService.Api.Modules.Reference.Controllers;
using Xunit;

namespace TransportationService.Api.Tests.Hr;

/// <summary>
/// HR wave 2026-09-12 §12: who may manage HR master data and company assets. The backend is
/// the gate (attributes / LookupControllerBase permission properties); role templates decide
/// which built-in roles receive the codes.
/// </summary>
public class HrWavePermissionTests
{
    private static IReadOnlyList<string> Codes(Type controller, string action) =>
        controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;

    private static IReadOnlyList<string> Template(string code) => DefaultRoleDefinitions.All.Single(t => t.Code == code).PermissionCodes;

    [Fact]
    public void HrTemplate_ManagesDepartmentsFunctionsContractTypesAndCompanyAssets()
    {
        var hr = Template("hr");
        Assert.Contains(PermissionCodes.EmployeesCreate, hr);
        Assert.Contains(PermissionCodes.EmployeesEdit, hr);
        Assert.Contains(PermissionCodes.DepartmentsManage, hr);
        Assert.Contains(PermissionCodes.JobFunctionsManage, hr);
        Assert.Contains(PermissionCodes.ReferenceDataView, hr);
        Assert.Contains(PermissionCodes.ReferenceDataManage, hr);
        Assert.Contains(PermissionCodes.InventoryManage, hr);           // issued-item categories
        Assert.Contains(PermissionCodes.IssuedItemsManageTemplates, hr); // templates
        Assert.Contains(PermissionCodes.IssuedItemsManage, hr);          // issue / return / reactivate
        Assert.Contains(PermissionCodes.IssuedItemsView, hr);            // receipt download
    }

    [Fact]
    public void EmployeeFacingTemplates_CannotManageHrMasterDataOrAssets()
    {
        foreach (var code in new[] { "chauffeur", "magazijn", "planner", "dispatcher", "boekhouding" })
        {
            var template = Template(code);
            Assert.DoesNotContain(PermissionCodes.DepartmentsManage, template);
            Assert.DoesNotContain(PermissionCodes.JobFunctionsManage, template);
            Assert.DoesNotContain(PermissionCodes.ReferenceDataManage, template);
            Assert.DoesNotContain(PermissionCodes.IssuedItemsManage, template);
            Assert.DoesNotContain(PermissionCodes.IssuedItemsManageTemplates, template);
            Assert.DoesNotContain(PermissionCodes.InventoryManage, template);
        }
    }

    [Fact]
    public void UpgradeStep33_GrantsHrTheMasterDataPermissions_ForExistingTenants()
    {
        Assert.Equal(33, DefaultRoleUpgrades.CurrentVersion);
        var step = DefaultRoleUpgrades.Steps.Single(s => s.Version == 33);
        var grants = step.GrantsByTemplateCode["hr"];
        Assert.Contains(PermissionCodes.DepartmentsManage, grants);
        Assert.Contains(PermissionCodes.JobFunctionsManage, grants);
        Assert.Contains(PermissionCodes.ReferenceDataView, grants);
        Assert.Contains(PermissionCodes.ReferenceDataManage, grants);
        Assert.Single(step.GrantsByTemplateCode); // nobody else widens
    }

    private static string ManagePermissionOf(object controller) =>
        (string)controller.GetType().GetProperty("ManagePermission", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;

    [Fact]
    public void MasterDataControllers_GateCreateOnManagePermissions()
    {
        Assert.Equal(PermissionCodes.DepartmentsManage, ManagePermissionOf(new DepartmentsController(null!, null!, null!)));
        Assert.Equal(PermissionCodes.JobFunctionsManage, ManagePermissionOf(new JobFunctionsController(null!, null!, null!)));
        Assert.Equal(PermissionCodes.ReferenceDataManage, ManagePermissionOf(new ContractTypesController(null!, null!, null!)));
    }

    [Fact]
    public void IssuedItemEndpoints_RequireIssuedItemsManage_ForStateChanges_AndViewForReceipt()
    {
        Assert.Equal(new[] { PermissionCodes.IssuedItemsManage }, Codes(typeof(IssuedItemsController), nameof(IssuedItemsController.Return)));
        Assert.Equal(new[] { PermissionCodes.IssuedItemsManage }, Codes(typeof(IssuedItemsController), nameof(IssuedItemsController.Reactivate)));
        Assert.Equal(new[] { PermissionCodes.IssuedItemsManage }, Codes(typeof(IssuedItemsController), nameof(IssuedItemsController.Delete)));
        Assert.Equal(new[] { PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage },
            Codes(typeof(IssuedItemsController), nameof(IssuedItemsController.Document)));
        Assert.Equal(new[] { PermissionCodes.IssuedItemsManageTemplates }, Codes(typeof(IssuedItemsController), nameof(IssuedItemsController.CreateTemplate)));
    }

    [Fact]
    public void EmployeeAttention_IsReadableWithEmployeesView()
    {
        Assert.Equal(new[] { PermissionCodes.EmployeesView }, Codes(typeof(EmployeesController), nameof(EmployeesController.GetAttention)));
    }
}
