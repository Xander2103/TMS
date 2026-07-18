import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './authContextValue'
import './RequireAuth.css'

/**
 * Gate for all protected routes. While auth state is resolving it shows a neutral loading screen
 * (so protected pages never fire API calls before the session is known). Unauthenticated users are
 * redirected to /login with the originally requested location preserved for post-login return.
 */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div className="auth-loading" role="status" aria-live="polite">
        <span className="auth-loading-spinner" aria-hidden="true" />
        <span className="auth-loading-text">Bezig met laden…</span>
      </div>
    )
  }

  if (status === 'unauthenticated') {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <Outlet />
}
