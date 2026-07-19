using TransportationService.Api.Modules.Identity;

namespace TransportationService.Api.Data;

/// <summary>
/// Versioned evolution of the default-role templates. Each step lists ONLY the permissions
/// newly introduced for a template at that version; the seeder applies steps above a
/// tenant's recorded version exactly once (add-if-missing, matched on Role.TemplateCode).
/// Nothing is ever removed, and a permission a tenant deletes AFTER the step was applied
/// stays deleted — the per-tenant version marker prevents re-application.
/// </summary>
public static class DefaultRoleUpgrades
{
    public sealed record UpgradeStep(
        int Version,
        string Description,
        IReadOnlyDictionary<string, IReadOnlyList<string>> GrantsByTemplateCode);

    /// <summary>Version 1 = the original role creation; steps start at 2.</summary>
    public const int CurrentVersion = 2;

    public static IReadOnlyList<UpgradeStep> Steps { get; } =
    [
        new(2,
            "Costing/KPI milestone 2026-07: planning-conflict override, trip costs, profitability and KPI permissions.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] =
                [
                    PermissionCodes.EmployeePlanningConflictOverride,
                    PermissionCodes.TripCostsView,
                ],
                ["management"] =
                [
                    PermissionCodes.EmployeePlanningConflictOverride,
                    PermissionCodes.TripCostsView,
                    PermissionCodes.TripCostsManage,
                    PermissionCodes.TripCostsOverride,
                    PermissionCodes.ProfitabilityView,
                    PermissionCodes.KpiView,
                    PermissionCodes.KpiExport,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.TripCostsView,
                    PermissionCodes.ProfitabilityView,
                    PermissionCodes.KpiView,
                    PermissionCodes.KpiExport,
                ],
                // HR is created by this same milestone with its full set; listed here so a
                // stamped hr role created moments earlier in the same run converges too.
                ["hr"] =
                [
                    PermissionCodes.EmployeePlanningConflictOverride,
                ],
            }),
    ];
}
