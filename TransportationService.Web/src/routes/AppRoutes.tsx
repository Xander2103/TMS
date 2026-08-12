import { lazy, Suspense, type ComponentType } from 'react'
import {
  createBrowserRouter,
  createRoutesFromElements,
  Navigate,
  Outlet,
  Route,
  RouterProvider,
} from 'react-router-dom'
import { AuthProvider } from '../features/auth/AuthContext'
import { LoginPage } from '../features/auth/LoginPage'
import { RequireAuth } from '../features/auth/RequireAuth'
import { AppLayout } from '../components/layout/AppLayout'
import { InternalOnly, RootRedirect } from './portalRouting'
import { LoadingState } from '../components/feedback/LoadingState'
import { NotFoundPage } from '../components/feedback/NotFoundPage'
import { LOOKUP_RESOURCES } from '../features/master-data/lookupRegistry'

/**
 * Route-based code splitting: every page loads as its own chunk on first visit, so the
 * initial bundle only carries the shell (auth, layout, shared kit). Pages use named
 * exports, hence the pick-helper around React.lazy.
 */
function lazyPage<TModule extends Record<string, unknown>>(
  loader: () => Promise<TModule>,
  name: keyof TModule,
) {
  return lazy(async () => ({ default: (await loader())[name] as ComponentType }))
}

