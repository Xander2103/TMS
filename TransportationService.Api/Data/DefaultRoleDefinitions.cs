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
                PermissionCodes.CustomersManageFiscal, PermissionCodes.CustomersManageCommunication,
                PermissionCodes.CustomersManageSurcharge, PermissionCodes.CustomersManagePo,
                PermissionCodes.LocationsCreate, PermissionCodes.LocationsEdit,
                PermissionCodes.EmployeeDocumentsView,
                PermissionCodes.PackagesView, PermissionCodes.PackagesCreate, PermissionCodes.PackagesManage,
                PermissionCodes.PackagesCancel, PermissionCodes.PackagesRelabel, PermissionCodes.PackagesExport,
                PermissionCodes.PackageExceptionsCreate, PermissionCodes.PackageExceptionsManage,
                PermissionCodes.ScanningOverride,
                PermissionCodes.WarehouseView, PermissionCodes.WarehouseReleaseTrip,
                PermissionCodes.PackageReportsExport,
                PermissionCodes.DossiersView, PermissionCodes.DossiersManage,
                PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                PermissionCodes.TariffsView,
                PermissionCodes.ReportsView,
                PermissionCodes.OperationsView, PermissionCodes.OperationsManageAlerts,
                PermissionCodes.WarehouseSchedule, PermissionCodes.WarehouseConflictOverride,
                PermissionCodes.LegalEntitiesView,
                PermissionCodes.ContactDepartmentsManage,
                PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend,
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
                PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
                PermissionCodes.ReportsView,
                PermissionCodes.OperationsView, PermissionCodes.OperationsManageAlerts,
                PermissionCodes.WarehouseSchedule,
                PermissionCodes.LegalEntitiesView,
                PermissionCodes.CustomerMessagesView, PermissionCodes.CustomerMessagesSend,
            ]),

        new("management", "Management",
            "Leest alles, keurt afwezigheden goed en mag planningsbeperkingen overschrijven.",
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
                PermissionCodes.DossiersView, PermissionCodes.DossiersManage,
                PermissionCodes.IncidentsView, PermissionCodes.IncidentsManage,
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
                PermissionCodes.CustomerCategoriesView, PermissionCodes.ContactDepartmentsView,
                PermissionCodes.ContactDepartmentsManage, PermissionCodes.ReferenceDataView,
                PermissionCodes.CompanySettingsView,
                PermissionCodes.TripCostsView, PermissionCodes.ProfitabilityView,
                PermissionCodes.KpiView, PermissionCodes.KpiExport,
                PermissionCodes.PackageReportsExport,
                PermissionCodes.DossiersView, PermissionCodes.IncidentsView,
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
            ]),

        new("hr", "HR",
            "Personeelszaken: medewerkers, personeelsplanning, afwezigheden en kwalificaties.",
            [
                PermissionCodes.DashboardView,
                PermissionCodes.EmployeesView, PermissionCodes.EmployeesCreate, PermissionCodes.EmployeesEdit,
                PermissionCodes.EmployeePlanningView, PermissionCodes.EmployeePlanningManage,
                PermissionCodes.EmployeePlanningConflictOverride,
                PermissionCodes.AbsencesView, PermissionCodes.AbsencesCreate, PermissionCodes.AbsencesEdit,
                PermissionCodes.AbsencesDelete, PermissionCodes.AbsencesApprove,
                PermissionCodes.EmployeeDocumentsView, PermissionCodes.EmployeeDocumentsCreate,
                PermissionCodes.EmployeeDocumentsEdit, PermissionCodes.EmployeeDocumentsDelete,
                PermissionCodes.EmployeeDocumentsViewSensitive, PermissionCodes.HrSettingsManage,
                PermissionCodes.EmployeeNotesView, PermissionCodes.EmployeeNotesManage, PermissionCodes.EmployeeNotesPin,
                PermissionCodes.IssuedItemsView, PermissionCodes.IssuedItemsManage, PermissionCodes.IssuedItemsManageTemplates,
                PermissionCodes.InventoryView, PermissionCodes.InventoryManage, PermissionCodes.InventoryAdjust,
                PermissionCodes.InventoryOverrideNegativeStock,
                PermissionCodes.LeaveBalancesView, PermissionCodes.LeaveBalancesManage, PermissionCodes.LeaveBalancesAdjust,
                PermissionCodes.LeaveBalancesViewOwn, PermissionCodes.LeaveTypesManage,
                PermissionCodes.MessagesSend,
                PermissionCodes.QualificationTypesView,
                PermissionCodes.DepartmentsView, PermissionCodes.JobFunctionsView,
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
                PermissionCodes.AbsencesView, PermissionCodes.AbsencesCreate,
                PermissionCodes.LeaveBalancesViewOwn,
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
            "Laadt en scant colli, geeft ritten vrij en meldt afwijkingen in het magazijn.",
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
            ]),
    ];
}
