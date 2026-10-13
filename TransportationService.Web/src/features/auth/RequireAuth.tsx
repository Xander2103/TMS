import { useEffect, useState } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { useLocale } from '../../i18n/localeContext'
import { useAuth, type SessionUnavailableReason } from './authContextValue'
import './RequireAuth.css'

const REASON_KEY: Record<SessionUnavailableReason, string> = {
  network: 'ui.session.network',
  rate_limited: 'ui.session.rateLimited',
  server: 'ui.session.server',
}

/**
 * Shown while the session cannot be restored for a reason that is not the user's (B01). The
 * refresh cookie is untouched, so one successful retry brings them straight back to the page
 * they asked for — no password, no lost context. Also retries by itself when the browser
 * reports the network coming back.
 */
function SessionUnavailable({ reason, onRetry }: { reason: SessionUnavailableReason | null; onRetry: () => Promise<void> }) {
  const { t } = useLocale()
  const [busy, setBusy] = useState(false)

  async function retry() {
    setBusy(true)
    try {
      await onRetry()
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    const handleOnline = () => void retry()
    window.addEventListener('online', handleOnline)
    return () => window.removeEventListener('online', handleOnline)
    // eslint-disable-next-line react-hooks/exhaustive-deps -- retry is stable for this screen
  }, [])

  return (
    <div className="auth-loading" role="alert">
      <span className="auth-unavailable-icon" aria-hidden="true">⚠</span>
      <h2 className="auth-unavailable-title">{t('ui.session.unavailableTitle')}</h2>
      <p className="auth-unavailable-text">{t(REASON_KEY[reason ?? 'server'])}</p>
      <Button onClick={() => void retry()} disabled={busy}>
        {busy ? t('ui.session.retrying') : t('ui.session.retry')}
      </Button>
    </div>
  )
}

/**
 * Gate for all protected routes. While auth state is resolving it shows a neutral loading screen
 * (so protected pages never fire API calls before the session is known). Unauthenticated users are
 * redirected to /login with the originally requested location preserved for post-login return —
 * except after an explicit sign-out: the page being left is not a request, and the next sign-in
 * (maybe another account) must get its own permission-driven landing page via "/".
 */
export function RequireAuth() {
  const { status, user, signedOut, unavailableReason, retrySession } = useAuth()
  const location = useLocation()

  if (status === 'unavailable') {
    return <SessionUnavailable reason={unavailableReason} onRetry={retrySession} />
  }

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
