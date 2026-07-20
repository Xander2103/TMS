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
import { NotFoundPage } from '../components/feedback/NotFoundPage'
import { DashboardPage } from '../features/dashboard/pages/DashboardPage'
import { TransportOrdersPage } from '../features/transport-orders/pages/TransportOrdersPage'
import { NewTransportOrderPage } from '../features/transport-orders/pages/NewTransportOrderPage'
import { TransportOrderDetailPage } from '../features/transport-orders/pages/TransportOrderDetailPage'
import { PlanningPage } from '../features/planning/pages/PlanningPage'
import { TripDetailPage } from '../features/planning/pages/TripDetailPage'
import { MyTripsPage } from '../features/my-trips/pages/MyTripsPage'
import { TripExecutionPage } from '../features/my-trips/pages/TripExecutionPage'
import { ExceptionsPage } from '../features/exceptions/pages/ExceptionsPage'
import { ExceptionDetailPage } from '../features/exceptions/pages/ExceptionDetailPage'
import { PodDetailPage } from '../features/pod/pages/PodDetailPage'
import { PackageDetailPage } from '../features/packages/pages/PackageDetailPage'
import { WarehousePage } from '../features/packages/pages/WarehousePage'
import { PortalDashboardPage } from '../features/portal/pages/PortalDashboardPage'
import { PortalPlanningPage } from '../features/portal/pages/PortalPlanningPage'
import { EmployeePlanningPage } from '../features/employee-planning/pages/EmployeePlanningPage'
import { MessagingPage } from '../features/messaging/pages/MessagingPage'
import { EdiPage } from '../features/edi/pages/EdiPage'
import { IntegrationsPage } from '../features/integrations/pages/IntegrationsPage'
import { PortalAbsencesPage } from '../features/portal/pages/PortalAbsencesPage'
import { PortalQualificationsPage } from '../features/portal/pages/PortalQualificationsPage'
import { PortalProfilePage } from '../features/portal/pages/PortalProfilePage'
import { InvoicesPage } from '../features/invoices/pages/InvoicesPage'
import { NewInvoicePage } from '../features/invoices/pages/NewInvoicePage'
import { InvoiceDetailPage } from '../features/invoices/pages/InvoiceDetailPage'
import { NotificationsPage } from '../features/notifications/pages/NotificationsPage'
import { CustomersPage } from '../features/customers/pages/CustomersPage'
import { NewCustomerPage } from '../features/customers/pages/NewCustomerPage'
import { CustomerDetailPage } from '../features/customers/pages/CustomerDetailPage'
import { DriversPage } from '../features/drivers/pages/DriversPage'
import { NewDriverPage } from '../features/drivers/pages/NewDriverPage'
import { DriverDetailPage } from '../features/drivers/pages/DriverDetailPage'
import { VehiclesPage } from '../features/vehicles/pages/VehiclesPage'
import { NewVehiclePage } from '../features/vehicles/pages/NewVehiclePage'
import { VehicleDetailPage } from '../features/vehicles/pages/VehicleDetailPage'
import { TrailersPage } from '../features/trailers/pages/TrailersPage'
import { NewTrailerPage } from '../features/trailers/pages/NewTrailerPage'
import { TrailerDetailPage } from '../features/trailers/pages/TrailerDetailPage'
import { AbsencesPage } from '../features/absences/pages/AbsencesPage'
import { FleetDashboardPage } from '../features/fleet-dashboard/pages/FleetDashboardPage'
import { TankCardsPage } from '../features/tank-cards/pages/TankCardsPage'
import { MaintenancePoliciesPage } from '../features/maintenance-policies/pages/MaintenancePoliciesPage'
import { CustomerPortalOrdersPage } from '../features/customer-portal/pages/CustomerPortalOrdersPage'
import { CustomerPortalNewOrderPage } from '../features/customer-portal/pages/CustomerPortalNewOrderPage'
import { CustomerPortalOrderDetailPage } from '../features/customer-portal/pages/CustomerPortalOrderDetailPage'
import { ChangePasswordPage, ForgotPasswordPage, ResetPasswordPage } from '../features/auth/PasswordFlowPages'
import { JobFunctionMappingsPage } from '../features/roles/pages/JobFunctionMappingsPage'
import { CostRatesPage } from '../features/trip-costing/pages/CostRatesPage'
import { KpiDashboardPage } from '../features/kpi/pages/KpiDashboardPage'
import { KpiTripsPage } from '../features/kpi/pages/KpiTripsPage'
import { LocationsPage } from '../features/locations/pages/LocationsPage'
import { NewLocationPage } from '../features/locations/pages/NewLocationPage'
import { LocationDetailPage } from '../features/locations/pages/LocationDetailPage'
import { SettingsPage } from '../features/settings/pages/SettingsPage'
import { UsersPage } from '../features/users/pages/UsersPage'
import { NewUserPage } from '../features/users/pages/NewUserPage'
import { UserDetailPage } from '../features/users/pages/UserDetailPage'
import { RolesPage } from '../features/roles/pages/RolesPage'
import { RoleDetailPage } from '../features/roles/pages/RoleDetailPage'
import { EmployeesPage } from '../features/employees/pages/EmployeesPage'
import { QualificationsOverviewPage } from '../features/qualifications/pages/QualificationsOverviewPage'
import { NewEmployeePage } from '../features/employees/pages/NewEmployeePage'
import { EmployeeDetailPage } from '../features/employees/pages/EmployeeDetailPage'
import { LookupPage } from '../features/master-data/pages/LookupPage'
import { LOOKUP_RESOURCES } from '../features/master-data/lookupRegistry'

