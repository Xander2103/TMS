using TransportationService.Api.Modules.Identity;

namespace TransportationService.Api.Data;

/// <summary>
/// Sensible default (non-system) roles seeded once per tenant. Tenant administrators may
/// rename them or adjust their permissions afterwards — the seeder never re-syncs an
/// existing role, so customisation is preserved. Administrator itself stays the only
/// system role (full catalog, managed by PermissionCatalogSeeder).
/// </summary>
public static class DefaultRoleDefinitions
{
    /// <summary>
    /// <paramref name="Code"/> is the STABLE template identity stamped on seeded roles
    /// (Role.TemplateCode); upgrades match on it, never on the display name.
    /// </summary>
    public sealed record RoleTemplate(string Code, string Name, string Description, IReadOnlyList<string> PermissionCodes);

    private static readonly string[] CommonViewPermissions =
    [
        PermissionCodes.DashboardView,
        PermissionCodes.CustomersView,
        PermissionCodes.LocationsView,
        PermissionCodes.DriversView,
        PermissionCodes.VehiclesView,
        PermissionCodes.TrailersView,
        PermissionCodes.EmployeesView,
        PermissionCodes.AbsencesView,
        PermissionCodes.DepartmentsView,
        PermissionCodes.JobFunctionsView,
        PermissionCodes.VehicleCategoriesView,
        PermissionCodes.TrailerCategoriesView,
        PermissionCodes.DriverCategoriesView,
        PermissionCodes.CustomerCategoriesView,
        PermissionCodes.ContactDepartmentsView,
        PermissionCodes.ReferenceDataView,
        PermissionCodes.LeaveBalancesViewOwn,
    ];

    private const string ManagementDescription =
        "Leest alles en beslist mee: keurt afwezigheden en doorrekeningen goed, overschrijft planningsbeperkingen en prijzen, "
        + "beheert tarieven, dossiers, vlootfinanciën en Peppol-verzending.";

