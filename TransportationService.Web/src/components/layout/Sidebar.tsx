import { useEffect, useMemo, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useAuth } from '../../features/auth/authContextValue'
import { LegalEntitySwitcher } from '../../features/legal-entities/components/LegalEntitySwitcher'
import { getUnreadCount } from '../../features/notifications/api/notificationsApi'
import { getNavModules } from './nav/navConfig'
import { NavFilter } from './nav/NavFilter'
import { NavModule } from './nav/NavModule'
import { filterModule, findActiveModuleId, type VisibleModule } from './nav/navState'
import { useExpandedModules } from './nav/useExpandedModules'
import '../../features/notifications/pages/notifications.css'
import './Sidebar.css'
import './nav.css'

function initials(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase() || '?'
}

const UNREAD_POLL_MS = 60_000

export function Sidebar({ open = false, onNavigate }: { open?: boolean; onNavigate?: () => void }) {
  const { user, logout, hasAnyPermission } = useAuth()
  const location = useLocation()
  const [unreadCount, setUnreadCount] = useState(0)
  const [query, setQuery] = useState('')

  const modules = useMemo(() => getNavModules(), [])
  const activeModuleId = findActiveModuleId(modules, location.pathname)
  const { isExpanded, toggle } = useExpandedModules(user?.id ?? null, activeModuleId)

  // The sidebar only shows what the user may open; the backend enforces the same
  // permissions on every endpoint (UI filtering is UX, never security).
  const visibleModules = useMemo<VisibleModule[]>(
    () =>
      modules
        .map((m) => filterModule(m, { hasAnyPermission, hasEmployee: !!user?.employeeId, query }))
        .filter((vm): vm is VisibleModule => vm !== null),
    [modules, hasAnyPermission, user?.employeeId, query],
  )

  const filtering = query.trim().length > 0

  // Light poll so the notification badge stays roughly current without a push channel.
  useEffect(() => {
    let mounted = true
    const load = () => {
      getUnreadCount()
        .then((data) => {
          if (mounted) setUnreadCount(data.count)
        })
        .catch(() => {})
    }
    load()
    const timer = window.setInterval(load, UNREAD_POLL_MS)
    return () => {
      mounted = false
      window.clearInterval(timer)
    }
  }, [])

  return (
    <aside className={open ? 'sidebar sidebar-open' : 'sidebar'}>
      <h1 className="app-title">Transportation Service</h1>
      <LegalEntitySwitcher />
      <NavFilter value={query} onChange={setQuery} />
      <nav aria-label="Hoofdnavigatie">
        <ul className="nav-modules">
          {visibleModules.map((vm) => (
            <NavModule
              key={vm.module.id}
              vm={vm}
              expanded={filtering || isExpanded(vm.module.id)}
              active={vm.module.id === activeModuleId}
              unreadCount={unreadCount}
              onToggle={toggle}
              onNavigate={onNavigate}
            />
          ))}
        </ul>
        {filtering && visibleModules.length === 0 && (
          <p className="nav-empty">Geen menu-items voor “{query.trim()}”.</p>
        )}
      </nav>

      {user && (
        <div className="sidebar-user">
          <div className="sidebar-user-avatar" aria-hidden="true">
            {initials(user.firstName, user.lastName)}
          </div>
          <div className="sidebar-user-info">
            <span className="sidebar-user-name" title={`${user.firstName} ${user.lastName}`}>
              {user.firstName} {user.lastName}
            </span>
            <span className="sidebar-user-tenant" title={user.tenantName}>
              {user.tenantName}
            </span>
          </div>
          <button
            type="button"
            className="sidebar-logout"
            onClick={() => void logout()}
            aria-label="Uitloggen"
            title="Uitloggen"
          >
            ⎋
          </button>
        </div>
      )}
    </aside>
  )
}
