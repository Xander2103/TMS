import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from '../components/layout/AppLayout'
import { NotFoundPage } from '../components/feedback/NotFoundPage'
import { DashboardPage } from '../features/dashboard/pages/DashboardPage'
import { TransportOrdersPage } from '../features/transport-orders/pages/TransportOrdersPage'
import { NewTransportOrderPage } from '../features/transport-orders/pages/NewTransportOrderPage'
import { CustomersPage } from '../features/customers/pages/CustomersPage'
import { DriversPage } from '../features/drivers/pages/DriversPage'
import { VehiclesPage } from '../features/vehicles/pages/VehiclesPage'
import { SettingsPage } from '../features/settings/pages/SettingsPage'
import { UsersPage } from '../features/users/pages/UsersPage'
import { NewUserPage } from '../features/users/pages/NewUserPage'
import { UserDetailPage } from '../features/users/pages/UserDetailPage'

export function AppRoutes() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<AppLayout />}>
          <Route path="/" element={<Navigate to="/transport-orders" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/transport-orders" element={<TransportOrdersPage />} />
          <Route path="/transport-orders/new" element={<NewTransportOrderPage />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/drivers" element={<DriversPage />} />
          <Route path="/vehicles" element={<VehiclesPage />} />
          <Route path="/users" element={<UsersPage />} />
          <Route path="/users/new" element={<NewUserPage />} />
          <Route path="/users/:id" element={<UserDetailPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
