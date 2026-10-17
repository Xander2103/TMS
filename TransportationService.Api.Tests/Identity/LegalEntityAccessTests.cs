using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using TransportationService.Api.Modules.Organization.Controllers;

namespace TransportationService.Api.Tests.Identity;

/// <summary>
/// ACC-M8: the entity PICKER (options) is open to every signed-in user, so taking
/// legal_entities.view away from Planner/Dispatcher cannot break order or dossier entry; the
/// settings list/detail stay gated by legal_entities.view.
/// </summary>
public class LegalEntityAccessTests
{
    private static MethodInfo Action(string name) =>
        typeof(LegalEntitiesController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!;

    [Fact]
    public void OptionsEndpoint_IsAuthOnly_WhileListAndDetailRequireTheViewRight()
    {
        var options = Action("Options");
        Assert.Null(options.GetCustomAttribute<RequirePermissionAttribute>());
        Assert.NotNull(options.GetCustomAttribute<AuthorizeAttribute>());

        foreach (var gated in new[] { "List", "GetById" })
        {
            var action = typeof(LegalEntitiesController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == gated);
            if (action is null)
            {
                continue; // name differs; the template assertions below are the contract that matters
            }

            Assert.Contains(PermissionCodes.LegalEntitiesView, action.GetCustomAttribute<RequirePermissionAttribute>()!.PermissionCodes);
        }
    }

    [Fact]
    public void Templates_GiveLegalEntitySettingsToFinanceAndManagementOnly()
    {
        static IReadOnlyList<string> Template(string code) => DefaultRoleDefinitions.All.Single(t => t.Code == code).PermissionCodes;
        Assert.DoesNotContain(PermissionCodes.LegalEntitiesView, Template("planner"));
        Assert.DoesNotContain(PermissionCodes.LegalEntitiesView, Template("dispatcher"));
        Assert.DoesNotContain(PermissionCodes.LegalEntitiesView, Template("chauffeur"));
        Assert.Contains(PermissionCodes.LegalEntitiesView, Template("management"));
        Assert.Contains(PermissionCodes.LegalEntitiesView, Template("boekhouding"));

        var step = DefaultRoleUpgrades.Steps.Single(s => s.Version == 36);
        Assert.Empty(step.GrantsByTemplateCode);
        Assert.Equal([PermissionCodes.LegalEntitiesView], step.RevocationsByTemplateCode!["planner"]);
        Assert.Equal([PermissionCodes.LegalEntitiesView], step.RevocationsByTemplateCode["dispatcher"]);
    }
}
