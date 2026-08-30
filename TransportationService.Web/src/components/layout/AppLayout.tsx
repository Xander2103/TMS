import { Suspense, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { useAuth } from '../../features/auth/authContextValue'
import { useLocale } from '../../i18n/localeContext'
import { isLocale } from '../../i18n/translations'
import { DisplayPreferencesProvider, useDisplayPreferences } from './DisplayPreferencesProvider'
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
  const { user } = useAuth()
  const { applyFallbackLocale, t } = useLocale()
  // Offline queues (scans + driver actions) replay automatically when the connection returns.
  const queues = useActionQueueSync()

  // Regional display preferences (date format, decimal separator, TENANT TIME ZONE) are loaded
  // and applied by the shared bootstrap — see DisplayPreferencesProvider, which also gates the
  // routed content below so no page renders a timestamp before the zone is known.
  // Taalresolutie: heeft de gebruiker géén eigen voorkeur, dan geldt de tenant-default
  // als fallback (§7/§9) — een bewuste sessiewissel blijft altijd winnen. Dat stuk blijft hier,
  // omdat alleen deze shell de ingelogde interne gebruiker kent.
  const hasOwnLanguage = user?.preferredLanguage != null
  const { preferences } = useDisplayPreferences()
  useEffect(() => {
    if (!hasOwnLanguage && isLocale(preferences?.defaultLanguage)) {
      applyFallbackLocale(preferences.defaultLanguage)
    }
  }, [applyFallbackLocale, hasOwnLanguage, preferences])
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
            aria-label={navOpen ? t('ui.nav.closeMenu') : t('ui.nav.openMenu')}
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
          <Modal title={t('ui.nav.shortcuts')} onClose={() => shortcuts.setHelpOpen(false)}>
            <ul className="shortcut-help-list">
              {shortcuts.availableShortcuts.map((shortcut) => (
                <li key={shortcut.keys}>
                  <kbd>{shortcut.keys.replace('mod', navigator.platform.includes('Mac') ? '⌘' : 'Ctrl')}</kbd>
                  <span>{t(shortcut.label)}</span>
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
            <Suspense fallback={<LoadingState message={t('ui.nav.pageLoading')} />}>
              <DisplayPreferencesProvider fallback={<LoadingState message={t('ui.nav.pageLoading')} />}>
                <Outlet />
              </DisplayPreferencesProvider>
            </Suspense>
          </main>
        </div>
      </div>
    </NotificationsProvider>
  )
}