const DashboardPage = lazyPage(() => import('../features/dashboard/pages/DashboardPage'), 'DashboardPage')
const TransportOrdersPage = lazyPage(() => import('../features/transport-orders/pages/TransportOrdersPage'), 'TransportOrdersPage')
const NewTransportOrderPage = lazyPage(() => import('../features/transport-orders/pages/NewTransportOrderPage'), 'NewTransportOrderPage')
const TransportOrderDetailPage = lazyPage(() => import('../features/transport-orders/pages/TransportOrderDetailPage'), 'TransportOrderDetailPage')
const PlanningPage = lazyPage(() => import('../features/planning/pages/PlanningPage'), 'PlanningPage')
const PlanningCenterPage = lazyPage(() => import('../features/planning-center/pages/PlanningCenterPage'), 'PlanningCenterPage')
const OperationsPage = lazyPage(() => import('../features/operations/pages/OperationsPage'), 'OperationsPage')
const ProfitabilityPage = lazyPage(() => import('../features/profitability/pages/ProfitabilityPage'), 'ProfitabilityPage')
const WarehousesPage = lazyPage(() => import('../features/warehousing/pages/WarehousesPage'), 'WarehousesPage')
const WarehouseTracePage = lazyPage(() => import('../features/warehousing/pages/WarehouseTracePage'), 'WarehouseTracePage')
const DockPlanningPage = lazyPage(() => import('../features/warehousing/pages/DockPlanningPage'), 'DockPlanningPage')
const DriverLayout = lazyPage(() => import('../features/driver/components/DriverLayout'), 'DriverLayout')
const DriverHomePage = lazyPage(() => import('../features/driver/pages/DriverHomePage'), 'DriverHomePage')
const DriverDocumentsPage = lazyPage(() => import('../features/driver/pages/DriverDocumentsPage'), 'DriverDocumentsPage')
const DriverIncidentsPage = lazyPage(() => import('../features/driver/pages/DriverIncidentsPage'), 'DriverIncidentsPage')
const TripDetailPage = lazyPage(() => import('../features/planning/pages/TripDetailPage'), 'TripDetailPage')
const MyTripsPage = lazyPage(() => import('../features/my-trips/pages/MyTripsPage'), 'MyTripsPage')
const TripExecutionPage = lazyPage(() => import('../features/my-trips/pages/TripExecutionPage'), 'TripExecutionPage')
const ExceptionsPage = lazyPage(() => import('../features/exceptions/pages/ExceptionsPage'), 'ExceptionsPage')
const ExceptionDetailPage = lazyPage(() => import('../features/exceptions/pages/ExceptionDetailPage'), 'ExceptionDetailPage')
const PodDetailPage = lazyPage(() => import('../features/pod/pages/PodDetailPage'), 'PodDetailPage')
const PackageDetailPage = lazyPage(() => import('../features/packages/pages/PackageDetailPage'), 'PackageDetailPage')
const WarehousePage = lazyPage(() => import('../features/packages/pages/WarehousePage'), 'WarehousePage')
const PortalDashboardPage = lazyPage(() => import('../features/portal/pages/PortalDashboardPage'), 'PortalDashboardPage')
const PortalPlanningPage = lazyPage(() => import('../features/portal/pages/PortalPlanningPage'), 'PortalPlanningPage')
const EmployeePlanningPage = lazyPage(() => import('../features/employee-planning/pages/EmployeePlanningPage'), 'EmployeePlanningPage')
const MessagingPage = lazyPage(() => import('../features/messaging/pages/MessagingPage'), 'MessagingPage')
const EdiPage = lazyPage(() => import('../features/edi/pages/EdiPage'), 'EdiPage')
const PeppolPage = lazyPage(() => import('../features/peppol/pages/PeppolPage'), 'PeppolPage')
const IntegrationsPage = lazyPage(() => import('../features/integrations/pages/IntegrationsPage'), 'IntegrationsPage')
const PortalAbsencesPage = lazyPage(() => import('../features/portal/pages/PortalAbsencesPage'), 'PortalAbsencesPage')
const PortalQualificationsPage = lazyPage(() => import('../features/portal/pages/PortalQualificationsPage'), 'PortalQualificationsPage')
const PortalProfilePage = lazyPage(() => import('../features/portal/pages/PortalProfilePage'), 'PortalProfilePage')
const InvoicesPage = lazyPage(() => import('../features/invoices/pages/InvoicesPage'), 'InvoicesPage')
const NewInvoicePage = lazyPage(() => import('../features/invoices/pages/NewInvoicePage'), 'NewInvoicePage')
const InvoiceControlPage = lazyPage(() => import('../features/invoices/pages/InvoiceControlPage'), 'InvoiceControlPage')
const InvoiceDetailPage = lazyPage(() => import('../features/invoices/pages/InvoiceDetailPage'), 'InvoiceDetailPage')
const NotificationsPage = lazyPage(() => import('../features/notifications/pages/NotificationsPage'), 'NotificationsPage')
const CustomersPage = lazyPage(() => import('../features/customers/pages/CustomersPage'), 'CustomersPage')
const NewCustomerPage = lazyPage(() => import('../features/customers/pages/NewCustomerPage'), 'NewCustomerPage')
const CustomerDetailPage = lazyPage(() => import('../features/customers/pages/CustomerDetailPage'), 'CustomerDetailPage')
const NewDriverPage = lazyPage(() => import('../features/drivers/pages/NewDriverPage'), 'NewDriverPage')
const DriverDetailPage = lazyPage(() => import('../features/drivers/pages/DriverDetailPage'), 'DriverDetailPage')
const VehiclesPage = lazyPage(() => import('../features/vehicles/pages/VehiclesPage'), 'VehiclesPage')
const NewVehiclePage = lazyPage(() => import('../features/vehicles/pages/NewVehiclePage'), 'NewVehiclePage')
const VehicleDetailPage = lazyPage(() => import('../features/vehicles/pages/VehicleDetailPage'), 'VehicleDetailPage')
const TrailersPage = lazyPage(() => import('../features/trailers/pages/TrailersPage'), 'TrailersPage')
const NewTrailerPage = lazyPage(() => import('../features/trailers/pages/NewTrailerPage'), 'NewTrailerPage')
const TrailerDetailPage = lazyPage(() => import('../features/trailers/pages/TrailerDetailPage'), 'TrailerDetailPage')
const AbsencesPage = lazyPage(() => import('../features/absences/pages/AbsencesPage'), 'AbsencesPage')
const FleetDashboardPage = lazyPage(() => import('../features/fleet-dashboard/pages/FleetDashboardPage'), 'FleetDashboardPage')
const TankCardsPage = lazyPage(() => import('../features/tank-cards/pages/TankCardsPage'), 'TankCardsPage')
const MaintenancePoliciesPage = lazyPage(() => import('../features/maintenance-policies/pages/MaintenancePoliciesPage'), 'MaintenancePoliciesPage')
const CustomerPortalOrdersPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalOrdersPage'), 'CustomerPortalOrdersPage')
const CustomerPortalNewOrderPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalNewOrderPage'), 'CustomerPortalNewOrderPage')
const CustomerPortalOrderDetailPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalOrderDetailPage'), 'CustomerPortalOrderDetailPage')
const CustomerPortalLayout = lazyPage(() => import('../features/customer-portal/components/CustomerPortalLayout'), 'CustomerPortalLayout')
const CustomerPortalDashboardPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalDashboardPage'), 'CustomerPortalDashboardPage')
const CustomerPortalDocumentsPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalDocumentsPage'), 'CustomerPortalDocumentsPage')
const CustomerPortalInvoicesPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalInvoicesPage'), 'CustomerPortalInvoicesPage')
const CustomerPortalInvoiceDetailPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalInvoiceDetailPage'), 'CustomerPortalInvoiceDetailPage')
const CustomerPortalMessagesPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalMessagesPage'), 'CustomerPortalMessagesPage')
const CustomerPortalNoticesPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalNoticesPage'), 'CustomerPortalNoticesPage')
const PortalMessagesPage = lazyPage(() => import('../features/portal-messages/pages/PortalMessagesPage'), 'PortalMessagesPage')
const CustomerPortalUsersPage = lazyPage(() => import('../features/customer-portal/pages/CustomerPortalUsersPage'), 'CustomerPortalUsersPage')
const PortalAnnouncementsSettingsPage = lazyPage(() => import('../features/customer-portal/pages/PortalAnnouncementsSettingsPage'), 'PortalAnnouncementsSettingsPage')
const ForgotPasswordPage = lazyPage(() => import('../features/auth/PasswordFlowPages'), 'ForgotPasswordPage')
const ResetPasswordPage = lazyPage(() => import('../features/auth/PasswordFlowPages'), 'ResetPasswordPage')
const ActivatePage = lazyPage(() => import('../features/auth/PasswordFlowPages'), 'ActivatePage')
const ChangePasswordPage = lazyPage(() => import('../features/auth/PasswordFlowPages'), 'ChangePasswordPage')
const JobFunctionMappingsPage = lazyPage(() => import('../features/roles/pages/JobFunctionMappingsPage'), 'JobFunctionMappingsPage')
const InboxPage = lazyPage(() => import('../features/inbox/pages/InboxPage'), 'InboxPage')
const CostRatesPage = lazyPage(() => import('../features/trip-costing/pages/CostRatesPage'), 'CostRatesPage')
const PricingSettingsPage = lazyPage(() => import('../features/tarification/pages/PricingSettingsPage'), 'PricingSettingsPage')
const PricingTablesPage = lazyPage(() => import('../features/tarification/pages/PricingTablesPage'), 'PricingTablesPage')
const PricingTableDetailPage = lazyPage(() => import('../features/tarification/pages/PricingTableDetailPage'), 'PricingTableDetailPage')
const KpiDashboardPage = lazyPage(() => import('../features/kpi/pages/KpiDashboardPage'), 'KpiDashboardPage')
const KpiTripsPage = lazyPage(() => import('../features/kpi/pages/KpiTripsPage'), 'KpiTripsPage')
const LocationsPage = lazyPage(() => import('../features/locations/pages/LocationsPage'), 'LocationsPage')
const NewLocationPage = lazyPage(() => import('../features/locations/pages/NewLocationPage'), 'NewLocationPage')
const LocationDetailPage = lazyPage(() => import('../features/locations/pages/LocationDetailPage'), 'LocationDetailPage')
const SettingsPage = lazyPage(() => import('../features/settings/pages/SettingsPage'), 'SettingsPage')
const LegalEntitiesPage = lazyPage(() => import('../features/legal-entities/pages/LegalEntitiesPage'), 'LegalEntitiesPage')
const IssuedItemTemplatesPage = lazyPage(() => import('../features/issued-items/pages/IssuedItemTemplatesPage'), 'IssuedItemTemplatesPage')
const IssuedItemTemplateDetailPage = lazyPage(() => import('../features/issued-items/pages/IssuedItemTemplateDetailPage'), 'IssuedItemTemplateDetailPage')
const InventoryOverviewPage = lazyPage(() => import('../features/issued-items/pages/InventoryOverviewPage'), 'InventoryOverviewPage')
const LeaveSettingsPage = lazyPage(() => import('../features/leave-balance/pages/LeaveSettingsPage'), 'LeaveSettingsPage')
const AccountingSettingsPage = lazyPage(() => import('../features/accounting/pages/AccountingSettingsPage'), 'AccountingSettingsPage')
const NotificationAdminPage = lazyPage(() => import('../features/notification-admin/pages/NotificationAdminPage'), 'NotificationAdminPage')
const EscalationSettingsPage = lazyPage(() => import('../features/escalations/pages/EscalationSettingsPage'), 'EscalationSettingsPage')
const UsersPage = lazyPage(() => import('../features/users/pages/UsersPage'), 'UsersPage')
const NewUserPage = lazyPage(() => import('../features/users/pages/NewUserPage'), 'NewUserPage')
const UserDetailPage = lazyPage(() => import('../features/users/pages/UserDetailPage'), 'UserDetailPage')
const RolesPage = lazyPage(() => import('../features/roles/pages/RolesPage'), 'RolesPage')
const RoleDetailPage = lazyPage(() => import('../features/roles/pages/RoleDetailPage'), 'RoleDetailPage')
const EmployeesPage = lazyPage(() => import('../features/employees/pages/EmployeesPage'), 'EmployeesPage')
const QualificationsOverviewPage = lazyPage(() => import('../features/qualifications/pages/QualificationsOverviewPage'), 'QualificationsOverviewPage')
const NewEmployeePage = lazyPage(() => import('../features/employees/pages/NewEmployeePage'), 'NewEmployeePage')
const EmployeeDetailPage = lazyPage(() => import('../features/employees/pages/EmployeeDetailPage'), 'EmployeeDetailPage')
const HrReminderSettingsPage = lazyPage(() => import('../features/employees/pages/HrReminderSettingsPage'), 'HrReminderSettingsPage')
const LookupPage = lazyPage(() => import('../features/master-data/pages/LookupPage'), 'LookupPage')
const UnitTypesPage = lazyPage(() => import('../features/master-data/pages/UnitTypesPage'), 'UnitTypesPage')
const ServiceOptionsPage = lazyPage(() => import('../features/master-data/pages/ServiceOptionsPage'), 'ServiceOptionsPage')
const ReportsPage = lazyPage(() => import('../features/reports/pages/ReportsPage'), 'ReportsPage')
const ReportViewerPage = lazyPage(() => import('../features/reports/pages/ReportViewerPage'), 'ReportViewerPage')
const DossiersPage = lazyPage(() => import('../features/dossiers/pages/DossiersPage'), 'DossiersPage')
const NewDossierPage = lazyPage(() => import('../features/dossiers/pages/NewDossierPage'), 'NewDossierPage')
const DossierDetailPage = lazyPage(() => import('../features/dossiers/pages/DossierDetailPage'), 'DossierDetailPage')
const ActivityTypesPage = lazyPage(() => import('../features/dossiers/pages/ActivityTypesPage'), 'ActivityTypesPage')
const IncidentsPage = lazyPage(() => import('../features/incidents/pages/IncidentsPage'), 'IncidentsPage')
const IncidentDetailPage = lazyPage(() => import('../features/incidents/pages/IncidentDetailPage'), 'IncidentDetailPage')
const TasksPage = lazyPage(() => import('../features/tasks/pages/TasksPage'), 'TasksPage')
const TaskTemplatesPage = lazyPage(() => import('../features/tasks/pages/TaskTemplatesPage'), 'TaskTemplatesPage')

