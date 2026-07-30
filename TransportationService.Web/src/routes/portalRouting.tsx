import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../features/auth/authContextValue'
import { isPortalUser } from './portalUser'

/** Post-login/root landing: portal users go straight to their shell, everyone else keeps the
 * existing internal default. */
export function RootRedirect() {
  const { user } = useAuth()
  return <Navigate to={isPortalUser(user) ? '/klantportaal' : '/transport-orders'} replace />
}

/** Guards the internal app shell: a portal user who navigates here directly (e.g. a stale
 * bookmark) is bounced to their own shell instead of ever rendering the internal sidebar. */
export function InternalOnly() {
  const { user } = useAuth()
  return isPortalUser(user) ? <Navigate to="/klantportaal" replace /> : <Outlet />
}
