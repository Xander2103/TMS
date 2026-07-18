import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from '../features/auth/AuthContext'
import { LoginPage } from '../features/auth/LoginPage'
import { RequireAuth } from '../features/auth/RequireAuth'
import { AppLayout } from '../components/layout/AppLayout'
import { NotFoundPage } from '../components/feedback/NotFoundPage'
import { DashboardPage } from '../features/dashboard/pages/DashboardPage'
import { TransportOrdersPage } from '../features/transport-orders/pages/TransportOrdersPage'
import { NewTransportOrderPage } from '../features/transport-orders/pages/NewTransportOrderPage'
import { CustomersPage } from '../features/customers/pages/CustomersPage'
import { NewCustomerPage } from '../features/customers/pages/NewCustomerPage'
import { CustomerDetailPage } from '../features/customers/pages/CustomerDetailPage'
import { DriversPage } from '../features/drivers/pages/DriversPage'
import { VehiclesPage } from '../features/vehicles/pages/VehiclesPage'
import { SettingsPage } from '../features/settings/pages/SettingsPage'
import { UsersPage } from '../features/users/pages/UsersPage'
import { NewUserPage } from '../features/users/pages/NewUserPage'
import { UserDetailPage } from '../features/users/pages/UserDetailPage'
import { RolesPage } from '../features/roles/pages/RolesPage'
import { RoleDetailPage } from '../features/roles/pages/RoleDetailPage'
import { EmployeesPage } from '../features/employees/pages/EmployeesPage'
import { NewEmployeePage } from '../features/employees/pages/NewEmployeePage'
import { EmployeeDetailPage } from '../features/employees/pages/EmployeeDetailPage'
import { LookupPage } from '../features/master-data/pages/LookupPage'
import { LOOKUP_RESOURCES } from '../features/master-data/lookupRegistry'

export function AppRoutes() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<RequireAuth />}>
            <Route element={<AppLayout />}>
              <Route path="/" element={<Navigate to="/transport-orders" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/transport-orders" element={<TransportOrdersPage />} />
          <Route path="/transport-orders/new" element={<NewTransportOrderPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/customers/new" element={<NewCustomerPage />} />
          <Route path="/customers/:id" element={<CustomerDetailPage />} />
          <Route path="/drivers" element={<DriversPage />} />
          <Route path="/vehicles" element={<VehiclesPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/users/new" element={<NewUserPage />} />
          <Route path="/users/:id" element={<UserDetailPage />} />
          <Route path="/roles" element={<RolesPage />} />
          <Route path="/roles/:id" element={<RoleDetailPage />} />
          <Route path="/employees" element={<EmployeesPage />} />
          <Route path="/employees/new" element={<NewEmployeePage />} />
          <Route path="/employees/:id" element={<EmployeeDetailPage />} />
          <Route
            path="/master-data"
            element={<Navigate to={`/master-data/${LOOKUP_RESOURCES[0].slug}`} replace />}
          />
          <Route path="/master-data/:resource" element={<LookupPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}
