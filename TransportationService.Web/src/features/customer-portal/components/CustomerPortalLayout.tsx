import { useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../auth/authContextValue'
import { DisplayPreferencesProvider } from '../../../components/layout/DisplayPreferencesProvider'
import { LocaleProvider } from '../../../i18n/LocaleProvider'
import { useLocale } from '../../../i18n/localeContext'
import { isLocale, type Locale } from '../../../i18n/translations'
import {
  getPortalContext,
  getPortalFeedUnreadCount,
  getPortalMessagesUnreadCount,
  type PortalContext,
} from '../api/customerPortalApi'
import { LanguageSwitcher } from './LanguageSwitcher'
import './customer-portal-layout.css'

interface NavItem {
  labelKey: string
  to: string
  permission?: string
  badgeKey?: 'messages' | 'notices'
}

const NAV_ITEMS: NavItem[] = [
  { labelKey: 'navigation.dashboard', to: '/klantportaal/dashboard' },
  { labelKey: 'navigation.notices', to: '/klantportaal/mededelingen', permission: 'customer_portal.view', badgeKey: 'notices' },
  { labelKey: 'navigation.orders', to: '/klantportaal', permission: 'customer_portal.view' },
  { labelKey: 'navigation.documents', to: '/klantportaal/documenten', permission: 'customer_portal.view_documents' },
  { labelKey: 'navigation.invoices', to: '/klantportaal/facturen', permission: 'customer_portal.view_invoices' },
  { labelKey: 'navigation.messages', to: '/klantportaal/berichten', permission: 'customer_portal.messages', badgeKey: 'messages' },
  { labelKey: 'navigation.users', to: '/klantportaal/gebruikers', permission: 'customer_portal.manage_users' },
  { labelKey: 'navigation.preferences', to: '/klantportaal/voorkeuren', permission: 'customer_portal.view' },
]

const UNREAD_POLL_MS = 60_000

/**
 * Dedicated shell for customer-portal users — the mirror of DriverLayout for the mobile driver
 * app. Portal users NEVER see the internal AppLayout sidebar; every /klantportaal/* route renders
 * exclusively inside this shell (see AppRoutes and the post-login redirect in RequireAuth/root).
 *
 * The layout is also the portal's i18n root: it loads the portal context once and mounts the
 * LocaleProvider around the whole /klantportaal tree, seeded with the user's saved language.
 */
export function CustomerPortalLayout() {
  const [context, setContext] = useState<PortalContext | null>(null)

  useEffect(() => {
    let mounted = true
    getPortalContext()
      .then((data) => {
        if (mounted) setContext(data)
      })
      .catch(() => {
        // Non-fatal: the top bar falls back to no company name, the locale to the browser language.
      })
    return () => {
      mounted = false
    }
  }, [])

  const preferredLanguage: Locale | null = context && isLocale(context.preferredLanguage) ? context.preferredLanguage : null

  return (
    <LocaleProvider preferredLanguage={preferredLanguage}>
      <CustomerPortalShell companyName={context?.customerName ?? null} />
    </LocaleProvider>
  )
}

function CustomerPortalShell({ companyName }: { companyName: string | null }) {
  const { user, logout, hasPermission } = useAuth()
  const { t } = useLocale()
  const [unreadMessages, setUnreadMessages] = useState(0)
  const [unreadNotices, setUnreadNotices] = useState(0)
  const canSeeMessages = hasPermission('customer_portal.messages')
  const canSeeNotices = hasPermission('customer_portal.view')

  // Light poll so the "Berichten" badge stays roughly current without a push channel — the
  // same idiom as the internal Sidebar's notification badge.
  useEffect(() => {
    if (!canSeeMessages) return
    let mounted = true
    const load = () => {
      getPortalMessagesUnreadCount()
        .then((data) => {
          if (mounted) setUnreadMessages(data.count)
        })
        .catch(() => {})
    }
    load()
    const timer = window.setInterval(load, UNREAD_POLL_MS)
    return () => {
      mounted = false
      window.clearInterval(timer)
    }
  }, [canSeeMessages])

  // Same poll idiom for the staff-authored "Mededelingen" feed badge.
  useEffect(() => {
    if (!canSeeNotices) return
    let mounted = true
    const load = () => {
      getPortalFeedUnreadCount()
        .then((data) => {
          if (mounted) setUnreadNotices(data.count)
        })
        .catch(() => {})
    }
    load()
    const timer = window.setInterval(load, UNREAD_POLL_MS)
    return () => {
      mounted = false
      window.clearInterval(timer)
    }
  }, [canSeeNotices])

  return (
    <div className="cpl-shell">
      <header className="cpl-topbar">
        <div className="cpl-brand">
          <span className="cpl-brand-app">{t('navigation.portalName')}</span>
          <span className="cpl-brand-company">{companyName ?? user?.firstName ?? ''}</span>
        </div>
        <button type="button" className="cpl-logout" onClick={() => void logout()}>
          {t('navigation.logout')}
        </button>
      </header>
      <nav className="cpl-nav" aria-label={t('navigation.navLabel')}>
        {NAV_ITEMS.filter((item) => !item.permission || hasPermission(item.permission)).map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/klantportaal'}
            className={({ isActive }) => (isActive ? 'cpl-nav-active' : undefined)}
          >
            {t(item.labelKey)}
            {item.badgeKey === 'messages' && unreadMessages > 0 && (
              <span className="cpl-nav-badge">{unreadMessages}</span>
            )}
            {item.badgeKey === 'notices' && unreadNotices > 0 && (
              <span className="cpl-nav-badge">{unreadNotices}</span>
            )}
          </NavLink>
        ))}
      </nav>
      <main className="cpl-content">
        {/* C-03: portal users never pass through AppLayout, so the shared bootstrap runs here
            too — a stop window is an appointment at the CARRIER's dock, and it must not render
            on the seeded default zone for a tenant that runs on another one. */}
        <DisplayPreferencesProvider>
          <Outlet />
        </DisplayPreferencesProvider>
      </main>
      <footer className="cpl-footer">
        <LanguageSwitcher />
      </footer>
    </div>
  )
}