/** Root layout route: providers that need to live inside the router render an Outlet. */
function RootProviders() {
  return (
    <AuthProvider>
      <Outlet />
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
      <Route element={<RequireAuth />}>
        <Route path="/change-password" element={<ChangePasswordPage />} />
        <Route element={<AppLayout />}>
              <Route path="/" element={<Navigate to="/transport-orders" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/transport-orders" element={<TransportOrdersPage />} />
          <Route path="/transport-orders/new" element={<NewTransportOrderPage />} />
          <Route path="/transport-orders/:id" element={<TransportOrderDetailPage />} />
          <Route path="/planning" element={<PlanningPage />} />
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
          <Route path="/integrations" element={<IntegrationsPage />} />
          <Route path="/portal/absences" element={<PortalAbsencesPage />} />
          <Route path="/portal/qualifications" element={<PortalQualificationsPage />} />
          <Route path="/portal/profile" element={<PortalProfilePage />} />
          <Route path="/my-trips" element={<MyTripsPage />} />
          <Route path="/my-trips/:id" element={<TripExecutionPage />} />
          <Route path="/invoices" element={<InvoicesPage />} />
          <Route path="/invoices/new" element={<NewInvoicePage />} />
          <Route path="/invoices/:id" element={<InvoiceDetailPage />} />
          <Route path="/notifications" element={<NotificationsPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/customers/new" element={<NewCustomerPage />} />
          <Route path="/customers/:id" element={<CustomerDetailPage />} />
          <Route path="/drivers" element={<DriversPage />} />
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
          <Route path="/customer-portal" element={<CustomerPortalOrdersPage />} />
          <Route path="/customer-portal/new" element={<CustomerPortalNewOrderPage />} />
          <Route path="/customer-portal/orders/:id" element={<CustomerPortalOrderDetailPage />} />
          <Route path="/cost-rates" element={<CostRatesPage />} />
          <Route path="/kpi" element={<KpiDashboardPage />} />
          <Route path="/kpi/trips" element={<KpiTripsPage />} />
          <Route path="/locations" element={<LocationsPage />} />
          <Route path="/locations/new" element={<NewLocationPage />} />
          <Route path="/locations/:id" element={<LocationDetailPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/users/new" element={<NewUserPage />} />
          <Route path="/users/:id" element={<UserDetailPage />} />
          <Route path="/roles" element={<RolesPage />} />
          <Route path="/job-function-mappings" element={<JobFunctionMappingsPage />} />
          <Route path="/roles/:id" element={<RoleDetailPage />} />
          <Route path="/employees" element={<EmployeesPage />} />
          <Route path="/employees/new" element={<NewEmployeePage />} />
          <Route path="/employees/:id" element={<EmployeeDetailPage />} />
          <Route path="/absences" element={<AbsencesPage />} />
          <Route path="/qualifications" element={<QualificationsOverviewPage />} />
          <Route
            path="/master-data"
            element={<Navigate to={`/master-data/${LOOKUP_RESOURCES[0].slug}`} replace />}
          />
          <Route path="/master-data/:resource" element={<LookupPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>
    </Route>,
  ),
)

export function AppRoutes() {
  return <RouterProvider router={router} />
}
