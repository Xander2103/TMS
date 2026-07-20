import { NavLink, Outlet } from 'react-router-dom'
import { useActionQueueSync } from '../../../hooks/useActionQueueSync'
import { useOnlineStatus } from '../../../hooks/useOnlineStatus'
import './driver-layout.css'

/**
 * Mobile-first shell for the driver pages: sticky status bar (offline/sync state) and a
 * thumb-reachable bottom tab bar. Trip execution itself reuses the existing /my-trips pages
 * — one workflow, no duplicated execution logic.
 */
export function DriverLayout() {
  const online = useOnlineStatus()
  const { unsyncedCount } = useActionQueueSync()

  return (
    <div className="drv-shell">
      <header className="drv-topbar">
        <span className="drv-title">Chauffeur</span>
        <span className={`drv-status${online ? '' : ' drv-status-offline'}`}>
          {online ? 'Online' : 'Offline'}
          {unsyncedCount > 0 && <span className="drv-sync-badge">{unsyncedCount}</span>}
        </span>
      </header>
      <main className="drv-content">
        <Outlet />
      </main>
      <nav className="drv-tabs" aria-label="Chauffeursnavigatie">
        <NavLink to="/driver" end className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">🏠</span>Vandaag
        </NavLink>
        <NavLink to="/my-trips" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">🚚</span>Ritten
        </NavLink>
        <NavLink to="/driver/incidents" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">⚠️</span>Incident
        </NavLink>
        <NavLink to="/driver/documents" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">📄</span>Documenten
        </NavLink>
        <NavLink to="/inbox" className={({ isActive }) => (isActive ? 'drv-tab-active' : undefined)}>
          <span aria-hidden="true">✉️</span>Berichten
        </NavLink>
      </nav>
    </div>
  )
}
