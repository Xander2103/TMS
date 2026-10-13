import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './authContextValue'
import './RequireAuth.css'

/**
 * Gate for all protected routes. While auth state is resolving it shows a neutral loading screen
 * (so protected pages never fire API calls before the session is known). Unauthenticated users are
 * redirected to /login with the originally requested location preserved for post-login return —
 * except after an explicit sign-out: the page being left is not a request, and the next sign-in
 * (maybe another account) must get its own permission-driven landing page via "/".
 */
export function RequireAuth() {
  const { status, user, signedOut } = useAuth()
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
    return <Navigate to="/login" state={signedOut ? undefined : { from: location }} replace />
  }

  // A temporary credential must be replaced before anything else is reachable…
  if (user?.mustChangePassword && location.pathname !== '/change-password') {
    return <Navigate to="/change-password" replace />
  }

  // …and the forced-change screen exists only for that: without the flag (stale bookmark, a
  // remembered return location) the user goes to the root, which picks the landing page by permission.
  if (user && !user.mustChangePassword && location.pathname === '/change-password') {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
