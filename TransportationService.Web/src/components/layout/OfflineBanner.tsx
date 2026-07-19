import { useOnlineStatus } from '../../hooks/useOnlineStatus'
import './OfflineBanner.css'

/**
 * Connectivity indicator: visible whenever the browser reports offline. Honest about
 * capability — there is no offline sync, so users know changes must wait.
 */
export function OfflineBanner() {
  const online = useOnlineStatus()
  if (online) return null

  return (
    <div className="offline-banner" role="status" aria-live="polite">
      <span aria-hidden="true">⚠</span> Offline — wijzigingen zijn pas mogelijk zodra de verbinding terugkeert.
    </div>
  )
}
