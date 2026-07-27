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
    public const int CurrentVersion = 16;

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

        new(3,
            "Package milestone 2026-07: package management, scanning override, warehouse access, incident dispositions and package reports.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                // Planner acts as dispatch: full package administration, dispositions,
                // completion overrides, warehouse visibility and trip release.
                ["planner"] =
                [
                    PermissionCodes.PackagesView, PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage,
                    PermissionCodes.PackagesCancel, PermissionCodes.PackagesRelabel, PermissionCodes.PackagesExport,
                    PermissionCodes.PackageExceptionsCreate, PermissionCodes.PackageExceptionsManage,
                    PermissionCodes.ScanningOverride,
                    PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
                    PermissionCodes.PackageReportsExport,
                ],
                ["dispatcher"] =
                [
                    PermissionCodes.PackagesView,
                    PermissionCodes.PackageExceptionsCreate, PermissionCodes.PackageExceptionsManage,
                    PermissionCodes.ScanningOverride,
                    PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
                ],
                ["management"] =
                [
                    PermissionCodes.PackagesView,
                    PermissionCodes.WarehouseView,
                    PermissionCodes.PackageReportsExport,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.PackageReportsExport,
                ],
            }),

        new(4,
            "Improvement wave 2026-07-20: explicit customer activate/deactivate permission.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                // Customer lifecycle (deactivate/reactivate) sits with planner (customer
                // administration) and management (supervision); boekhouding keeps only the
                // credit block via customers.edit.
                ["planner"] =
                [
                    PermissionCodes.CustomersDeactivate,
                    PermissionCodes.OrdersCorrectStatus,
                    PermissionCodes.MessagesSend,
                ],
                ["hr"] =
                [
                    PermissionCodes.MessagesSend,
                ],
                ["management"] =
                [
                    PermissionCodes.CustomersDeactivate,
                    PermissionCodes.MaintenancePoliciesView,
                    PermissionCodes.MaintenancePoliciesManage,
                    PermissionCodes.OrdersCorrectStatus,
                    PermissionCodes.MessagesSend,
                ],
                // Existing tenants' klantportaal roles gain the portal permissions; the old
                // orders.view grant is deliberately left in place (upgrades never remove) —
                // tenants revoke it manually if they want the stricter portal-only surface.
                ["klantportaal"] =
                [
                    PermissionCodes.CustomerPortalView,
                    PermissionCodes.CustomerPortalSubmitOrders,
                    PermissionCodes.CustomerPortalManageLocations,
                ],
            }),

        // A separate step (not merged into 4) because tenants may already be stamped at
        // version 4 by an earlier deployment of this same wave.
        new(5,
            "Improvement wave 2026-07-20: transport dossiers and incident registration.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] =
                [
                    PermissionCodes.DossiersView, PermissionCodes.DossiersManage,
                    PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                ],
                // Dispatch registers and works incidents during execution; dossier
                // administration stays with planner/management.
                ["dispatcher"] =
                [
                    PermissionCodes.DossiersView,
                    PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                ],
                ["management"] =
                [
                    PermissionCodes.DossiersView, PermissionCodes.DossiersManage,
                    PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.DossiersView, PermissionCodes.IncidentsView,
                ],
            }),

        new(6,
            "Improvement wave 2026-07-20: customer rate cards (tarification slice).",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] =
                [
                    PermissionCodes.TariffsView,
                ],
                ["management"] =
                [
                    PermissionCodes.TariffsView, PermissionCodes.TariffsManage,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.TariffsView, PermissionCodes.TariffsManage,
                ],
            }),

        new(7,
            "Improvement 2026-07-20: report centre catalog access.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] = [PermissionCodes.ReportsView],
                ["dispatcher"] = [PermissionCodes.ReportsView],
                ["management"] = [PermissionCodes.ReportsView],
                ["boekhouding"] = [PermissionCodes.ReportsView],
                ["hr"] = [PermissionCodes.ReportsView],
            }),

        new(8,
            "Operational wave 2026-07-20: control center, dock planning and profitability export.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                // Planner and dispatcher run the live operation: control center + alert
                // handling + dock scheduling. Dock conflict override stays planner-side.
                ["planner"] =
                [
                    PermissionCodes.OperationsView, PermissionCodes.OperationsManageAlerts,
                    PermissionCodes.WarehouseSchedule, PermissionCodes.WarehouseConflictOverride,
                ],
                ["dispatcher"] =
                [
                    PermissionCodes.OperationsView, PermissionCodes.OperationsManageAlerts,
                    PermissionCodes.WarehouseSchedule,
                ],
                ["management"] =
                [
                    PermissionCodes.OperationsView,
                    PermissionCodes.ProfitabilityExport,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.ProfitabilityExport,
                ],
                // Warehouse staff own master data and the dock board, and see the control
                // center for arrivals context.
                ["magazijn"] =
                [
                    PermissionCodes.OperationsView,
                    PermissionCodes.WarehouseManage, PermissionCodes.WarehouseSchedule,
                    PermissionCodes.WarehouseConflictOverride,
                ],
            }),

        new(9,
            "Business feedback wave 2026-07-21: legal entities, customer master data, HR and fleet compliance.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] =
                [
                    PermissionCodes.LegalEntitiesView,
                    PermissionCodes.ContactDepartmentsView, PermissionCodes.ContactDepartmentsManage,
                    PermissionCodes.CustomersManageFiscal, PermissionCodes.CustomersManageCommunication,
                    PermissionCodes.CustomersManageSurcharge, PermissionCodes.CustomersManagePo,
                ],
                ["dispatcher"] =
                [
                    PermissionCodes.LegalEntitiesView,
                    PermissionCodes.ContactDepartmentsView,
                ],
                ["management"] =
                [
                    PermissionCodes.LegalEntitiesView,
                    PermissionCodes.ContactDepartmentsView,
                    PermissionCodes.InvoiceAttachmentsView,
                    PermissionCodes.TachographView, PermissionCodes.TachographManage,
                    PermissionCodes.FleetFinanceView, PermissionCodes.FleetFinanceManage,
                ],
                ["hr"] =
                [
                    PermissionCodes.ContactDepartmentsView,
                    PermissionCodes.EmployeeDocumentsCreate, PermissionCodes.EmployeeDocumentsEdit,
                    PermissionCodes.EmployeeDocumentsDelete, PermissionCodes.EmployeeDocumentsViewSensitive,
                    PermissionCodes.HrSettingsManage,
                    PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage, PermissionCodes.IssuedItemsManageTemplates,
                ],
                ["magazijn"] =
                [
                    PermissionCodes.IssuedItemsView,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.LegalEntitiesView, PermissionCodes.LegalEntitiesManage,
                    PermissionCodes.InvoicesOverrideNumber,
                    PermissionCodes.CustomersImport, PermissionCodes.CustomersOverrideNumber,
                    PermissionCodes.ContactDepartmentsView, PermissionCodes.ContactDepartmentsManage,
                    PermissionCodes.CustomersManageFiscal, PermissionCodes.CustomersManageCommunication,
                    PermissionCodes.CustomersManageSurcharge, PermissionCodes.CustomersManagePo,
                    PermissionCodes.InvoiceAttachmentsView, PermissionCodes.InvoiceAttachmentsManage,
                ],
            }),

        new(10,
            "Leave balance 2026-07-23: verlofsaldo view/manage/adjust, configurable leave & balance types, and self-view.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["hr"] =
                [
                    PermissionCodes.LeaveBalancesView, PermissionCodes.LeaveBalancesManage,
                    PermissionCodes.LeaveBalancesAdjust, PermissionCodes.LeaveBalancesViewOwn,
                    PermissionCodes.LeaveTypesManage,
                ],
                ["management"] =
                [
                    PermissionCodes.LeaveBalancesView, PermissionCodes.LeaveBalancesViewOwn,
                ],
                ["planner"] = [PermissionCodes.LeaveBalancesViewOwn],
                ["dispatcher"] = [PermissionCodes.LeaveBalancesViewOwn],
                ["chauffeur"] = [PermissionCodes.LeaveBalancesViewOwn],
            }),

        new(11,
            "Stock subsystem 2026-07-23: inventory view/manage/adjust + negative-stock override for issued items.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["hr"] =
                [
                    PermissionCodes.InventoryView, PermissionCodes.InventoryManage,
                    PermissionCodes.InventoryAdjust, PermissionCodes.InventoryOverrideNegativeStock,
                ],
                ["magazijn"] =
                [
                    PermissionCodes.InventoryView, PermissionCodes.InventoryAdjust,
                ],
                ["management"] =
                [
                    PermissionCodes.InventoryView,
                ],
            }),

        new(12,
            "Low-stock alerts 2026-07-24: recipients of inventory low-stock notifications.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["hr"] = [PermissionCodes.InventoryLowStockAlerts],
                ["magazijn"] = [PermissionCodes.InventoryLowStockAlerts],
            }),

        new(13,
            "Order pricing 2026-07-24: manual override of the calculated order price.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] = [PermissionCodes.OrdersOverridePrice],
                ["planner"] = [PermissionCodes.OrdersOverridePrice],
            }),

        new(14,
            "Order pricing status 2026-07-27: line-level manual pricing, status and locking.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] = [PermissionCodes.OrdersLockPrice],
                ["management"] = [PermissionCodes.OrdersLockPrice],
                ["boekhouding"] = [PermissionCodes.OrdersLockPrice],
            }),

        new(15,
            "Rate-table Excel export/import 2026-07-27: validated round-trip import of tarieventabellen.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] = [PermissionCodes.TariffsImport],
                ["boekhouding"] = [PermissionCodes.TariffsImport],
            }),

        new(16,
            "Accounting 2026-07-28: tenant ledger accounts and sales-category mappings (Bedrijfsinstellingen → Boekhouding).",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["boekhouding"] = [PermissionCodes.AccountingView, PermissionCodes.AccountingManage],
                ["management"] = [PermissionCodes.AccountingView],
            }),
    ];
}
