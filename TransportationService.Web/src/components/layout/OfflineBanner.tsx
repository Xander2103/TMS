import { useOnlineStatus } from '../../hooks/useOnlineStatus'
import { useLocale } from '../../i18n/localeContext'
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
  const { t } = useLocale()
  const online = useOnlineStatus()
  if (online && unsyncedCount === 0) return null

  return (
    <div className="offline-banner" role="status" aria-live="polite">
      <span aria-hidden="true">⚠</span>{' '}
      {online
        ? t('ui.offline.pendingSync', { count: unsyncedCount })
        : unsyncedCount > 0
          ? t('ui.offline.queued', { count: unsyncedCount })
          : t('ui.offline.banner')}
    </div>
  )
}
