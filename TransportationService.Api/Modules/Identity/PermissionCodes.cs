namespace TransportationService.Api.Modules.Identity;

public static class PermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersBlock = "users.block";

    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesDelete = "roles.delete";
    public const string RolesManagePermissions = "roles.manage_permissions";

    public const string EmployeesView = "employees.view";
    public const string EmployeesCreate = "employees.create";
    public const string EmployeesEdit = "employees.edit";
    public const string EmployeesDeactivate = "employees.deactivate";
    public const string EmployeesViewConfidential = "employees.view_confidential";

    public const string EmployeeDocumentsView = "employee_documents.view";
    public const string EmployeeDocumentsCreate = "employee_documents.create";
    public const string EmployeeDocumentsEdit = "employee_documents.edit";
    public const string EmployeeDocumentsDelete = "employee_documents.delete";
    public const string EmployeeDocumentsApprove = "employee_documents.approve";

    public const string PlanningView = "planning.view";
    public const string PlanningCreate = "planning.create";
    public const string PlanningEdit = "planning.edit";
    public const string PlanningOverrideRestriction = "planning.override_restriction";

    public const string AuditLogsView = "audit_logs.view";

    // --- Organisation master data ---
    public const string DepartmentsView = "departments.view";
    public const string DepartmentsManage = "departments.manage";
    public const string JobFunctionsView = "job_functions.view";
    public const string JobFunctionsManage = "job_functions.manage";

    // --- Classification categories ---
    public const string VehicleCategoriesView = "vehicle_categories.view";
    public const string VehicleCategoriesManage = "vehicle_categories.manage";
    public const string TrailerCategoriesView = "trailer_categories.view";
    public const string TrailerCategoriesManage = "trailer_categories.manage";
    public const string DriverCategoriesView = "driver_categories.view";
    public const string DriverCategoriesManage = "driver_categories.manage";
    public const string CustomerCategoriesView = "customer_categories.view";
    public const string CustomerCategoriesManage = "customer_categories.manage";

    // --- Reference data (countries, languages, nationalities, contract types) ---
    public const string ReferenceDataView = "reference_data.view";
    public const string ReferenceDataManage = "reference_data.manage";

    // --- Customers ---
    public const string CustomersView = "customers.view";
    public const string CustomersCreate = "customers.create";
    public const string CustomersEdit = "customers.edit";
    public const string CustomersDelete = "customers.delete";

    // --- Locations (sites/addresses) ---
    public const string LocationsView = "locations.view";
    public const string LocationsCreate = "locations.create";
    public const string LocationsEdit = "locations.edit";
    public const string LocationsDelete = "locations.delete";

    // --- Drivers ---
    public const string DriversView = "drivers.view";
    public const string DriversCreate = "drivers.create";
    public const string DriversEdit = "drivers.edit";
    public const string DriversDelete = "drivers.delete";
    public const string DriversBlock = "drivers.block";

    // --- Company settings ---
    public const string CompanySettingsView = "company_settings.view";
    public const string CompanySettingsManage = "company_settings.manage";

    // --- Qualification types (catalog) ---
    public const string QualificationTypesView = "qualification_types.view";
    public const string QualificationTypesManage = "qualification_types.manage";

    // --- Vehicles (fleet) ---
    public const string VehiclesView = "vehicles.view";
    public const string VehiclesCreate = "vehicles.create";
    public const string VehiclesEdit = "vehicles.edit";
    public const string VehiclesDelete = "vehicles.delete";

    // --- Trailers (fleet) ---
    public const string TrailersView = "trailers.view";
    public const string TrailersCreate = "trailers.create";
    public const string TrailersEdit = "trailers.edit";
    public const string TrailersDelete = "trailers.delete";

    // --- Fleet documents (vehicle/trailer certificates & papers) ---
    public const string FleetDocumentsView = "fleet_documents.view";
    public const string FleetDocumentsCreate = "fleet_documents.create";
    public const string FleetDocumentsEdit = "fleet_documents.edit";
    public const string FleetDocumentsDelete = "fleet_documents.delete";

    // --- Maintenance (vehicle/trailer service jobs) ---
    public const string MaintenanceView = "maintenance.view";
    public const string MaintenanceCreate = "maintenance.create";
    public const string MaintenanceEdit = "maintenance.edit";
    public const string MaintenanceDelete = "maintenance.delete";

    // --- Inspections (vehicle/trailer/crane) ---
    public const string InspectionsView = "inspections.view";
    public const string InspectionsCreate = "inspections.create";
    public const string InspectionsEdit = "inspections.edit";
    public const string InspectionsDelete = "inspections.delete";

    // --- Damage reports ---
    public const string DamageReportsView = "damage_reports.view";
    public const string DamageReportsCreate = "damage_reports.create";
    public const string DamageReportsEdit = "damage_reports.edit";
    public const string DamageReportsDelete = "damage_reports.delete";

    // --- Tank cards ---
    public const string TankCardsView = "tank_cards.view";
    public const string TankCardsCreate = "tank_cards.create";
    public const string TankCardsEdit = "tank_cards.edit";
    public const string TankCardsDelete = "tank_cards.delete";
    public const string TankCardsBlock = "tank_cards.block";

    // --- Fuel transactions ---
    public const string FuelView = "fuel.view";
    public const string FuelCreate = "fuel.create";
    public const string FuelEdit = "fuel.edit";
    public const string FuelDelete = "fuel.delete";

    // --- Transport orders ---
    public const string OrdersView = "orders.view";
    public const string OrdersCreate = "orders.create";
    public const string OrdersEdit = "orders.edit";
    public const string OrdersDelete = "orders.delete";
    public const string OrdersChangeStatus = "orders.change_status";

    // --- Invoices ---
    public const string InvoicesView = "invoices.view";
    public const string InvoicesCreate = "invoices.create";
    public const string InvoicesEdit = "invoices.edit";
    public const string InvoicesDelete = "invoices.delete";
    public const string InvoicesChangeStatus = "invoices.change_status";

    // --- Driver workflow (trip execution) ---
    public const string DriverWorkflowView = "driver_workflow.view";
    public const string DriverWorkflowExecute = "driver_workflow.execute";

    // --- Absences (HR availability) ---
    public const string AbsencesView = "absences.view";
    public const string AbsencesCreate = "absences.create";
    public const string AbsencesEdit = "absences.edit";
    public const string AbsencesDelete = "absences.delete";
    public const string AbsencesApprove = "absences.approve";

    public static readonly IReadOnlyList<(string Code, string Module, string Action, string Description)> All =
    [
        (UsersView, "users", "view", "Gebruikers bekijken"),
        (UsersCreate, "users", "create", "Gebruikers aanmaken"),
        (UsersEdit, "users", "edit", "Gebruikers bewerken"),
        (UsersDelete, "users", "delete", "Gebruikers verwijderen"),
        (UsersBlock, "users", "block", "Gebruikers blokkeren"),
        (RolesView, "roles", "view", "Rollen bekijken"),
        (RolesCreate, "roles", "create", "Rollen aanmaken"),
        (RolesEdit, "roles", "edit", "Rollen bewerken"),
        (RolesDelete, "roles", "delete", "Rollen verwijderen"),
        (RolesManagePermissions, "roles", "manage_permissions", "Rechten van rollen beheren"),
        (EmployeesView, "employees", "view", "Personeel bekijken"),
        (EmployeesCreate, "employees", "create", "Personeel aanmaken"),
        (EmployeesEdit, "employees", "edit", "Personeel bewerken"),
        (EmployeesDeactivate, "employees", "deactivate", "Personeel deactiveren"),
        (EmployeesViewConfidential, "employees", "view_confidential", "Vertrouwelijke personeelsgegevens bekijken"),
        (EmployeeDocumentsView, "employee_documents", "view", "Personeelsdocumenten bekijken"),
        (EmployeeDocumentsCreate, "employee_documents", "create", "Personeelsdocumenten toevoegen"),
        (EmployeeDocumentsEdit, "employee_documents", "edit", "Personeelsdocumenten bewerken"),
        (EmployeeDocumentsDelete, "employee_documents", "delete", "Personeelsdocumenten verwijderen"),
        (EmployeeDocumentsApprove, "employee_documents", "approve", "Personeelsdocumenten goedkeuren"),
        (PlanningView, "planning", "view", "Planning bekijken"),
        (PlanningCreate, "planning", "create", "Planning aanmaken"),
        (PlanningEdit, "planning", "edit", "Planning bewerken"),
        (PlanningOverrideRestriction, "planning", "override_restriction", "Planningsbeperkingen overschrijven"),
        (AuditLogsView, "audit_logs", "view", "Auditlogboek bekijken"),
        (DepartmentsView, "departments", "view", "Afdelingen bekijken"),
        (DepartmentsManage, "departments", "manage", "Afdelingen beheren"),
        (JobFunctionsView, "job_functions", "view", "Functies bekijken"),
        (JobFunctionsManage, "job_functions", "manage", "Functies beheren"),
        (VehicleCategoriesView, "vehicle_categories", "view", "Voertuigcategorieën bekijken"),
        (VehicleCategoriesManage, "vehicle_categories", "manage", "Voertuigcategorieën beheren"),
        (TrailerCategoriesView, "trailer_categories", "view", "Opleggercategorieën bekijken"),
        (TrailerCategoriesManage, "trailer_categories", "manage", "Opleggercategorieën beheren"),
        (DriverCategoriesView, "driver_categories", "view", "Chauffeurcategorieën bekijken"),
        (DriverCategoriesManage, "driver_categories", "manage", "Chauffeurcategorieën beheren"),
        (CustomerCategoriesView, "customer_categories", "view", "Klantcategorieën bekijken"),
        (CustomerCategoriesManage, "customer_categories", "manage", "Klantcategorieën beheren"),
        (ReferenceDataView, "reference_data", "view", "Referentiegegevens bekijken"),
        (ReferenceDataManage, "reference_data", "manage", "Referentiegegevens beheren"),
        (CustomersView, "customers", "view", "Klanten bekijken"),
        (CustomersCreate, "customers", "create", "Klanten aanmaken"),
        (CustomersEdit, "customers", "edit", "Klanten bewerken"),
        (CustomersDelete, "customers", "delete", "Klanten verwijderen"),
        (LocationsView, "locations", "view", "Locaties bekijken"),
        (LocationsCreate, "locations", "create", "Locaties aanmaken"),
        (LocationsEdit, "locations", "edit", "Locaties bewerken"),
        (LocationsDelete, "locations", "delete", "Locaties verwijderen"),
        (DriversView, "drivers", "view", "Chauffeurs bekijken"),
        (DriversCreate, "drivers", "create", "Chauffeurs aanmaken"),
        (DriversEdit, "drivers", "edit", "Chauffeurs bewerken"),
        (DriversDelete, "drivers", "delete", "Chauffeurs verwijderen"),
        (DriversBlock, "drivers", "block", "Chauffeurs blokkeren"),
        (CompanySettingsView, "company_settings", "view", "Bedrijfsinstellingen bekijken"),
        (CompanySettingsManage, "company_settings", "manage", "Bedrijfsinstellingen beheren"),
        (QualificationTypesView, "qualification_types", "view", "Kwalificatietypes bekijken"),
        (QualificationTypesManage, "qualification_types", "manage", "Kwalificatietypes beheren"),
        (VehiclesView, "vehicles", "view", "Voertuigen bekijken"),
        (VehiclesCreate, "vehicles", "create", "Voertuigen aanmaken"),
        (VehiclesEdit, "vehicles", "edit", "Voertuigen bewerken"),
        (VehiclesDelete, "vehicles", "delete", "Voertuigen verwijderen"),
        (TrailersView, "trailers", "view", "Opleggers bekijken"),
        (TrailersCreate, "trailers", "create", "Opleggers aanmaken"),
        (TrailersEdit, "trailers", "edit", "Opleggers bewerken"),
        (TrailersDelete, "trailers", "delete", "Opleggers verwijderen"),
        (FleetDocumentsView, "fleet_documents", "view", "Voertuig- en opleggerdocumenten bekijken"),
        (FleetDocumentsCreate, "fleet_documents", "create", "Voertuig- en opleggerdocumenten toevoegen"),
        (FleetDocumentsEdit, "fleet_documents", "edit", "Voertuig- en opleggerdocumenten bewerken"),
        (FleetDocumentsDelete, "fleet_documents", "delete", "Voertuig- en opleggerdocumenten verwijderen"),
        (MaintenanceView, "maintenance", "view", "Onderhoud bekijken"),
        (MaintenanceCreate, "maintenance", "create", "Onderhoud plannen"),
        (MaintenanceEdit, "maintenance", "edit", "Onderhoud bewerken en afronden"),
        (MaintenanceDelete, "maintenance", "delete", "Onderhoud verwijderen"),
        (InspectionsView, "inspections", "view", "Keuringen bekijken"),
        (InspectionsCreate, "inspections", "create", "Keuringen plannen"),
        (InspectionsEdit, "inspections", "edit", "Keuringen bewerken en registreren"),
        (InspectionsDelete, "inspections", "delete", "Keuringen verwijderen"),
        (DamageReportsView, "damage_reports", "view", "Schademeldingen bekijken"),
        (DamageReportsCreate, "damage_reports", "create", "Schademeldingen aanmaken"),
        (DamageReportsEdit, "damage_reports", "edit", "Schademeldingen bewerken"),
        (DamageReportsDelete, "damage_reports", "delete", "Schademeldingen verwijderen"),
        (TankCardsView, "tank_cards", "view", "Tankkaarten bekijken"),
        (TankCardsCreate, "tank_cards", "create", "Tankkaarten aanmaken"),
        (TankCardsEdit, "tank_cards", "edit", "Tankkaarten bewerken"),
        (TankCardsDelete, "tank_cards", "delete", "Tankkaarten verwijderen"),
        (TankCardsBlock, "tank_cards", "block", "Tankkaarten blokkeren"),
        (FuelView, "fuel", "view", "Tankbeurten bekijken"),
        (FuelCreate, "fuel", "create", "Tankbeurten registreren"),
        (FuelEdit, "fuel", "edit", "Tankbeurten bewerken"),
        (FuelDelete, "fuel", "delete", "Tankbeurten verwijderen"),
        (OrdersView, "orders", "view", "Transportopdrachten bekijken"),
        (OrdersCreate, "orders", "create", "Transportopdrachten aanmaken"),
        (OrdersEdit, "orders", "edit", "Transportopdrachten bewerken"),
        (OrdersDelete, "orders", "delete", "Transportopdrachten verwijderen"),
        (OrdersChangeStatus, "orders", "change_status", "Status van transportopdrachten wijzigen"),
        (InvoicesView, "invoices", "view", "Facturen bekijken"),
        (InvoicesCreate, "invoices", "create", "Facturen aanmaken"),
        (InvoicesEdit, "invoices", "edit", "Facturen bewerken"),
        (InvoicesDelete, "invoices", "delete", "Facturen verwijderen"),
        (InvoicesChangeStatus, "invoices", "change_status", "Factuurstatus wijzigen"),
        (DriverWorkflowView, "driver_workflow", "view", "Eigen ritten en rituitvoering bekijken"),
        (DriverWorkflowExecute, "driver_workflow", "execute", "Stops registreren tijdens rituitvoering"),
        (AbsencesView, "absences", "view", "Afwezigheden bekijken"),
        (AbsencesCreate, "absences", "create", "Afwezigheden aanvragen"),
        (AbsencesEdit, "absences", "edit", "Afwezigheden bewerken en annuleren"),
        (AbsencesDelete, "absences", "delete", "Afwezigheden verwijderen"),
        (AbsencesApprove, "absences", "approve", "Afwezigheden goedkeuren of afwijzen"),
    ];
}
