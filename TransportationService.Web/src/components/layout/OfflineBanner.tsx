import { useOnlineStatus } from '../../hooks/useOnlineStatus'
import './OfflineBanner.css'

interface OfflineBannerProps {
  /** Pending offline actions (scans + driver actions) waiting for sync. */
  unsyncedCount?: number
}

/**
 * Connectivity indicator. Offline it explains that queued driver actions (scans, stop
 * updates, incident reports) sync automatically; back online it stays visible only while
 * queued work remains, so unsynced actions are never invisible.
 */
export function OfflineBanner({ unsyncedCount = 0 }: OfflineBannerProps) {
  const online = useOnlineStatus()
  if (online && unsyncedCount === 0) return null

  return (
    <div className="offline-banner" role="status" aria-live="polite">
      <span aria-hidden="true">⚠</span>{' '}
      {online
        ? `${unsyncedCount} actie(s) wachten op synchronisatie…`
        : unsyncedCount > 0
          ? `Offline — ${unsyncedCount} actie(s) in de wachtrij; ze synchroniseren automatisch zodra de verbinding terugkeert.`
          : 'Offline — scans, stopacties en incidentmeldingen komen in de wachtrij en synchroniseren automatisch.'}
    </div>
  )
}
