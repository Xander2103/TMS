namespace TransportationService.Api.Modules.Identity;

public static class PermissionCodes
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersDelete = "users.delete";
    public const string UsersBlock = "users.block";

    /// <summary>Sensitive: administratively (re)set another user's password. Separate from users.edit
    /// so that ordinary user-editing rights can never be leveraged into account takeover.</summary>
    public const string UsersResetPassword = "users.reset_password";

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
    public const string EmployeeDocumentsViewSensitive = "employee_documents.view_sensitive";

    public const string EmployeeNotesView = "employee_notes.view";
    public const string EmployeeNotesManage = "employee_notes.manage";
    public const string EmployeeNotesPin = "employee_notes.pin";

    // --- HR settings (reminders, expiry policies) ---
    public const string HrSettingsManage = "hr_settings.manage";

    // --- Issued items (bedrijfsmiddelen) ---
    public const string IssuedItemsView = "issued_items.view";
    public const string IssuedItemsManage = "issued_items.manage";
    public const string IssuedItemsManageTemplates = "issued_items.manage_templates";

    // --- Inventory / stock for issued items ---
    public const string InventoryView = "inventory.view";
    public const string InventoryManage = "inventory.manage";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryOverrideNegativeStock = "inventory.override_negative_stock";
    public const string InventoryLowStockAlerts = "inventory.low_stock_alerts";

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

    // --- Own companies / invoicing entities ---
    public const string LegalEntitiesView = "legal_entities.view";
    public const string LegalEntitiesManage = "legal_entities.manage";

    // --- Classification categories ---
    public const string VehicleCategoriesView = "vehicle_categories.view";
    public const string VehicleCategoriesManage = "vehicle_categories.manage";
    public const string TrailerCategoriesView = "trailer_categories.view";
    public const string TrailerCategoriesManage = "trailer_categories.manage";
    public const string DriverCategoriesView = "driver_categories.view";
    public const string DriverCategoriesManage = "driver_categories.manage";
    public const string CustomerCategoriesView = "customer_categories.view";
    public const string CustomerCategoriesManage = "customer_categories.manage";

    // --- Customer-contact departments (lookup) ---
    public const string ContactDepartmentsView = "contact_departments.view";
    public const string ContactDepartmentsManage = "contact_departments.manage";

    // --- Reference data (countries, languages, nationalities, contract types) ---
    public const string ReferenceDataView = "reference_data.view";
    public const string ReferenceDataManage = "reference_data.manage";

    // --- Order unit types (managed lookup) ---
    public const string UnitTypesView = "unit_types.view";
    public const string UnitTypesManage = "unit_types.manage";

    // --- Customers ---
    public const string CustomersView = "customers.view";
    public const string CustomersCreate = "customers.create";
    public const string CustomersEdit = "customers.edit";
    public const string CustomersDelete = "customers.delete";
    public const string CustomersDeactivate = "customers.deactivate";
    public const string CustomersImport = "customers.import";
    public const string CustomersOverrideNumber = "customers.override_number";
    public const string CustomersManageFiscal = "customers.manage_fiscal";
    public const string CustomersManageCommunication = "customers.manage_communication";
    public const string CustomersManageSurcharge = "customers.manage_surcharge";
    public const string CustomersManagePo = "customers.manage_po";

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

    // --- Tachograph calibration ---
    public const string TachographView = "tachograph.view";
    public const string TachographManage = "tachograph.manage";

    // --- Sensitive fleet finance (leasing amounts, ...) ---
    public const string FleetFinanceView = "fleet_finance.view";
    public const string FleetFinanceManage = "fleet_finance.manage";

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
    public const string MaintenancePoliciesView = "maintenance_policies.view";
    public const string MaintenancePoliciesManage = "maintenance_policies.manage";

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
    public const string OrdersCorrectStatus = "orders.correct_status";
    public const string OrdersCancel = "orders.cancel";
    public const string OrdersAssign = "orders.assign";
    public const string OrdersExport = "orders.export";
    /// <summary>Umbrella: every order action (checked as an any-of alternative on order endpoints).</summary>
    public const string OrdersManage = "orders.manage";
    public const string OrdersOverridePrice = "orders.override_price";
    /// <summary>Locks/unlocks the pricing status of an order (spec ch. 24-26), blocking further recalculation.</summary>
    public const string OrdersLockPrice = "orders.lock_price";

    // --- Dashboard (company overview) ---
    public const string DashboardView = "dashboard.view";

    public const string MessagesSend = "messages.send";

    // --- Transport dossiers ---
    public const string DossiersView = "dossiers.view";
    public const string DossiersManage = "dossiers.manage";

    // --- Incidents ---
    public const string IncidentsView = "incidents.view";
    public const string IncidentsManage = "incidents.manage";

    // --- Tariffs (customer rate cards) ---
    public const string TariffsView = "tariffs.view";
    public const string TariffsManage = "tariffs.manage";
    public const string TariffsImport = "tariffs.import";

    // --- Reporting centre (catalog access; each report keeps its own permission) ---
    public const string ReportsView = "reports.view";

    // --- Customer portal (all data scoped to the authenticated user's linked customer) ---
    public const string CustomerPortalView = "customer_portal.view";
    public const string CustomerPortalSubmitOrders = "customer_portal.submit_orders";
    public const string CustomerPortalManageLocations = "customer_portal.manage_locations";
    public const string CustomerPortalViewDocuments = "customer_portal.view_documents";
    public const string CustomerPortalViewInvoices = "customer_portal.view_invoices";
    public const string CustomerPortalMessages = "customer_portal.messages";
    public const string CustomerPortalManageUsers = "customer_portal.manage_users";

    // --- Customer messages (internal side of the portal's Berichten module) ---
    public const string CustomerMessagesView = "customer_messages.view";
    public const string CustomerMessagesSend = "customer_messages.send";

    // --- Portal announcements (broadcast notices shown in the customer portal) ---
    public const string PortalAnnouncementsManage = "portal_announcements.manage";

    // --- Invoices ---
    public const string InvoicesView = "invoices.view";
    public const string InvoicesCreate = "invoices.create";
    public const string InvoicesEdit = "invoices.edit";
    public const string InvoicesDelete = "invoices.delete";
    public const string InvoicesChangeStatus = "invoices.change_status";
    public const string InvoicesOverrideNumber = "invoices.override_number";
    public const string InvoiceAttachmentsView = "invoice_attachments.view";
    public const string InvoiceAttachmentsManage = "invoice_attachments.manage";

    // --- Accounting (ledger accounts + sales-category mappings) ---
    public const string AccountingView = "accounting.view";
    public const string AccountingManage = "accounting.manage";

    // --- Driver workflow (trip execution) ---
    public const string DriverWorkflowView = "driver_workflow.view";
    public const string DriverWorkflowExecute = "driver_workflow.execute";

    // --- Scanning (cargo verification during execution) ---
    public const string ScanningView = "scanning.view";
    public const string ScanningExecute = "scanning.execute";
    public const string ScanningCorrect = "scanning.correct";

    // --- EDI & integrations ---
    public const string EdiView = "edi.view";
    public const string EdiManage = "edi.manage";
    public const string EdiTest = "edi.test";
    public const string EdiRetry = "edi.retry";
    public const string IntegrationsManage = "integrations.manage";

    // --- Messaging (email/SMS outbox + templates) ---
    public const string MessagingManage = "messaging.manage";
    public const string MessageTemplatesManage = "message_templates.manage";

    // --- Employee planning (personnel shifts) ---
    public const string EmployeePlanningView = "employee_planning.view";
    public const string EmployeePlanningManage = "employee_planning.manage";
    public const string EmployeePlanningConflictOverride = "employee_planning.conflict_override";

    // --- Trip costing & profitability (sensitive financial data) ---
    public const string TripCostsView = "trip_costs.view";
    public const string TripCostsManage = "trip_costs.manage";
    public const string TripCostsOverride = "trip_costs.override";
    public const string ProfitabilityView = "profitability.view";

    // --- Management KPI dashboard & reports ---
    public const string KpiView = "kpi.view";
    public const string KpiExport = "kpi.export";

    // --- Packages (colli) & chain of custody ---
    public const string PackagesView = "packages.view";
    public const string PackagesCreate = "packages.create";
    public const string PackagesManage = "packages.manage";
    public const string PackagesCancel = "packages.cancel";
    public const string PackagesRelabel = "packages.relabel";
    public const string PackagesExport = "packages.export";
    public const string ScanningOverride = "scanning.override";
    public const string PackageExceptionsCreate = "package_exceptions.create";
    public const string PackageExceptionsManage = "package_exceptions.manage";
    public const string WarehouseView = "warehouse.view";
    public const string WarehouseReleaseTrip = "warehouse.release_trip";
    public const string PackageReportsExport = "package_reports.export";

    // --- Proof of delivery ---
    public const string PodView = "pod.view";
    public const string PodFinalize = "pod.finalize";
    public const string PodCorrect = "pod.correct";

    // --- Execution exceptions (afwijkingen) ---
    public const string ExceptionsView = "exceptions.view";
    public const string ExceptionsCreate = "exceptions.create";
    public const string ExceptionsResolve = "exceptions.resolve";

    // --- Operation control center ---
    public const string OperationsView = "operations.view";
    public const string OperationsManageAlerts = "operations.manage_alerts";

    // --- Warehouse & dock planning ---
    public const string WarehouseManage = "warehouse.manage";
    public const string WarehouseSchedule = "warehouse.schedule";
    public const string WarehouseConflictOverride = "warehouse.conflict_override";

    // --- Profitability exports ---
    public const string ProfitabilityExport = "profitability.export";

    // --- Absences (HR availability) ---
    public const string AbsencesView = "absences.view";
    public const string AbsencesCreate = "absences.create";
    public const string AbsencesEdit = "absences.edit";
    public const string AbsencesDelete = "absences.delete";
    public const string AbsencesApprove = "absences.approve";

    /// <summary>
    /// GDPR art. 9 special category. Sick-leave reasons, HR-internal notes on sick leave and
    /// medical certificates are health data; absences.view alone only shows that someone is
    /// absent and between which dates, which is what planning actually needs.
    /// </summary>
    public const string AbsencesViewMedical = "absences.view_medical";

    // --- Leave balances (verlofsaldo) ---
    public const string LeaveBalancesView = "leave_balances.view";
    public const string LeaveBalancesManage = "leave_balances.manage";
    public const string LeaveBalancesAdjust = "leave_balances.adjust";
    public const string LeaveBalancesViewOwn = "leave_balances.view_own";
    public const string LeaveTypesManage = "leave_types.manage";

    // --- Notification rules (configurable events, recipients, customer overrides) ---
    public const string NotificationRulesView = "notification_rules.view";
    public const string NotificationRulesManage = "notification_rules.manage";

    // --- Peppol e-invoicing (provider-neutral configuration, transmissions, incoming documents) ---
    public const string PeppolView = "peppol.view";
    public const string PeppolConfigure = "peppol.configure";
    public const string PeppolValidate = "peppol.validate";
    public const string PeppolSend = "peppol.send";
    public const string PeppolRetry = "peppol.retry";
    public const string PeppolViewIncoming = "peppol.view_incoming";

    public static readonly IReadOnlyList<(string Code, string Module, string Action, string Description)> All =
    [
        (UsersView, "users", "view", "Gebruikers bekijken"),
        (UsersCreate, "users", "create", "Gebruikers aanmaken"),
        (UsersEdit, "users", "edit", "Gebruikers bewerken"),
        (UsersResetPassword, "users", "reset_password", "Wachtwoord van een gebruiker administratief resetten"),
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
        (EmployeeDocumentsViewSensitive, "employee_documents", "view_sensitive", "Gevoelige personeelsdocumenten (ID, medisch, contract) bekijken"),
        (EmployeeNotesView, "employee_notes", "view", "Notities van medewerkers bekijken"),
        (EmployeeNotesManage, "employee_notes", "manage", "Notities van medewerkers toevoegen, bewerken en verwijderen"),
        (EmployeeNotesPin, "employee_notes", "pin", "Notities aan het startscherm toevoegen of verwijderen"),
        (HrSettingsManage, "hr_settings", "manage", "HR-instellingen en herinneringen beheren"),
        (IssuedItemsView, "issued_items", "view", "Bedrijfsmiddelen van medewerkers bekijken"),
        (IssuedItemsManage, "issued_items", "manage", "Bedrijfsmiddelen uitreiken en innemen"),
        (IssuedItemsManageTemplates, "issued_items", "manage_templates", "Sjablonen voor bedrijfsmiddelen beheren"),
        (InventoryView, "inventory", "view", "Voorraad van bedrijfsmiddelen bekijken"),
        (InventoryManage, "inventory", "manage", "Voorraadkenmerken, varianten en attributen beheren"),
        (InventoryAdjust, "inventory", "adjust", "Voorraad toevoegen en corrigeren"),
        (InventoryOverrideNegativeStock, "inventory", "override_negative_stock", "Uitgifte bij onvoldoende voorraad toestaan"),
        (InventoryLowStockAlerts, "inventory", "low_stock_alerts", "Ontvangt meldingen bij lage voorraad"),
        (PlanningView, "planning", "view", "Planning bekijken"),
        (PlanningCreate, "planning", "create", "Planning aanmaken"),
        (PlanningEdit, "planning", "edit", "Planning bewerken"),
        (PlanningOverrideRestriction, "planning", "override_restriction", "Planningsbeperkingen overschrijven"),
        (AuditLogsView, "audit_logs", "view", "Auditlogboek bekijken"),
        (DepartmentsView, "departments", "view", "Afdelingen bekijken"),
        (DepartmentsManage, "departments", "manage", "Afdelingen beheren"),
        (JobFunctionsView, "job_functions", "view", "Functies bekijken"),
        (JobFunctionsManage, "job_functions", "manage", "Functies beheren"),
        (LegalEntitiesView, "legal_entities", "view", "Eigen bedrijven (facturerende entiteiten) bekijken"),
        (LegalEntitiesManage, "legal_entities", "manage", "Eigen bedrijven (facturerende entiteiten) beheren"),
        (VehicleCategoriesView, "vehicle_categories", "view", "Voertuigcategorieën bekijken"),
        (VehicleCategoriesManage, "vehicle_categories", "manage", "Voertuigcategorieën beheren"),
        (TrailerCategoriesView, "trailer_categories", "view", "Opleggercategorieën bekijken"),
        (TrailerCategoriesManage, "trailer_categories", "manage", "Opleggercategorieën beheren"),
        (DriverCategoriesView, "driver_categories", "view", "Chauffeurcategorieën bekijken"),
        (DriverCategoriesManage, "driver_categories", "manage", "Chauffeurcategorieën beheren"),
        (CustomerCategoriesView, "customer_categories", "view", "Klantcategorieën bekijken"),
        (CustomerCategoriesManage, "customer_categories", "manage", "Klantcategorieën beheren"),
        (ContactDepartmentsView, "contact_departments", "view", "Contactafdelingen bekijken"),
        (ContactDepartmentsManage, "contact_departments", "manage", "Contactafdelingen beheren"),
        (ReferenceDataView, "reference_data", "view", "Referentiegegevens bekijken"),
        (ReferenceDataManage, "reference_data", "manage", "Referentiegegevens beheren"),
        (UnitTypesView, "unit_types", "view", "Eenheidstypes bekijken"),
        (UnitTypesManage, "unit_types", "manage", "Eenheidstypes beheren"),
        (CustomersView, "customers", "view", "Klanten bekijken"),
        (CustomersCreate, "customers", "create", "Klanten aanmaken"),
        (CustomersEdit, "customers", "edit", "Klanten bewerken"),
        (CustomersDelete, "customers", "delete", "Klanten verwijderen"),
        (CustomersDeactivate, "customers", "deactivate", "Klanten activeren/deactiveren"),
        (CustomersImport, "customers", "import", "Klanten importeren uit Excel"),
        (CustomersOverrideNumber, "customers", "override_number", "Klantnummers handmatig wijzigen"),
        (CustomersManageFiscal, "customers", "manage_fiscal", "Fiscale, Peppol- en bankgegevens van klanten beheren"),
        (CustomersManageCommunication, "customers", "manage_communication", "Communicatie-ontvangers van klanten beheren"),
        (CustomersManageSurcharge, "customers", "manage_surcharge", "Dieseltoeslag van klanten beheren"),
        (CustomersManagePo, "customers", "manage_po", "PO-beleid en PO-nummers van klanten beheren"),
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
        (TachographView, "tachograph", "view", "Tachograaf-ijkingen bekijken"),
        (TachographManage, "tachograph", "manage", "Tachograaf-ijkingen beheren"),
        (FleetFinanceView, "fleet_finance", "view", "Financiële leasinggegevens bekijken"),
        (FleetFinanceManage, "fleet_finance", "manage", "Leasingcontracten beheren"),
        (FleetDocumentsView, "fleet_documents", "view", "Voertuig- en opleggerdocumenten bekijken"),
        (FleetDocumentsCreate, "fleet_documents", "create", "Voertuig- en opleggerdocumenten toevoegen"),
        (FleetDocumentsEdit, "fleet_documents", "edit", "Voertuig- en opleggerdocumenten bewerken"),
        (FleetDocumentsDelete, "fleet_documents", "delete", "Voertuig- en opleggerdocumenten verwijderen"),
        (MaintenanceView, "maintenance", "view", "Onderhoud bekijken"),
        (MaintenanceCreate, "maintenance", "create", "Onderhoud plannen"),
        (MaintenanceEdit, "maintenance", "edit", "Onderhoud bewerken en afronden"),
        (MaintenanceDelete, "maintenance", "delete", "Onderhoud verwijderen"),
        (MaintenancePoliciesView, "maintenance_policies", "view", "Onderhoudsbeleid bekijken"),
        (MaintenancePoliciesManage, "maintenance_policies", "manage", "Onderhoudsbeleid beheren"),
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
        (OrdersCorrectStatus, "orders", "correct_status", "Status van transportopdrachten corrigeren (terugdraaien met reden)"),
        (OrdersCancel, "orders", "cancel", "Transportopdrachten annuleren"),
        (OrdersAssign, "orders", "assign", "Transportopdrachten aan ritten koppelen"),
        (OrdersExport, "orders", "export", "Transportopdrachten exporteren"),
        (OrdersOverridePrice, "orders", "override_price", "Berekende orderprijs handmatig overschrijven"),
        (OrdersLockPrice, "orders", "lock_price", "Prijs van transportopdrachten vergrendelen en ontgrendelen"),
        (OrdersManage, "orders", "manage", "Volledig beheer van transportopdrachten"),
        (DashboardView, "dashboard", "view", "Bedrijfsdashboard bekijken"),
        (MessagesSend, "messages", "send", "Interne berichten versturen"),
        (DossiersView, "dossiers", "view", "Transportdossiers bekijken"),
        (DossiersManage, "dossiers", "manage", "Transportdossiers beheren (aanmaken, koppelen, sluiten)"),
        (IncidentsView, "incidents", "view", "Incidenten bekijken"),
        (IncidentsManage, "incidents", "manage", "Incidenten registreren en afhandelen"),
        (TariffsView, "tariffs", "view", "Tarievenkaarten bekijken en prijzen berekenen"),
        (TariffsManage, "tariffs", "manage", "Tarievenkaarten beheren"),
        (TariffsImport, "tariffs", "import", "Tarieventabellen exporteren/importeren via Excel"),
        (ReportsView, "reports", "view", "Rapportcentrum openen"),
        (CustomerPortalView, "customer_portal", "view", "Klantportaal: eigen opdrachten bekijken"),
        (CustomerPortalSubmitOrders, "customer_portal", "submit_orders", "Klantportaal: opdrachten indienen"),
        (CustomerPortalManageLocations, "customer_portal", "manage_locations", "Klantportaal: eigen locaties beheren"),
        (CustomerPortalViewDocuments, "customer_portal", "view_documents", "Klantportaal: documenten bekijken"),
        (CustomerPortalViewInvoices, "customer_portal", "view_invoices", "Klantportaal: facturen bekijken"),
        (CustomerPortalMessages, "customer_portal", "messages", "Klantportaal: berichten bekijken en versturen"),
        (CustomerPortalManageUsers, "customer_portal", "manage_users", "Klantportaal: klantgebruikers beheren (uitnodigen, blokkeren, rechten)"),
        (CustomerMessagesView, "customer_messages", "view", "Berichten van klanten via het klantportaal bekijken"),
        (CustomerMessagesSend, "customer_messages", "send", "Berichten naar klanten via het klantportaal versturen"),
        (PortalAnnouncementsManage, "portal_announcements", "manage", "Mededelingen in het klantportaal beheren"),
        (InvoicesView, "invoices", "view", "Facturen bekijken"),
        (InvoicesCreate, "invoices", "create", "Facturen aanmaken"),
        (InvoicesEdit, "invoices", "edit", "Facturen bewerken"),
        (InvoicesDelete, "invoices", "delete", "Facturen verwijderen"),
        (InvoicesChangeStatus, "invoices", "change_status", "Factuurstatus wijzigen"),
        (InvoicesOverrideNumber, "invoices", "override_number", "Factuurnummers handmatig corrigeren"),
        (InvoiceAttachmentsView, "invoice_attachments", "view", "Factuurbijlagen bekijken"),
        (InvoiceAttachmentsManage, "invoice_attachments", "manage", "Factuurbijlagen beheren"),
        (AccountingView, "accounting", "view", "Boekhouding: grootboekrekeningen en mappings bekijken"),
        (AccountingManage, "accounting", "manage", "Boekhouding: grootboekrekeningen en verkoopcategorie-mappings beheren"),
        (DriverWorkflowView, "driver_workflow", "view", "Eigen ritten en rituitvoering bekijken"),
        (DriverWorkflowExecute, "driver_workflow", "execute", "Stops registreren tijdens rituitvoering"),
        (ScanningView, "scanning", "view", "Scanhistoriek en scanstatus bekijken"),
        (ScanningExecute, "scanning", "execute", "Colli scannen tijdens rituitvoering"),
        (ScanningCorrect, "scanning", "correct", "Scantellingen handmatig corrigeren"),
        (EdiView, "edi", "view", "EDI-berichten, handelspartners en mappings bekijken"),
        (EdiManage, "edi", "manage", "EDI-partners, berichten en mappings beheren"),
        (EdiTest, "edi", "test", "EDI-berichten valideren en testberichten versturen"),
        (EdiRetry, "edi", "retry", "Mislukte EDI-berichten opnieuw verwerken"),
        (IntegrationsManage, "integrations", "manage", "Integraties en synchronisatiewachtrijen beheren"),
        (MessagingManage, "messaging", "manage", "Berichtenoutbox bekijken en opnieuw verzenden"),
        (MessageTemplatesManage, "message_templates", "manage", "Berichtsjablonen beheren"),
        (EmployeePlanningView, "employee_planning", "view", "Personeelsplanning bekijken"),
        (EmployeePlanningManage, "employee_planning", "manage", "Personeelsplanning en shifts beheren"),
        (EmployeePlanningConflictOverride, "employee_planning", "conflict_override", "Blokkerende planningsconflicten overschrijven"),
        (TripCostsView, "trip_costs", "view", "Ritkosten en tarieven bekijken"),
        (TripCostsManage, "trip_costs", "manage", "Ritkosten berekenen, invoeren en afronden; tarieven beheren"),
        (TripCostsOverride, "trip_costs", "override", "Kostenregels overschrijven en afgeronde kosten heropenen"),
        (ProfitabilityView, "profitability", "view", "Rendement en marges van ritten bekijken"),
        (KpiView, "kpi", "view", "Management-KPI-dashboard bekijken"),
        (KpiExport, "kpi", "export", "KPI-rapporten exporteren naar Excel"),
        (PackagesView, "packages", "view", "Colli en pakketstatussen bekijken"),
        (PackagesCreate, "packages", "create", "Colli aanmaken (handmatig, bulk of import)"),
        (PackagesManage, "packages", "manage", "Colli beheren (gegevens, groepen, disposities)"),
        (PackagesCancel, "packages", "cancel", "Colli annuleren"),
        (PackagesRelabel, "packages", "relabel", "Colli heretiketteren en etiketten herafdrukken"),
        (PackagesExport, "packages", "export", "Collilijsten exporteren"),
        (ScanningOverride, "scanning", "override", "Scanblokkades en stopafronding overrulen"),
        (PackageExceptionsCreate, "package_exceptions", "create", "Pakketafwijkingen melden (ontbrekend/verkeerd/beschadigd)"),
        (PackageExceptionsManage, "package_exceptions", "manage", "Pakketafwijkingen toewijzen en afhandelen"),
        (WarehouseView, "warehouse", "view", "Magazijnmodule: laadlijsten en laadvoortgang bekijken"),
        (WarehouseReleaseTrip, "warehouse", "release_trip", "Ritten vrijgeven voor vertrek (incl. override met reden)"),
        (PackageReportsExport, "package_reports", "export", "Pakketrapporten exporteren naar Excel"),
        (PodView, "pod", "view", "Afleverbewijzen bekijken"),
        (PodFinalize, "pod", "finalize", "Afleverbewijzen opnemen en afronden"),
        (PodCorrect, "pod", "correct", "Afleverbewijzen corrigeren (nieuwe versie)"),
        (ExceptionsView, "exceptions", "view", "Uitvoeringsafwijkingen bekijken"),
        (ExceptionsCreate, "exceptions", "create", "Afwijkingen melden tijdens uitvoering"),
        (ExceptionsResolve, "exceptions", "resolve", "Afwijkingen onderzoeken en afhandelen"),
        (OperationsView, "operations", "view", "Operationeel controlecentrum bekijken"),
        (OperationsManageAlerts, "operations", "manage_alerts", "Operationele meldingen bevestigen, toewijzen en afhandelen"),
        (WarehouseManage, "warehouse", "manage", "Magazijnen en docks beheren"),
        (WarehouseSchedule, "warehouse", "schedule", "Dockafspraken plannen en verplaatsen"),
        (WarehouseConflictOverride, "warehouse", "conflict_override", "Blokkerende dockconflicten overschrijven met reden"),
        (ProfitabilityExport, "profitability", "export", "Rendementsrapporten exporteren naar Excel"),
        (AbsencesView, "absences", "view", "Afwezigheden bekijken"),
        (AbsencesCreate, "absences", "create", "Afwezigheden aanvragen"),
        (AbsencesEdit, "absences", "edit", "Afwezigheden bewerken en annuleren"),
        (AbsencesDelete, "absences", "delete", "Afwezigheden verwijderen"),
        (AbsencesApprove, "absences", "approve", "Afwezigheden goedkeuren of afwijzen"),
        (AbsencesViewMedical, "absences", "view_medical", "Medische gegevens bij ziekteverzuim bekijken (reden, HR-notitie, attest)"),
        (LeaveBalancesView, "leave_balances", "view", "Verlofsaldo van medewerkers bekijken"),
        (LeaveBalancesManage, "leave_balances", "manage", "Jaarrecht en overdracht van verlofsaldo beheren"),
        (LeaveBalancesAdjust, "leave_balances", "adjust", "Verlofsaldo handmatig aanpassen (met reden)"),
        (LeaveBalancesViewOwn, "leave_balances", "view_own", "Eigen verlofsaldo bekijken"),
        (LeaveTypesManage, "leave_types", "manage", "Verloftypes en saldotypes beheren"),
        (NotificationRulesView, "notification_rules", "view", "Meldingsregels en klantafwijkingen bekijken"),
        (NotificationRulesManage, "notification_rules", "manage", "Meldingsregels, ontvangers en klantafwijkingen beheren"),
        (PeppolView, "peppol", "view", "Peppol-configuratie en overzicht bekijken"),
        (PeppolConfigure, "peppol", "configure", "Peppol-instellingen per eigen bedrijf beheren"),
        (PeppolValidate, "peppol", "validate", "Peppol-gegevens van klanten en eigen bedrijven valideren"),
        (PeppolSend, "peppol", "send", "Facturen via Peppol verzenden"),
        (PeppolRetry, "peppol", "retry", "Mislukte Peppol-verzendingen opnieuw proberen"),
        (PeppolViewIncoming, "peppol", "view_incoming", "Inkomende Peppol-documenten bekijken en beoordelen"),
    ];
}
