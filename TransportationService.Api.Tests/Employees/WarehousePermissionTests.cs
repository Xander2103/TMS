using System.Reflection;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TransportationService.Api.Common;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Employees.Controllers;
using TransportationService.Api.Modules.Employees.Services;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;

namespace TransportationService.Api.Tests.Employees;

/// <summary>WH-H4 permissions, WH-M10 authorization order and WH-M8 Dutch conversion messages.</summary>
public class WarehousePermissionTests
{
    private static IReadOnlyList<string> PermissionsOf(string action) =>
        typeof(InventoryIssuesController).GetMethod(action)!.GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes;

    private static IReadOnlySet<string> TemplateGrants(string code) =>
        DefaultRoleDefinitions.All.Single(t => t.Code == code).PermissionCodes.ToHashSet();

    [Fact]
    public void WarehouseDesk_IssueAndReturn_HaveTheirOwnGranularPermissions()
    {
        Assert.Equal([PermissionCodes.InventoryIssue], PermissionsOf(nameof(InventoryIssuesController.Issue)));
        Assert.Equal([PermissionCodes.InventoryReturn], PermissionsOf(nameof(InventoryIssuesController.Return)));
        Assert.Contains(PermissionCodes.EmployeesDeactivate, PermissionsOf(nameof(InventoryIssuesController.Outstanding)));
        Assert.DoesNotContain(PermissionCodes.EmployeesView, PermissionsOf(nameof(InventoryIssuesController.EmployeeOptions)));
    }

    [Fact]
    public void Magazijn_RunsTheDesk_WithoutAnyHrEmployeeAccess()
    {
        var magazijn = TemplateGrants("magazijn");
        Assert.Contains(PermissionCodes.InventoryIssue, magazijn);
        Assert.Contains(PermissionCodes.InventoryReturn, magazijn);
        foreach (var hr in new[]
                 {
                     // The item catalogue stays with HR (HR-wave decision, HrWavePermissionTests).
                     PermissionCodes.IssuedItemsManageTemplates, PermissionCodes.InventoryManage,
                     PermissionCodes.EmployeesView, PermissionCodes.EmployeesEdit, PermissionCodes.EmployeesViewConfidential,
                     PermissionCodes.IssuedItemsManage, PermissionCodes.AbsencesViewMedical, PermissionCodes.EmployeeDocumentsViewSensitive,
                     PermissionCodes.InventoryManageThresholds, PermissionCodes.InventoryOverrideNegativeStock,
                 })
        {
            Assert.DoesNotContain(hr, magazijn);
        }

        var hrRole = TemplateGrants("hr");
        Assert.Contains(PermissionCodes.InventoryIssue, hrRole);
        Assert.Contains(PermissionCodes.InventoryReturn, hrRole);
    }

    [Fact]
    public void Version39_GrantsTheDesk()
    {
        // Integration 2026-10-18: Warehouse is step 39; Exceptions followed as 40.
        Assert.Equal(40, DefaultRoleUpgrades.CurrentVersion);
        var step = DefaultRoleUpgrades.Steps.Single(s => s.Version == 39);
        Assert.Contains(PermissionCodes.InventoryIssue, step.GrantsByTemplateCode["magazijn"]);
        Assert.Contains(PermissionCodes.InventoryReturn, step.GrantsByTemplateCode["hr"]);
        Assert.Contains(PermissionCodes.InventoryIssue, PermissionCodes.All.Select(p => p.Code));
    }

    [Fact]
    public void PermissionCheck_RunsBeforeModelValidation()
    {
        // ApiController's ModelStateInvalidFilter runs at order -2000.
        Assert.True(new RequirePermissionAttribute(PermissionCodes.InventoryIssue).Order < -2000);
    }

    [Fact]
    public void JsonConversionError_IsRewrittenToDutch_OnTheFieldName()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.quantity",
            "The JSON value could not be converted to System.Int32. Path: $.quantity | LineNumber: 0 | BytePositionInLine: 17.");
        modelState.AddModelError("request", "The request field is required.");

        DutchModelStateMessages.Rewrite(modelState);

        Assert.False(modelState.ContainsKey("request"));
        var error = Assert.Single(modelState["quantity"]!.Errors);
        Assert.Equal("Geef een geheel getal in (geen decimalen of tekst).", error.ErrorMessage);
    }

    [Fact]
    public void JsonConversionError_OnARecordBody_UsesTheFieldsRealType()
    {
        // A positional record names the RECORD as the failing type, not Int32.
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.quantity",
            "The JSON value could not be converted to TransportationService.Api.Modules.Employees.Services.StockReceiptRequest. Path: $.quantity | LineNumber: 0 | BytePositionInLine: 33.");

        DutchModelStateMessages.Rewrite(modelState, typeof(StockReceiptRequest));

        Assert.Equal("Geef een geheel getal in (geen decimalen of tekst).", Assert.Single(modelState["quantity"]!.Errors).ErrorMessage);
    }

    [Fact]
    public void OrdinaryValidationErrors_AreLeftAlone()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Name", "The Name field is required.");
        DutchModelStateMessages.Rewrite(modelState);
        Assert.True(modelState.ContainsKey("Name"));
    }

    [Fact]
    public void IssuedItemCodes_Normalize()
    {
        Assert.Equal("BM-0007", IssuedItemCodes.Format(7));
        Assert.Equal("SCAN-01", IssuedItemCodes.Normalize(" scan-01 "));
        Assert.Null(IssuedItemCodes.Normalize("  "));
        Assert.Throws<DomainValidationException>(() => IssuedItemCodes.Normalize("met spatie"));
    }
}
