import { Suspense, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { useActionQueueSync } from '../../hooks/useActionQueueSync'
import { useShortcutRegistry } from '../../hooks/useShortcutRegistry'
import { NotificationBell } from '../../features/notifications/components/NotificationBell'
import { NotificationsProvider } from '../../features/notifications/notificationsContext'
import { LoadingState } from '../feedback/LoadingState'
import { Modal } from '../ui/Modal'
import { CommandPalette } from './CommandPalette'
import { OfflineBanner } from './OfflineBanner'
import { Sidebar } from './Sidebar'
import './AppLayout.css'

/**
 * Desktop keeps the fixed sidebar; below 900px the sidebar becomes an off-canvas drawer
 * behind a hamburger — the portal must work one-handed on a phone. Navigating from the
 * drawer closes it (Sidebar calls onNavigate on every link click).
 *
 * NotificationsProvider lives here (behind RequireAuth + InternalOnly) so only signed-in
 * internal users poll the unread count; sidebar badge and NotificationBell share that state.
 */
export function AppLayout() {
  const [navOpen, setNavOpen] = useState(false)
  // Offline queues (scans + driver actions) replay automatically when the connection returns.
  const queues = useActionQueueSync()
  // Central keyboard shortcuts: mod+K/'/' palette, 'g x' navigation chords, '?' help.
  const shortcuts = useShortcutRegistry()

  return (
    <NotificationsProvider>
      <div className="app-shell">
        <OfflineBanner unsyncedCount={queues.unsyncedCount} />
        <header className="mobile-topbar">
          <button
            type="button"
            className="mobile-nav-toggle"
            onClick={() => setNavOpen((open) => !open)}
            aria-label={navOpen ? 'Menu sluiten' : 'Menu openen'}
            aria-expanded={navOpen}
          >
            ☰
          </button>
          <span className="mobile-topbar-title">Transportation Service</span>
        </header>
        {navOpen && <div className="mobile-nav-overlay" onClick={() => setNavOpen(false)} aria-hidden="true" />}
        <Sidebar open={navOpen} onNavigate={() => setNavOpen(false)} />
        <CommandPalette open={shortcuts.paletteOpen} onClose={() => shortcuts.setPaletteOpen(false)} />
        {shortcuts.helpOpen && (
          <Modal title="Sneltoetsen" onClose={() => shortcuts.setHelpOpen(false)}>
            <ul className="shortcut-help-list">
              {shortcuts.availableShortcuts.map((shortcut) => (
                <li key={shortcut.keys}>
                  <kbd>{shortcut.keys.replace('mod', navigator.platform.includes('Mac') ? '⌘' : 'Ctrl')}</kbd>
                  <span>{shortcut.label}</span>
                </li>
              ))}
            </ul>
          </Modal>
        )}
        <div className="app-main">
          {/* Slim header zone: on desktop a right-aligned strip above the page content,
              on mobile the bell overlays the fixed mobile-topbar's right edge. */}
          <header className="app-topbar">
            <NotificationBell />
          </header>
          <main className="content">
            {/* Pages are code-split per route; the shell stays visible while a chunk loads. */}
            <Suspense fallback={<LoadingState message="Pagina laden..." />}>
              <Outlet />
            </Suspense>
          </main>
        </div>
      </div>
    </NotificationsProvider>
  )
}
