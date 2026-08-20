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
    public const int CurrentVersion = 30;

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
                    PermissionCodes.PackagesCancel, PermissionCodes.PackagesRelabel,
                    // Historische stap: "packages.export" is uit de catalogus verwijderd (L5);
                    // de literal blijft zodat de stap zelf onveranderd is — de seeder slaat
                    // codes over die niet meer bestaan.
                    "packages.export",
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

        new(17,
            "Personnel notes 2026-07-29: multiple notes per employee with dashboard pinning.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["hr"] =
                [
                    PermissionCodes.EmployeeNotesView, PermissionCodes.EmployeeNotesManage, PermissionCodes.EmployeeNotesPin,
                ],
                ["management"] =
                [
                    PermissionCodes.EmployeeNotesView, PermissionCodes.EmployeeNotesManage, PermissionCodes.EmployeeNotesPin,
                ],
            }),

        new(18,
            "Notification domain 2026-07-29: configurable events, recipients and customer overrides.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] =
                [
                    PermissionCodes.NotificationRulesView, PermissionCodes.NotificationRulesManage,
                ],
                ["hr"] = [PermissionCodes.NotificationRulesView],
                ["boekhouding"] = [PermissionCodes.NotificationRulesView],
            }),

        new(19,
            "Customer portal foundation 2026-07-30: portal messages default on the base "
            + "klantportaal role, and internal customer-messages/portal-announcements access.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                // klantportaal_documenten/klantportaal_facturen/klantportaal_gebruikersbeheer are
                // BRAND NEW templates — the seeder's create-phase (phase 2) already stamps them
                // for every tenant with their full permission set, so no upgrade step is needed
                // for those three; only the pre-existing "klantportaal" role needs its new
                // CustomerPortalMessages grant applied here.
                ["klantportaal"] = [PermissionCodes.CustomerPortalMessages],
                ["planner"] = [PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend],
                ["dispatcher"] = [PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend],
                ["management"] =
                [
                    PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend,
                    PermissionCodes.PortalAnnouncementsManage,
                ],
            }),

        new(20,
            "EDI redesign 2026-07-30: edi.manage keeps full rights; edi.view/test/retry granted "
            + "alongside it to the same template (management is the only current edi.manage holder).",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] =
                [
                    PermissionCodes.EdiView, PermissionCodes.EdiTest, PermissionCodes.EdiRetry,
                ],
            }),

        new(21,
            "Peppol foundations 2026-07-30: provider-neutral Peppol domain and configuration. "
            + "Boekhouding gets everyday Peppol operation rights; management additionally gets "
            + "peppol.configure (per-legal-entity settings).",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] =
                [
                    PermissionCodes.PeppolView, PermissionCodes.PeppolConfigure, PermissionCodes.PeppolValidate,
                    PermissionCodes.PeppolSend, PermissionCodes.PeppolRetry, PermissionCodes.PeppolViewIncoming,
                ],
                ["boekhouding"] =
                [
                    PermissionCodes.PeppolView, PermissionCodes.PeppolValidate,
                    PermissionCodes.PeppolSend, PermissionCodes.PeppolRetry, PermissionCodes.PeppolViewIncoming,
                ],
            }),

        new(22,
            "Health-data gating 2026-07-31: sick-leave reasons, HR-internal notes on sick leave and "
            + "medical certificates now require absences.view_medical (GDPR art. 9). Granted to HR, "
            + "which already held employee_documents.view_sensitive and so already saw this data. "
            + "Deliberately NOT granted to planning-side templates: they keep absences.view, which "
            + "still shows who is absent and between which dates.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["hr"] = [PermissionCodes.AbsencesViewMedical],
            }),

        new(23,
            "Inventory/tasks/notifications sprint 2026-08-01: every employee-facing template gets "
            + "personal tasks (view_own/manage_own). Magazijn additionally operates the reorder "
            + "flow and sees movements/loans but never sets thresholds or overrides negative "
            + "stock; management owns thresholds, the full task surface, bulk/portal messaging "
            + "and escalation rules; HR coordinates team tasks and bulk employee messages.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["magazijn"] =
                [
                    PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn,
                    PermissionCodes.InventoryViewMovements, PermissionCodes.InventoryReorderView,
                    PermissionCodes.InventoryReorderManage, PermissionCodes.InventoryLoansView,
                ],
                ["management"] =
                [
                    PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn,
                    PermissionCodes.TasksViewTeam, PermissionCodes.TasksViewAll,
                    PermissionCodes.TasksAssign, PermissionCodes.TasksEdit, PermissionCodes.TasksCancel,
                    PermissionCodes.TasksReview, PermissionCodes.TasksReopen,
                    PermissionCodes.TasksManageCategories, PermissionCodes.TasksManageTemplates,
                    PermissionCodes.TasksManageRecurring,
                    PermissionCodes.InventoryManageThresholds, PermissionCodes.InventoryViewMovements,
                    PermissionCodes.InventoryReorderView, PermissionCodes.InventoryReorderManage,
                    PermissionCodes.InventoryLoansView,
                    PermissionCodes.MessagesSendBulk, PermissionCodes.MessagesViewDeliveryStatus,
                    PermissionCodes.MessagesCancel,
                    PermissionCodes.PortalMessagesView, PermissionCodes.PortalMessagesSend,
                    PermissionCodes.PortalMessagesSendBulk, PermissionCodes.PortalMessagesCancel,
                    PermissionCodes.EscalationsManage,
                ],
                ["hr"] =
                [
                    PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn,
                    PermissionCodes.TasksViewTeam, PermissionCodes.TasksAssign, PermissionCodes.TasksReview,
                    PermissionCodes.InventoryLoansView,
                    PermissionCodes.MessagesSendBulk, PermissionCodes.MessagesViewDeliveryStatus,
                ],
                ["planner"] = [PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn],
                ["dispatcher"] = [PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn],
                ["boekhouding"] = [PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn],
                ["chauffeur"] = [PermissionCodes.TasksViewOwn, PermissionCodes.TasksManageOwn],
            }),

        new(24,
            "Order usability/pricing coverage wave 2026-08-04: confirming a price with unpriced "
            + "goods lines is a separate, explicitly granted decision (reason required, warning "
            + "stays attached) — management and boekhouding only.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] = [PermissionCodes.OrdersConfirmIncompletePrice],
                ["boekhouding"] = [PermissionCodes.OrdersConfirmIncompletePrice],
            }),

        new(25,
            "Master-data wave 2026-08-05: location access codes are sensitive site data "
            + "(locations.view_sensitive). Granted to the operational templates that brief "
            + "drivers and plan site visits — planner, dispatcher and management.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] = [PermissionCodes.LocationsViewSensitive],
                ["dispatcher"] = [PermissionCodes.LocationsViewSensitive],
                ["management"] = [PermissionCodes.LocationsViewSensitive],
            }),

        new(26,
            "Dossier foundation wave 2026-08-11: tenant-configurable activity types. The "
            + "operational templates read the catalogue (activity_types.view); management "
            + "also maintains it (activity_types.manage).",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] = [PermissionCodes.ActivityTypesView],
                ["dispatcher"] = [PermissionCodes.ActivityTypesView],
                ["management"] = [PermissionCodes.ActivityTypesView, PermissionCodes.ActivityTypesManage],
            }),
        new(27,
            "Commercial foundation wave 2026-08-12: moving a dossier/order to another issuing "
            + "entity than the customer default becomes a separate audited right with a "
            + "mandatory reason — dossiers.manage alone no longer suffices.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] = [PermissionCodes.DossiersOverrideEntity],
                ["boekhouding"] = [PermissionCodes.DossiersOverrideEntity],
            }),
        new(28,
            "Problems wave 2026-08-12: charging a problem to the customer is an approval-gated "
            + "decision (creates the sales line) — proposing stays incidents.manage, deciding "
            + "needs problems.approve_charge.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] = [PermissionCodes.ProblemsApproveCharge],
                ["boekhouding"] = [PermissionCodes.ProblemsApproveCharge],
            }),
        new(29,
            "Settings/system wave 2026-08: Systeeminformatie + back-upoverzicht voor management. "
            + "Back-upacties (create/download/delete/restore) krijgen BEWUST geen sjabloon — "
            + "die kent een beheerder per persoon toe.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["management"] = [PermissionCodes.SystemInfoView, PermissionCodes.BackupsView],
            }),

        new(30,
            "Attendance wave 2026-08-20: urenregistratie + prikklok. attendance.self (eigen "
            + "punches/historiek) voor alle interne medewerker-sjablonen incl. chauffeur; HR "
            + "krijgt overzicht, correcties, rapporten, PIN-beheer en instellingen; management "
            + "kijkt en rapporteert. Dispatcher krijgt BEWUST geen attendance.view (least "
            + "privilege) en kioskbeheer (manage_kiosks) blijft bij de beheerder per persoon.",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["planner"] = [PermissionCodes.AttendanceSelf],
                ["dispatcher"] = [PermissionCodes.AttendanceSelf],
                ["boekhouding"] = [PermissionCodes.AttendanceSelf],
                ["magazijn"] = [PermissionCodes.AttendanceSelf],
                ["chauffeur"] = [PermissionCodes.AttendanceSelf],
                ["hr"] =
                [
                    PermissionCodes.AttendanceSelf, PermissionCodes.AttendanceView,
                    PermissionCodes.AttendanceCorrect, PermissionCodes.AttendanceReport,
                    PermissionCodes.AttendanceManageCredentials, PermissionCodes.AttendanceManageSettings,
                ],
                ["management"] =
                [
                    PermissionCodes.AttendanceSelf, PermissionCodes.AttendanceView,
                    PermissionCodes.AttendanceReport,
                ],
            }),
    ];
}
