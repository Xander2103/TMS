import { Suspense, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { useActionQueueSync } from '../../hooks/useActionQueueSync'
import { LoadingState } from '../feedback/LoadingState'
import { CommandPalette } from './CommandPalette'
import { OfflineBanner } from './OfflineBanner'
import { Sidebar } from './Sidebar'
import './AppLayout.css'

/**
 * Desktop keeps the fixed sidebar; below 900px the sidebar becomes an off-canvas drawer
 * behind a hamburger — the portal must work one-handed on a phone. Navigating from the
 * drawer closes it (Sidebar calls onNavigate on every link click).
 */
export function AppLayout() {
  const [navOpen, setNavOpen] = useState(false)
  // Offline queues (scans + driver actions) replay automatically when the connection returns.
  const queues = useActionQueueSync()

  return (
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
      <CommandPalette />
      <main className="content">
        {/* Pages are code-split per route; the shell stays visible while a chunk loads. */}
        <Suspense fallback={<LoadingState message="Pagina laden..." />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  )
}
