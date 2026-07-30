import { useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../../auth/authContextValue'
import { getPortalContext, getPortalMessagesUnreadCount } from '../api/customerPortalApi'
import './customer-portal-layout.css'

interface NavItem {
  label: string
  to: string
  permission?: string
  badgeKey?: 'messages'
}

const NAV_ITEMS: NavItem[] = [
  { label: 'Dashboard', to: '/klantportaal/dashboard' },
  { label: 'Opdrachten', to: '/klantportaal', permission: 'customer_portal.view' },
  { label: 'Documenten', to: '/klantportaal/documenten', permission: 'customer_portal.view_documents' },
  { label: 'Facturen', to: '/klantportaal/facturen', permission: 'customer_portal.view_invoices' },
  { label: 'Berichten', to: '/klantportaal/berichten', permission: 'customer_portal.messages', badgeKey: 'messages' },
  { label: 'Gebruikers', to: '/klantportaal/gebruikers', permission: 'customer_portal.manage_users' },
]

const UNREAD_POLL_MS = 60_000

/**
 * Dedicated shell for customer-portal users — the mirror of DriverLayout for the mobile driver
 * app. Portal users NEVER see the internal AppLayout sidebar; every /klantportaal/* route renders
 * exclusively inside this shell (see AppRoutes and the post-login redirect in RequireAuth/root).
 */
export function CustomerPortalLayout() {
  const { user, logout, hasPermission } = useAuth()
  const [companyName, setCompanyName] = useState<string | null>(null)
  const [unreadMessages, setUnreadMessages] = useState(0)
  const canSeeMessages = hasPermission('customer_portal.messages')

  useEffect(() => {
    let mounted = true
    getPortalContext()
      .then((context) => {
        if (mounted) setCompanyName(context.customerName)
      })
      .catch(() => {
        // Non-fatal: the top bar simply falls back to no company name.
      })
    return () => {
      mounted = false
    }
  }, [])

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

  return (
    <div className="cpl-shell">
      <header className="cpl-topbar">
        <div className="cpl-brand">
          <span className="cpl-brand-app">Klantportaal</span>
          <span className="cpl-brand-company">{companyName ?? user?.firstName ?? ''}</span>
        </div>
        <button type="button" className="cpl-logout" onClick={() => void logout()}>
          Uitloggen
        </button>
      </header>
      <nav className="cpl-nav" aria-label="Klantportaalnavigatie">
        {NAV_ITEMS.filter((item) => !item.permission || hasPermission(item.permission)).map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/klantportaal'}
            className={({ isActive }) => (isActive ? 'cpl-nav-active' : undefined)}
          >
            {item.label}
            {item.badgeKey === 'messages' && unreadMessages > 0 && (
              <span className="cpl-nav-badge">{unreadMessages}</span>
            )}
          </NavLink>
        ))}
      </nav>
      <main className="cpl-content">
        <Outlet />
      </main>
    </div>
  )
}
