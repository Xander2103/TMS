import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../features/auth/authContextValue'
import { isPortalUser } from './portalUser'

/** Post-login/root landing: portal users go straight to their shell; internal users land on
 * the dossier list (Wave 1: het dossier is het centrale werkobject) when they may see it,
 * with the classic order list as fallback for roles without dossiers.view. */
export function RootRedirect() {
  const { user, hasPermission } = useAuth()
  if (isPortalUser(user)) return <Navigate to="/klantportaal" replace />
  return <Navigate to={hasPermission('dossiers.view') ? '/dossiers' : '/transport-orders'} replace />
}

/** Guards the internal app shell: a portal user who navigates here directly (e.g. a stale
 * bookmark) is bounced to their own shell instead of ever rendering the internal sidebar. */
export function InternalOnly() {
  const { user } = useAuth()
  return isPortalUser(user) ? <Navigate to="/klantportaal" replace /> : <Outlet />
}
