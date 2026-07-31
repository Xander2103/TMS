using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TransportationService.Api.Data;
using TransportationService.Api.Modules.Identity;
using TransportationService.Api.Modules.Identity.Authorization;
using Xunit;

namespace TransportationService.Api.Tests.Security;

/// <summary>
/// Phase 10: the systematic, structural half of the security suite. Behaviour is covered per
/// phase (Phase1–9 test files); these tests freeze the INVARIANTS so drift fails the build:
/// every endpoint is deliberately classified (permission-gated, authenticated-only by reviewed
/// design, or explicitly anonymous — the anonymous list lives in Phase1), and the permission
/// model is internally consistent.
/// </summary>
public class Phase10SystematicSecurityTests
{
    /// <summary>
    /// Endpoints that intentionally carry NO [RequirePermission]: they are authenticated-only
    /// and scope every query to the CALLER (own profile, own portal data, own driver trips) or
    /// to harmless shared context. The fallback authorization policy plus the account-state
    /// filter still applies to all of them. New endpoints land here only after review.
    /// </summary>
    private static readonly HashSet<string> ReviewedAuthenticatedOnlyEndpoints = new(StringComparer.Ordinal)
    {
        // Session/self: operate exclusively on the caller's own account.
        "AuthController.Logout",
        "AuthController.Me",
        "MeController.ChangePassword",

        // Employee self-service portal: every query resolves the caller's OWN employee link
        // (MyEmployeeIdAsync) — no foreign id is accepted anywhere.
        "MeController.Profile",
        "MeController.Dashboard",
        "MeController.Absences",
        "MeController.CreateAbsence",
        "MeController.CancelAbsence",
        "MeController.AttachAbsenceDocument",
        "MeController.DownloadAbsenceDocument",
        "MeController.Planning",
        "MeController.PlanningIcs",
        "MeController.ListCalendarNotes",
        "MeController.CreateCalendarNote",
        "MeController.UpdateCalendarNote",
        "MeController.DeleteCalendarNote",
        "MeController.Qualifications",
        "MeController.QualificationDocument",
        "MeResourceLinksController.List",
        "MeResourceLinksController.Touch",
        "MeResourceLinksController.Delete",
        "MeResourceLinksController.Reorder",

        // Per-user notification/message state.
        "NotificationsController.List",
        "NotificationsController.UnreadCount",
        "NotificationsController.MarkRead",
        "NotificationsController.MarkAllRead",
        "NotificationsController.Archive",
        "NotificationsController.Preferences",
        "NotificationsController.SetPreference",
        "InternalMessagesController.Inbox",
        "InternalMessagesController.UnreadCount",
        "InternalMessagesController.MarkRead",

        // Shared, non-sensitive tenant context: results are filtered by the caller's own
        // permissions (search) or are plain reference data (countries, legal-entity options/logo).
        "SearchController.Search",
        "CountriesController.GetOptions",
        "CountriesController.GetAll",
        "LegalEntitiesController.Options",
        "LegalEntitiesController.DownloadLogo",
        "LegalEntitiesController.GetActiveSelection",
        "LegalEntitiesController.SetActiveSelection",
    };

    /// <summary>
    /// All HTTP actions, INCLUDING those inherited from an abstract controller base — a
    /// DeclaredOnly scan would silently skip every LookupControllerBase-derived endpoint.
    /// </summary>
    private static IEnumerable<(Type Controller, MethodInfo Action)> HttpActions()
    {
        var assembly = typeof(TransportationDbContext).Assembly;
        foreach (var controller in assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            foreach (var method in controller
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
            {
                yield return (controller, method);
            }
        }
    }

    /// <summary>
    /// Lookup controllers enforce their per-controller View-/ManagePermission codes inside
    /// <see cref="TransportationService.Api.Common.Lookups.LookupControllerBase{TEntity}"/>
    /// (fail-closed, 401/403) rather than via attributes — the code varies per concrete
    /// controller. Phase 8 registers those codes as service-side enforced.
    /// </summary>
    private static bool IsServiceSideGatedLookupController(Type controller)
    {
        for (var type = controller.BaseType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(TransportationService.Api.Common.Lookups.LookupControllerBase<>))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void EveryEndpoint_IsDeliberatelyClassified()
    {
        var unclassified = new List<string>();
        foreach (var (controller, action) in HttpActions())
        {
            var name = $"{controller.Name}.{action.Name}";
            var anonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var permissionGated = controller.GetCustomAttributes<RequirePermissionAttribute>(true).Any()
                || action.GetCustomAttributes<RequirePermissionAttribute>(true).Any();

            if (anonymous || permissionGated || IsServiceSideGatedLookupController(controller)
                || ReviewedAuthenticatedOnlyEndpoints.Contains(name))
            {
                continue;
            }

            unclassified.Add(name);
        }

        Assert.True(unclassified.Count == 0,
            "Endpoints without [RequirePermission] that are not on the reviewed authenticated-only "
            + "allowlist (classify them deliberately): " + string.Join(", ", unclassified));
    }

    [Fact]
    public void EveryAttributeCheckedPermission_ExistsInTheCatalogue()
    {
        var catalogue = PermissionCodes.All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var (controller, action) in HttpActions())
        {
            var codes = controller.GetCustomAttributes<RequirePermissionAttribute>(true)
                .Concat(action.GetCustomAttributes<RequirePermissionAttribute>(true))
                .SelectMany(a => a.PermissionCodes);
            unknown.AddRange(codes.Where(c => !catalogue.Contains(c)).Select(c => $"{controller.Name}.{action.Name}:{c}"));
        }

        Assert.True(unknown.Count == 0,
            "Endpoints demanding a permission that is not in the catalogue (nobody could ever hold "
            + "it — the endpoint is dead for everyone but system roles): " + string.Join(", ", unknown));
    }

    [Fact]
    public void DefaultTemplates_OnlyGrantCataloguePermissions()
    {
        var catalogue = PermissionCodes.All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        var unknown = DefaultRoleDefinitions.All
            .SelectMany(t => t.PermissionCodes.Select(code => (t.Code, code)))
            .Where(x => !catalogue.Contains(x.code))
            .Select(x => $"{x.Code}:{x.code}")
            .ToList();

        Assert.True(unknown.Count == 0,
            "Role templates granting non-catalogue permissions: " + string.Join(", ", unknown));
    }

    [Fact]
    public void UpgradeSteps_OnlyGrantCatalogueOrExplicitlyRetiredPermissions()
    {
        // Historical steps are immutable; codes retired later stay in them as literals and are
        // skipped by the seeder. Everything else must exist.
        var catalogue = PermissionCodes.All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);
        string[] retired = ["users.delete", "qualification_types.manage", "qualification_types.view", "packages.export"];

        var unknown = DefaultRoleUpgrades.Steps
            .SelectMany(s => s.GrantsByTemplateCode.Values.SelectMany(codes => codes.Select(code => (s.Version, code))))
            .Where(x => !catalogue.Contains(x.code) && !retired.Contains(x.code))
            .Select(x => $"v{x.Version}:{x.code}")
            .ToList();

        Assert.True(unknown.Count == 0,
            "Upgrade steps granting unknown, non-retired permissions: " + string.Join(", ", unknown));
    }
}
