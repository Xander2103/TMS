import { Navigate, Outlet } from 'react-router-dom'
import { getNavModules } from '../components/layout/nav/navConfig'
import { findFirstPermittedRoute } from '../components/layout/nav/navState'
import { useAuth } from '../features/auth/authContextValue'
import { isPortalUser } from './portalUser'

/**
 * UX-1: the desk each operational role works from, decided by what the user may actually do, never by
 * role name. First match wins; every entry also requires the target page's own right, so a custom role
 * without it falls through. Only administrators keep the general dossier list.
 */
const ROLE_LANDINGS: { requires: string[]; to: string }[] = [
  { requires: ['roles.manage_permissions', 'dossiers.view'], to: '/dossiers' },
  { requires: ['planning.edit', 'orders.create', 'planning.view'], to: '/planning-center' },
  { requires: ['planning.edit', 'operations.view'], to: '/operations' },
  { requires: ['invoices.change_status', 'invoices.view'], to: '/invoices' },
  { requires: ['vehicles.edit', 'vehicles.view'], to: '/fleet' },
  { requires: ['employees.edit', 'employees.view'], to: '/employees' },
  { requires: ['inventory.issue'], to: '/inventory/issues' },
  { requires: ['operations.view', 'dashboard.view'], to: '/dashboard' },
]

/** A driver's work lives in the driver shell; someone who can also plan keeps their board. */
function landingRoute(hasPermission: (code: string) => boolean): string | null {
  const desk = ROLE_LANDINGS.find((entry) => entry.requires.every(hasPermission))
  if (desk) return desk.to
  if (hasPermission('driver_workflow.execute') && !hasPermission('planning.view')) return '/driver'
  return null
}

/** Post-login/root landing: portal users go straight to their shell; internal users land on their
 * role's desk (see ROLE_LANDINGS), then the dossier list, with the classic order list as fallback for
 * roles without dossiers.view. A role with none of these lands on the first sidebar entry it may
 * open: never on a page that answers 403. */
export function RootRedirect() {
  const { user, hasPermission, hasAnyPermission } = useAuth()
  if (isPortalUser(user)) return <Navigate to="/klantportaal" replace />
  const landing = landingRoute(hasPermission)
  if (landing) return <Navigate to={landing} replace />
  if (hasPermission('dossiers.view')) return <Navigate to="/dossiers" replace />
  if (hasPermission('orders.view')) return <Navigate to="/transport-orders" replace />
  return <Navigate to={findFirstPermittedRoute(getNavModules(), hasAnyPermission) ?? '/inbox'} replace />
}

/** Guards the internal app shell: a portal user who navigates here directly (e.g. a stale
 * bookmark) is bounced to their own shell instead of ever rendering the internal sidebar. */
export function InternalOnly() {
  const { user } = useAuth()
  return isPortalUser(user) ? <Navigate to="/klantportaal" replace /> : <Outlet />
}