/** Root layout route: providers that need to live inside the router render an Outlet. */
function RootProviders() {
  return (
    <AuthProvider>
      {/* Boundary for lazily loaded routes outside the app shell (login/password flows). */}
      <Suspense fallback={<LoadingState message="Laden..." />}>
        <Outlet />
      </Suspense>
    </AuthProvider>
  )
}

// Data router (createBrowserRouter) is required for useBlocker-based unsaved-changes guards.
const router = createBrowserRouter(
  createRoutesFromElements(
    <Route element={<RootProviders />}>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/activeren" element={<ActivatePage />} />
      <Route element={<RequireAuth />}>
        <Route path="/" element={<RootRedirect />} />
        <Route path="/change-password" element={<ChangePasswordPage />} />
        {/* Mobile-first driver shell (bottom tabs); execution pages stay shared under /my-trips. */}
        <Route element={<DriverLayout />}>
          <Route path="/driver" element={<DriverHomePage />} />
          <Route path="/driver/documents" element={<DriverDocumentsPage />} />
          <Route path="/driver/incidents" element={<DriverIncidentsPage />} />
        </Route>
        {/* Customer-portal shell: portal users never render the internal AppLayout sidebar. */}
        <Route element={<CustomerPortalLayout />}>
          <Route path="/klantportaal" element={<CustomerPortalOrdersPage />} />
          <Route path="/klantportaal/dashboard" element={<CustomerPortalDashboardPage />} />
          <Route path="/klantportaal/new" element={<CustomerPortalNewOrderPage />} />
          <Route path="/klantportaal/orders/:id" element={<CustomerPortalOrderDetailPage />} />
          <Route path="/klantportaal/documenten" element={<CustomerPortalDocumentsPage />} />
          <Route path="/klantportaal/facturen" element={<CustomerPortalInvoicesPage />} />
          <Route path="/klantportaal/facturen/:id" element={<CustomerPortalInvoiceDetailPage />} />
          <Route path="/klantportaal/berichten" element={<CustomerPortalMessagesPage />} />
          <Route path="/klantportaal/mededelingen" element={<CustomerPortalNoticesPage />} />
          <Route path="/klantportaal/gebruikers" element={<CustomerPortalUsersPage />} />
        </Route>
        <Route element={<InternalOnly />}>
        <Route element={<AppLayout />}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/transport-orders" element={<TransportOrdersPage />} />
          <Route path="/transport-orders/new" element={<NewTransportOrderPage />} />
          <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
          <Route path="/planning" element={<PlanningPage />} />
          <Route path="/planning-center" element={<PlanningCenterPage />} />
          <Route path="/operations" element={<OperationsPage />} />
          <Route path="/profitability" element={<ProfitabilityPage />} />
          <Route path="/warehouses" element={<WarehousesPage />} />
          <Route path="/warehouse/trace" element={<WarehouseTracePage />} />
          <Route path="/dock-planning" element={<DockPlanningPage />} />
          <Route path="/planning/:id" element={<TripDetailPage />} />
          <Route path="/exceptions" element={<ExceptionsPage />} />
          <Route path="/exceptions/:id" element={<ExceptionDetailPage />} />
          <Route path="/pods/:id" element={<PodDetailPage />} />
          <Route path="/packages/:id" element={<PackageDetailPage />} />
          <Route path="/warehouse" element={<WarehousePage />} />
          <Route path="/portal" element={<PortalDashboardPage />} />
          <Route path="/portal/planning" element={<PortalPlanningPage />} />
          <Route path="/employee-planning" element={<EmployeePlanningPage />} />
          <Route path="/messaging" element={<MessagingPage />} />
          <Route path="/edi" element={<EdiPage />} />
          <Route path="/peppol" element={<PeppolPage />} />
          <Route path="/integrations" element={<IntegrationsPage />} />
          <Route path="/portal/absences" element={<PortalAbsencesPage />} />
          <Route path="/portal/qualifications" element={<PortalQualificationsPage />} />
          <Route path="/portal/profile" element={<PortalProfilePage />} />
          <Route path="/my-trips" element={<MyTripsPage />} />
          <Route path="/my-trips/:id" element={<TripExecutionPage />} />
          <Route path="/invoices" element={<InvoicesPage />} />
          <Route path="/invoice-control" element={<InvoiceControlPage />} />
          <Route path="/invoices/new" element={<NewInvoicePage />} />
          <Route path="/invoices/:id" element={<InvoiceDetailPage />} />
          <Route path="/notifications" element={<NotificationsPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/customers/new" element={<NewCustomerPage />} />
          <Route path="/customers/:id" element={<CustomerDetailPage />} />
          {/* Driver profiles are merged into the personnel dossier: the list is the
              "Chauffeurs" view of /employees, and driver detail redirects to its tab. */}
          <Route path="/drivers" element={<Navigate to="/employees?view=chauffeurs" replace />} />
          <Route path="/drivers/new" element={<NewDriverPage />} />
          <Route path="/drivers/:id" element={<DriverDetailPage />} />
          <Route path="/fleet" element={<FleetDashboardPage />} />
          <Route path="/vehicles" element={<VehiclesPage />} />
          <Route path="/vehicles/new" element={<NewVehiclePage />} />
          <Route path="/vehicles/:id" element={<VehicleDetailPage />} />
          <Route path="/trailers" element={<TrailersPage />} />
          <Route path="/trailers/new" element={<NewTrailerPage />} />
          <Route path="/trailers/:id" element={<TrailerDetailPage />} />
          <Route path="/tank-cards" element={<TankCardsPage />} />
          <Route path="/maintenance-policies" element={<MaintenancePoliciesPage />} />
          <Route path="/dossiers" element={<DossiersPage />} />
          <Route path="/dossiers/new" element={<NewDossierPage />} />
          <Route path="/dossiers/:id" element={<DossierDetailPage />} />
          <Route path="/incidents" element={<IncidentsPage />} />
          <Route path="/incidents/new" element={<IncidentDetailPage />} />
          <Route path="/incidents/:id" element={<IncidentDetailPage />} />
          <Route path="/cost-rates" element={<CostRatesPage />} />
          <Route path="/settings/pricing" element={<PricingSettingsPage />} />
          <Route path="/pricing/tables" element={<PricingTablesPage />} />
          <Route path="/pricing/tables/:id" element={<PricingTableDetailPage />} />
          <Route path="/kpi" element={<KpiDashboardPage />} />
          <Route path="/kpi/trips" element={<KpiTripsPage />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/reports/:reportId" element={<ReportViewerPage />} />
          <Route path="/locations" element={<LocationsPage />} />
          <Route path="/locations/new" element={<NewLocationPage />} />
          <Route path="/locations/:id" element={<LocationDetailPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/users/new" element={<NewUserPage />} />
          <Route path="/users/:id" element={<UserDetailPage />} />
          <Route path="/roles" element={<RolesPage />} />
          <Route path="/job-function-mappings" element={<JobFunctionMappingsPage />} />
          <Route path="/inbox" element={<InboxPage />} />
          <Route path="/roles/:id" element={<RoleDetailPage />} />
          <Route path="/employees" element={<EmployeesPage />} />
          <Route path="/employees/new" element={<NewEmployeePage />} />
          <Route path="/employees/:id" element={<EmployeeDetailPage />} />
          <Route path="/settings/hr-reminders" element={<HrReminderSettingsPage />} />
          <Route path="/absences" element={<AbsencesPage />} />
          <Route path="/qualifications" element={<QualificationsOverviewPage />} />
          <Route path="/tasks" element={<TasksPage />} />
          <Route path="/settings/task-templates" element={<TaskTemplatesPage />} />
          <Route
            path="/master-data"
            element={<Navigate to={`/master-data/${LOOKUP_RESOURCES[0].slug}`} replace />}
          />
          <Route path="/master-data/eenheden" element={<UnitTypesPage />} />
          <Route path="/master-data/services" element={<ServiceOptionsPage />} />
          <Route path="/master-data/:resource" element={<LookupPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/settings/legal-entities" element={<LegalEntitiesPage />} />
          <Route path="/settings/issued-item-templates" element={<IssuedItemTemplatesPage />} />
          <Route path="/settings/issued-item-templates/:id" element={<IssuedItemTemplateDetailPage />} />
          <Route path="/inventory" element={<InventoryOverviewPage />} />
          <Route path="/settings/leave" element={<LeaveSettingsPage />} />
          <Route path="/settings/activity-types" element={<ActivityTypesPage />} />
          <Route path="/settings/accounting" element={<AccountingSettingsPage />} />
          <Route path="/settings/notifications" element={<NotificationAdminPage />} />
          <Route path="/settings/escalations" element={<EscalationSettingsPage />} />
          <Route path="/settings/portal-announcements" element={<PortalAnnouncementsSettingsPage />} />
          <Route path="/settings/portal-messages" element={<PortalMessagesPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
        </Route>
      </Route>
    </Route>,
  ),
)

export function AppRoutes() {
  return <RouterProvider router={router} />
}
