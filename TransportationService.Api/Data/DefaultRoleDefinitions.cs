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
    public sealed record RoleTemplate(string Name, string Description, IReadOnlyList<string> PermissionCodes);

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
        PermissionCodes.ReferenceDataView,
    ];

    public static IReadOnlyList<RoleTemplate> All { get; } =
    [
        new("Planner",
            "Plant transportopdrachten en ritten; beheert klanten en locaties.",
            [
                .. CommonViewPermissions,
                PermissionCodes.OrdersView, PermissionCodes.OrdersCreate, PermissionCodes.OrdersEdit,
                PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersCancel, PermissionCodes.OrdersAssign,
                PermissionCodes.OrdersExport,
                PermissionCodes.PlanningView, PermissionCodes.PlanningCreate, PermissionCodes.PlanningEdit,
                PermissionCodes.ScanningView,
                PermissionCodes.ExceptionsView, PermissionCodes.ExceptionsResolve,
                PermissionCodes.PodView,
                PermissionCodes.CustomersCreate, PermissionCodes.CustomersEdit,
                PermissionCodes.LocationsCreate, PermissionCodes.LocationsEdit,
                PermissionCodes.EmployeeDocumentsView,
            ]),

        new("Dispatcher",
            "Stuurt de dagelijkse uitvoering aan: ritten, statussen en chauffeursopvolging.",
            [
                .. CommonViewPermissions,
                PermissionCodes.OrdersView, PermissionCodes.OrdersChangeStatus, PermissionCodes.OrdersAssign,
                PermissionCodes.PlanningView, PermissionCodes.PlanningCreate, PermissionCodes.PlanningEdit,
                PermissionCodes.DriverWorkflowView,
                PermissionCodes.ScanningView, PermissionCodes.ScanningCorrect,
                PermissionCodes.ExceptionsView, PermissionCodes.ExceptionsCreate, PermissionCodes.ExceptionsResolve,
                PermissionCodes.PodView, PermissionCodes.PodCorrect,
                PermissionCodes.EmployeeDocumentsView,
            ]),

        new("Management",
            "Leest alles, keurt afwezigheden goed en mag planningsbeperkingen overschrijven.",
            [
                .. CommonViewPermissions,
                PermissionCodes.OrdersView, PermissionCodes.OrdersExport,
                PermissionCodes.PlanningView, PermissionCodes.PlanningOverrideRestriction,
                PermissionCodes.ScanningView,
                PermissionCodes.ExceptionsView,
                PermissionCodes.PodView,
                PermissionCodes.InvoicesView,
                PermissionCodes.AbsencesApprove,
                PermissionCodes.AuditLogsView,
                PermissionCodes.EmployeeDocumentsView,
                PermissionCodes.EmployeesViewConfidential,
                PermissionCodes.FleetDocumentsView, PermissionCodes.MaintenanceView,
                PermissionCodes.InspectionsView, PermissionCodes.DamageReportsView,
                PermissionCodes.TankCardsView, PermissionCodes.FuelView,
                PermissionCodes.CompanySettingsView,
                PermissionCodes.UsersView, PermissionCodes.RolesView,
            ]),

        new("Boekhouding",
            "Beheert facturen en factuurgegevens van klanten.",
            [
                PermissionCodes.DashboardView,
                PermissionCodes.InvoicesView, PermissionCodes.InvoicesCreate, PermissionCodes.InvoicesEdit,
                PermissionCodes.InvoicesDelete, PermissionCodes.InvoicesChangeStatus,
                PermissionCodes.OrdersView, PermissionCodes.OrdersExport,
                PermissionCodes.PodView,
                PermissionCodes.CustomersView, PermissionCodes.CustomersEdit,
                PermissionCodes.CustomerCategoriesView, PermissionCodes.ReferenceDataView,
                PermissionCodes.CompanySettingsView,
            ]),

        new("Chauffeur",
            "Voert eigen ritten uit en beheert eigen afwezigheden.",
            [
                PermissionCodes.DriverWorkflowView, PermissionCodes.DriverWorkflowExecute,
                PermissionCodes.ScanningView, PermissionCodes.ScanningExecute,
                PermissionCodes.ExceptionsCreate,
                PermissionCodes.PodFinalize,
                PermissionCodes.AbsencesView, PermissionCodes.AbsencesCreate,
            ]),

        new("Klantportaal",
            "Leestoegang voor klantgebruikers (toekomstig klantportaal).",
            [
                PermissionCodes.OrdersView,
            ]),
    ];
}
