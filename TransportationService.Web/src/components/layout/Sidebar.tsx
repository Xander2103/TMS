import { useMemo, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useAuth } from '../../features/auth/authContextValue'
import { useLocale } from '../../i18n/localeContext'
import { AppLanguageSwitcher } from './AppLanguageSwitcher'
import { LegalEntitySwitcher } from '../../features/legal-entities/components/LegalEntitySwitcher'
import { useUnreadNotifications } from '../../features/notifications/notificationsContextValue'
import { getNavModules } from './nav/navConfig'
import { NavFilter } from './nav/NavFilter'
import { NavModule } from './nav/NavModule'
import { filterModule, findActiveModuleId, type VisibleModule } from './nav/navState'
import { useExpandedModules } from './nav/useExpandedModules'
import './Sidebar.css'
import './nav.css'

function initials(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase() || '?'
}

export function Sidebar({ open = false, onNavigate }: { open?: boolean; onNavigate?: () => void }) {
  const { user, logout, hasAnyPermission } = useAuth()
  const { t } = useLocale()
  const location = useLocation()
  // Polling van de ongelezen-teller is gecentraliseerd in de NotificationsProvider (AppLayout).
  const { unreadCount } = useUnreadNotifications()
  const [query, setQuery] = useState('')

  const modules = useMemo(() => getNavModules(), [])
  const activeModuleId = findActiveModuleId(modules, location.pathname)
  const { isExpanded, toggle } = useExpandedModules(user?.id ?? null, activeModuleId)

  // The sidebar only shows what the user may open; the backend enforces the same
  // permissions on every endpoint (UI filtering is UX, never security).
  const visibleModules = useMemo<VisibleModule[]>(
    () =>
      modules
        .map((m) => filterModule(m, { hasAnyPermission, hasEmployee: !!user?.employeeId, query, translate: t }))
        .filter((vm): vm is VisibleModule => vm !== null),
    [modules, hasAnyPermission, user?.employeeId, query, t],
  )

  const filtering = query.trim().length > 0

  return (
    <aside className={open ? 'sidebar sidebar-open' : 'sidebar'}>
      <h1 className="app-title">Transportation Service</h1>
      <LegalEntitySwitcher />
      <NavFilter value={query} onChange={setQuery} />
      <nav aria-label={t('ui.nav.mainNavigation')}>
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
          <p className="nav-empty">{t('ui.nav.noMenuItems', { query: query.trim() })}</p>
        )}
      </nav>

      <AppLanguageSwitcher />

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
            aria-label={t('navigation.logout')}
            title={t('navigation.logout')}
          >
            ⎋
          </button>
        </div>
      )}
    </aside>
  )
}