    /// <summary>
    /// Earlier seeded descriptions that were wrong, per template code. The seeder replaces a stamped
    /// role's description only while it still equals one of these exact texts, so a description a
    /// tenant edited is never touched (no role-template version: no permission changes).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> SupersededDescriptions { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["management"] = ["Leest alles, keurt afwezigheden goed en mag planningsbeperkingen overschrijven."],
        };

    public static IReadOnlyList<RoleTemplate> All { get; } =
    [
        new("planner", "Planner",
            "Plant transportopdrachten en ritten; beheert klanten en locaties.",
            [
                .. CommonViewPermissions,
                PermissionCodes.OrdersView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit,
                PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersCorrectStatus, PermissionCodes.OrdersCancel,
                PermissionCodes.OrdersAssign, PermissionCodes.OrdersExport,
                PermissionCodes.PlanningView, PermissionCodes.PlanningCreate, PermissionCodes.PlanningEdit,
                PermissionCodes.ScanningView,
                PermissionCodes.ExceptionsView, PermissionCodes.ExceptionsResolve,
                PermissionCodes.PodView,
                PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage,
                PermissionCodes.EmployeePlanningConflictOverride,
                PermissionCodes.TripCostsView,
                PermissionCodes.MessagingManage, PermissionCodes.MessageTemplatesManage, PermissionCodes.MessagesSend,
                PermissionCodes.CustomersCreate, PermissionCodes.CustomersEdit, PermissionCodes.CustomersDeactivate,
                // MD-M8 (v38): no customers.manage_fiscal — fiscal / bank / Peppol data is accounting's.
                PermissionCodes.CustomersManageCommunication,
                PermissionCodes.CustomersManageSurcharge, PermissionCodes.CustomersManagePo,
                PermissionCodes.LocationsCreate, PermissionCodes.LocationsEdit,
                PermissionCodes.EmployeeDocumentsView,
                PermissionCodes.PackagesView, PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage,
                PermissionCodes.PackagesCancel, PermissionCodes.PackagesRelabel,
                PermissionCodes.PackageExceptionsCreate, PermissionCodes.PackageExceptionsManage,
                PermissionCodes.ScanningOverride,
                PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
                PermissionCodes.PackageReportsExport,
                PermissionCodes.DossiersView, PermissionCodes.DossiersManage, PermissionCodes.DossiersPrice, PermissionCodes.DossiersReopen,
                PermissionCodes.ActivityTypesView,
                PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                PermissionCodes.ProblemsCreateRedelivery,
                PermissionCodes.TariffsView,
                PermissionCodes.ReportsView,
                PermissionCodes.OperationsView, PermissionCodes.OperationsManageAlerts,
                PermissionCodes.WarehouseSchedule, PermissionCodes.WarehouseConflictOverride,
                PermissionCodes.ContactDepartmentsManage,
                PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend,
                PermissionCodes.AttendanceSelf,
            ]),

        new("dispatcher", "Dispatcher",
            "Stuurt de dagelijkse uitvoering aan: ritten, statussen en chauffeursopvolging.",
            [
                .. CommonViewPermissions,
                PermissionCodes.OrdersView, PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersAssign,
                PermissionCodes.PlanningView, PermissionCodes.PlanningCreate, PermissionCodes.PlanningEdit,
                PermissionCodes.DriverWorkflowView,
                PermissionCodes.ScanningView, PermissionCodes.ScanningCorrect,
                PermissionCodes.ExceptionsView, PermissionCodes.ExceptionsCreate, PermissionCodes.ExceptionsResolve,
                PermissionCodes.PodView, PermissionCodes.PodCorrect,
                PermissionCodes.EmployeePlanningView,
                PermissionCodes.EmployeeDocumentsView,
                PermissionCodes.PackagesView,
                PermissionCodes.PackageExceptionsCreate, PermissionCodes.PackageExceptionsManage,
                PermissionCodes.ScanningOverride,
                PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
                PermissionCodes.DossiersView,
                PermissionCodes.ActivityTypesView,
                PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                // Exception remediation (v40): the controlled redelivery command, no general orders.create.
                PermissionCodes.ProblemsCreateRedelivery,
                PermissionCodes.ReportsView,
                PermissionCodes.OperationsView, PermissionCodes.OperationsManageAlerts,
                PermissionCodes.WarehouseSchedule,
                PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend,
                PermissionCodes.AttendanceSelf,
            ]),

        // Security M4: NOT read-only. The accepted policy (v13/v14/v21/v24/v27/v28) gives Management
        // commercial and financial decisions; the description says so instead of "leest alles".
        new("management", "Management",
            ManagementDescription,
            [
                .. CommonViewPermissions,
                PermissionCodes.OrdersView, PermissionCodes.OrdersExport,
                PermissionCodes.PlanningView, PermissionCodes.PlanningOverrideRestriction,
                PermissionCodes.ScanningView,
                PermissionCodes.ExceptionsView,
                PermissionCodes.PodView,
                PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage,
                PermissionCodes.EmployeePlanningConflictOverride,
                PermissionCodes.TripCostsView, PermissionCodes.TripCostsManage, PermissionCodes.TripCostsOverride,
                PermissionCodes.ProfitabilityView,
                PermissionCodes.KpiView, PermissionCodes.KpiExport,
                PermissionCodes.MessagingManage, PermissionCodes.MessageTemplatesManage, PermissionCodes.MessagesSend,
                PermissionCodes.EdiView, PermissionCodes.EdiManage, PermissionCodes.EdiTest, PermissionCodes.EdiRetry,
                PermissionCodes.IntegrationsManage,
                PermissionCodes.InvoicesView,
                PermissionCodes.InvoiceAttachmentsView,
                PermissionCodes.AbsencesApprove,
                PermissionCodes.AuditLogsView,
                PermissionCodes.EmployeeDocumentsView,
                PermissionCodes.EmployeesViewConfidential,
                PermissionCodes.FleetDocumentsView, PermissionCodes.MaintenanceView,
                PermissionCodes.MaintenancePoliciesView, PermissionCodes.MaintenancePoliciesManage,
                PermissionCodes.InspectionsView, PermissionCodes.DamageReportsView,
                PermissionCodes.TachographView, PermissionCodes.TachographManage,
                PermissionCodes.FleetFinanceView, PermissionCodes.FleetFinanceManage,
                PermissionCodes.TankCardsView, PermissionCodes.FuelView,
                PermissionCodes.CompanySettingsView,
                PermissionCodes.UsersView, PermissionCodes.RolesView,
                PermissionCodes.CustomersDeactivate,
                PermissionCodes.PackagesView,
                PermissionCodes.WarehouseView,
                PermissionCodes.InventoryView,
                PermissionCodes.PackageReportsExport,
                PermissionCodes.DossiersView, PermissionCodes.DossiersManage, PermissionCodes.DossiersPrice, PermissionCodes.DossiersReopen,
                PermissionCodes.ActivityTypesView, PermissionCodes.ActivityTypesManage,
                PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage, PermissionCodes.ProblemsApproveCharge,
                PermissionCodes.TariffsView, PermissionCodes.TariffsManage,
                PermissionCodes.ReportsView,
                PermissionCodes.OperationsView,
                PermissionCodes.ProfitabilityExport,
                PermissionCodes.LegalEntitiesView,
                PermissionCodes.LeaveBalancesView,
                PermissionCodes.AccountingView,
                PermissionCodes.EmployeeNotesView, PermissionCodes.EmployeeNotesManage, PermissionCodes.EmployeeNotesPin,
                PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend,
                PermissionCodes.PortalAnnouncementsManage,
                PermissionCodes.PeppolView, PermissionCodes.PeppolConfigure, PermissionCodes.PeppolValidate,
                PermissionCodes.PeppolSend, PermissionCodes.PeppolRetry, PermissionCodes.PeppolViewIncoming,
                PermissionCodes.AttendanceSelf, PermissionCodes.AttendanceView, PermissionCodes.AttendanceReport,
            ]),

        new("boekhouding", "Boekhouding",
            "Beheert facturen en factuurgegevens van klanten.",
            [
                PermissionCodes.DashboardView,
                PermissionCodes.InvoicesView, PermissionCodes.InvoicesCreate, PermissionCodes.InvoicesEdit,
                PermissionCodes.InvoicesDelete, PermissionCodes.InvoicesChangeStatus,
                PermissionCodes.InvoiceAttachmentsView, PermissionCodes.InvoiceAttachmentsManage,
                PermissionCodes.OrdersView, PermissionCodes.OrdersExport,
                PermissionCodes.PodView,
                PermissionCodes.CustomersView, PermissionCodes.CustomersEdit,
                PermissionCodes.LocationsView, // MD-M8 (v38): the address picker of customer / invoice data
                PermissionCodes.CustomerCategoriesView, PermissionCodes.ContactDepartmentsView,
                PermissionCodes.ContactDepartmentsManage, PermissionCodes.ReferenceDataView,
                PermissionCodes.CompanySettingsView,
                PermissionCodes.TripCostsView, PermissionCodes.ProfitabilityView,
                PermissionCodes.KpiView, PermissionCodes.KpiExport,
                PermissionCodes.PackageReportsExport,
                PermissionCodes.DossiersView, PermissionCodes.IncidentsView,
                // EX-06: accounting decides on charging a problem to the customer (since v28) — without incidents.manage.
                PermissionCodes.ProblemsApproveCharge,
                PermissionCodes.TariffsView, PermissionCodes.TariffsManage,
                PermissionCodes.ReportsView,
                PermissionCodes.ProfitabilityExport,
                PermissionCodes.LegalEntitiesView, PermissionCodes.LegalEntitiesManage,
                PermissionCodes.InvoicesOverrideNumber,
                PermissionCodes.CustomersImport, PermissionCodes.CustomersOverrideNumber,
                PermissionCodes.CustomersManageFiscal, PermissionCodes.CustomersManageCommunication,
                PermissionCodes.CustomersManageSurcharge, PermissionCodes.CustomersManagePo,
                PermissionCodes.AccountingView, PermissionCodes.AccountingManage,
                PermissionCodes.PeppolView, PermissionCodes.PeppolValidate,
                PermissionCodes.PeppolSend, PermissionCodes.PeppolRetry, PermissionCodes.PeppolViewIncoming,
                PermissionCodes.AttendanceSelf,
                // FL-M12: fleet costs are accounting-relevant — read-only, never fleet management.
                PermissionCodes.VehiclesView, PermissionCodes.TrailersView,
                PermissionCodes.MaintenanceView, PermissionCodes.DamageReportsView, PermissionCodes.FuelView,
                PermissionCodes.FleetFinanceView, PermissionCodes.TachographView,
            ]),

        // Wagenparkbeheer: the fleet manager runs vehicles, trailers and their legal/technical
        // follow-up, including fleet costs — no user administration, HR or invoicing.
        new("wagenparkbeheerder", "Wagenparkbeheerder",
            "Beheert voertuigen, opleggers, documenten, keuringen, onderhoud en schade.",
            [
                .. CommonViewPermissions,
                PermissionCodes.VehiclesCreate, PermissionCodes.VehiclesEdit, PermissionCodes.VehiclesDelete,
                PermissionCodes.TrailersCreate, PermissionCodes.TrailersEdit, PermissionCodes.TrailersDelete,
                PermissionCodes.VehicleCategoriesManage, PermissionCodes.TrailerCategoriesManage,
                PermissionCodes.FleetDocumentsView, PermissionCodes.FleetDocumentsCreate,
                PermissionCodes.FleetDocumentsEdit, PermissionCodes.FleetDocumentsDelete,
                PermissionCodes.InspectionsView, PermissionCodes.InspectionsCreate,
                PermissionCodes.InspectionsEdit, PermissionCodes.InspectionsDelete,
                PermissionCodes.MaintenanceView, PermissionCodes.MaintenanceCreate,
                PermissionCodes.MaintenanceEdit, PermissionCodes.MaintenanceDelete,
                PermissionCodes.MaintenancePoliciesView, PermissionCodes.MaintenancePoliciesManage,
                PermissionCodes.DamageReportsView, PermissionCodes.DamageReportsCreate,
                PermissionCodes.DamageReportsEdit, PermissionCodes.DamageReportsDelete,
                PermissionCodes.FuelView, PermissionCodes.FuelCreate, PermissionCodes.FuelEdit, PermissionCodes.FuelDelete,
                PermissionCodes.TankCardsView, PermissionCodes.TankCardsCreate, PermissionCodes.TankCardsEdit,
                PermissionCodes.TankCardsDelete, PermissionCodes.TankCardsBlock,
                PermissionCodes.TachographView, PermissionCodes.TachographManage,
                PermissionCodes.FleetFinanceView, PermissionCodes.FleetFinanceManage,
                PermissionCodes.KpiView,
                PermissionCodes.PlanningView,
                PermissionCodes.AttendanceSelf,
            ]),

        new("hr", "HR",
            "Personeelszaken: medewerkers, personeelsplanning, afwezigheden en kwalificaties.",
            [
                PermissionCodes.DashboardView,
                PermissionCodes.EmployeesView, PermissionCodes.EmployeesCreate, PermissionCodes.EmployeesEdit,
                PermissionCodes.EmployeesDeactivate, PermissionCodes.EmployeesViewConfidential,
                PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage,
                PermissionCodes.EmployeePlanningConflictOverride,
                PermissionCodes.AbsencesView, PermissionCodes.AbsencesCreate, PermissionCodes.AbsencesEdit,
                PermissionCodes.AbsencesDelete, PermissionCodes.AbsencesApprove,
                PermissionCodes.AbsencesViewMedical,
                PermissionCodes.EmployeeDocumentsView, PermissionCodes.EmployeeDocumentsCreate,
                PermissionCodes.EmployeeDocumentsEdit, PermissionCodes.EmployeeDocumentsDelete,
                PermissionCodes.EmployeeDocumentsApprove,
                PermissionCodes.EmployeeDocumentsViewSensitive, PermissionCodes.HrSettingsManage,
                PermissionCodes.EmployeeNotesView, PermissionCodes.EmployeeNotesManage, PermissionCodes.EmployeeNotesPin,
                PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage, PermissionCodes.IssuedItemsManageTemplates,
                PermissionCodes.InventoryView, PermissionCodes.InventoryManage, PermissionCodes.InventoryAdjust,
                PermissionCodes.InventoryOverrideNegativeStock,
                PermissionCodes.InventoryIssue, PermissionCodes.InventoryReturn, PermissionCodes.InventoryLoansView,
                PermissionCodes.LeaveBalancesView, PermissionCodes.LeaveBalancesManage, PermissionCodes.LeaveBalancesAdjust,
                PermissionCodes.LeaveBalancesViewOwn, PermissionCodes.LeaveTypesManage,
                PermissionCodes.AttendanceSelf, PermissionCodes.AttendanceView, PermissionCodes.AttendanceCorrect,
                PermissionCodes.AttendanceReport, PermissionCodes.AttendanceManageCredentials,
                PermissionCodes.AttendanceManageSettings,
                PermissionCodes.MessagesSend,
                // HR master data (afdelingen, functies, contracttypes) is HR's own to maintain.
                PermissionCodes.DepartmentsView, PermissionCodes.DepartmentsManage,
                PermissionCodes.JobFunctionsView, PermissionCodes.JobFunctionsManage,
                PermissionCodes.ReferenceDataView, PermissionCodes.ReferenceDataManage,
                PermissionCodes.ContactDepartmentsView,
                PermissionCodes.ReportsView,
            ]),

        new("chauffeur", "Chauffeur",
            "Voert eigen ritten uit en beheert eigen afwezigheden.",
            [
                PermissionCodes.DriverWorkflowView, PermissionCodes.DriverWorkflowExecute,
                PermissionCodes.ScanningView, PermissionCodes.ScanningExecute,
                PermissionCodes.ExceptionsCreate,
                PermissionCodes.PodFinalize,
                // Own absences run through the self-scoped portal (/api/me/absences); absences.view /
                // absences.create are company-wide and deliberately NOT granted (HR acceptance B1).
                PermissionCodes.LeaveBalancesViewOwn,
                PermissionCodes.AttendanceSelf,
            ]),

        // Deliberately NO internal orders.view: portal users reach only the customer-portal
        // endpoints, which scope every query to their linked customer. Every portal user gets
        // this base role — Documenten/Facturen/Gebruikersbeheer are OPTIONAL add-ons granted
        // per user via the three templates below (composed with this one, never replacing it).
        new("klantportaal", "Klantportaal",
            "Klantgebruikers: eigen opdrachten bekijken en indienen via het klantportaal.",
            [
                PermissionCodes.CustomerPortalView,
                PermissionCodes.CustomerPortalSubmitOrders,
                PermissionCodes.CustomerPortalManageLocations,
                PermissionCodes.CustomerPortalMessages,
            ]),

        // Optional add-on templates composing extra portal capability onto a klantportaal user;
        // never assigned alone. See CustomerPortalUserService — invite/grants toggle these by
        // TemplateCode, and never expose any other role to a portal user.
        new("klantportaal_documenten", "Klantportaal - Documenten",
            "Optionele klantportaal-uitbreiding: documenten van eigen opdrachten bekijken.",
            [
                PermissionCodes.CustomerPortalViewDocuments,
            ]),

        new("klantportaal_facturen", "Klantportaal - Facturen",
            "Optionele klantportaal-uitbreiding: eigen facturen bekijken.",
            [
                PermissionCodes.CustomerPortalViewInvoices,
            ]),

        new("klantportaal_gebruikersbeheer", "Klantportaal - Gebruikersbeheer",
            "Optionele klantportaal-uitbreiding: klantgebruikers uitnodigen en beheren.",
            [
                PermissionCodes.CustomerPortalManageUsers,
            ]),

        // Warehouse: scans, packages, releases and incident reporting — deliberately no
        // HR, cost or profitability permissions.
        new("magazijn", "Magazijn",
            "Laadt en scant colli, geeft ritten vrij en meldt afwijkingen in het magazijn; ontvangt en corrigeert voorraad en geeft bedrijfsmiddelen uit en neemt ze terug.",
            [
                PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
                PermissionCodes.PackagesView,
                PermissionCodes.ScanningView, PermissionCodes.ScanningExecute,
                PermissionCodes.PackageExceptionsCreate,
                PermissionCodes.ExceptionsView, PermissionCodes.ExceptionsCreate,
                PermissionCodes.PlanningView,
                PermissionCodes.OperationsView,
                PermissionCodes.WarehouseManage, PermissionCodes.WarehouseSchedule,
                PermissionCodes.WarehouseConflictOverride,
                PermissionCodes.IssuedItemsView,
                PermissionCodes.InventoryView, PermissionCodes.InventoryAdjust,
                // WH-H4: the warehouse clerk runs the issue/return desk without any HR employee access
                // (the safe employee picker only). The item catalogue stays with HR (HR-wave decision).
                PermissionCodes.InventoryIssue, PermissionCodes.InventoryReturn,
                PermissionCodes.AttendanceSelf,
            ]),
    ];
}
