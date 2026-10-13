import { NavLink, Outlet } from 'react-router-dom'
import { DisplayPreferencesProvider } from '../../../components/layout/DisplayPreferencesProvider'
import { useActionQueueSync } from '../../../hooks/useActionQueueSync'
import { useOnlineStatus } from '../../../hooks/useOnlineStatus'
import { useLocale } from '../../../i18n/localeContext'
import './driver-layout.css'

/**
 * Mobile-first shell for the driver pages: sticky status bar (offline/sync state) and a
 * thumb-reachable bottom tab bar. Trip execution itself reuses the existing /my-trips pages
 * — one workflow, no duplicated execution logic.
 */
export function DriverLayout() {
  const online = useOnlineStatus()
  const { unsyncedCount } = useActionQueueSync()
  const { t } = useLocale()

  return (
    <div className="drv-shell">
      <header className="drv-topbar">
        <span className="drv-title">{t('driverApp.layout.title')}</span>
        <span className={`drv-status${online ? '' : ' drv-status-offline'}`}>
          {online ? t('driverApp.layout.online') : t('driverApp.layout.offline')}
          {unsyncedCount > 0 && <span className="drv-sync-badge">{unsyncedCount}</span>}
        </span>
      </header>
      <main className="drv-content">
        {/* C-03: the driver shell is mounted outside AppLayout (AppRoutes), so it needs the
            shared regional bootstrap of its own — without it every driver screen rendered its
            timestamps on the seeded default zone and format. */}
        <DisplayPreferencesProvider>
          <Outlet />
        </DisplayPreferencesProvider>
      </main>
      <nav className="drv-tabs" aria-label={t('driverApp.layout.navLabel')}>
        <NavLink to="/driver" end className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">🏠</span>{t('driverApp.layout.tabToday')}
        </NavLink>
        <NavLink to="/my-trips" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">🚚</span>{t('driverApp.layout.tabTrips')}
        </NavLink>
        <NavLink to="/driver/incidents" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">⚠️</span>{t('driverApp.layout.tabIncident')}
        </NavLink>
        <NavLink to="/driver/documents" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">📄</span>{t('driverApp.layout.tabDocuments')}
        </NavLink>
        <NavLink to="/inbox" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">✉️</span>{t('driverApp.layout.tabMessages')}
        </NavLink>
      </nav>
    </div>
  )
}
