import { useEffect, useState } from 'react'
import { NavLink } from 'react-router-dom'
import { LOOKUP_GROUP_LABELS, LOOKUP_RESOURCES, type LookupGroup } from '../../features/master-data/lookupRegistry'
import { useAuth } from '../../features/auth/authContextValue'
import { getUnreadCount } from '../../features/notifications/api/notificationsApi'
import '../../features/notifications/pages/notifications.css'
import './Sidebar.css'

interface NavItem {
  label: string
  to: string
  /** Any-of permissions required to see this entry; omitted = visible to every user. */
  permissions?: string[]
}

const operationsNavItems: NavItem[] = [
  { label: 'Dashboard', to: '/dashboard', permissions: ['dashboard.view'] },
  { label: 'Transportopdrachten', to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
  { label: 'Planning', to: '/planning', permissions: ['planning.view'] },
  { label: 'Afwijkingen', to: '/exceptions', permissions: ['exceptions.view'] },
  { label: 'Mijn ritten', to: '/my-trips', permissions: ['driver_workflow.view'] },
  { label: 'Klanten', to: '/customers', permissions: ['customers.view'] },
  { label: 'Chauffeurs', to: '/drivers', permissions: ['drivers.view'] },
  { label: 'Afwezigheden', to: '/absences', permissions: ['absences.view'] },
  { label: 'Vloot', to: '/fleet', permissions: ['vehicles.view'] },
  { label: 'Voertuigen', to: '/vehicles', permissions: ['vehicles.view'] },
  { label: 'Opleggers', to: '/trailers', permissions: ['trailers.view'] },
  { label: 'Tankkaarten', to: '/tank-cards', permissions: ['tank_cards.view'] },
  { label: 'Locaties', to: '/locations', permissions: ['locations.view'] },
  { label: 'Facturen', to: '/invoices', permissions: ['invoices.view'] },
]

const administrationNavItems: NavItem[] = [
  { label: 'Gebruikers', to: '/users', permissions: ['users.view'] },
  { label: 'Rollen en rechten', to: '/roles', permissions: ['roles.view'] },
  { label: 'Personeel', to: '/employees', permissions: ['employees.view'] },
  { label: 'Kwalificaties', to: '/qualifications', permissions: ['employee_documents.view'] },
]

const lookupGroupOrder: LookupGroup[] = ['organisatie', 'categorieen', 'referentie']

function renderNavItems(items: NavItem[]) {
  return items.map((item) => (
    <li key={item.to}>
      <NavLink to={item.to} className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
        {item.label}
      </NavLink>
    </li>
  ))
}

function initials(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase() || '?'
}

const UNREAD_POLL_MS = 60_000

export function Sidebar() {
  const { user, logout, hasAnyPermission } = useAuth()
  const [unreadCount, setUnreadCount] = useState(0)

  // The sidebar only shows what the user may actually open; the backend enforces the
  // same permissions on every endpoint (UI filtering is UX, never security).
  const visible = (items: NavItem[]) =>
    items.filter((item) => !item.permissions || hasAnyPermission(item.permissions))

  const visibleOperations = visible(operationsNavItems)
  const visibleAdministration = visible(administrationNavItems)
  const visibleLookupsByGroup = lookupGroupOrder
    .map((group) => ({
      group,
      items: LOOKUP_RESOURCES.filter(
        (resource) => resource.group === group && hasAnyPermission([resource.viewPermission, resource.managePermission]),
      ),
    }))
    .filter(({ items }) => items.length > 0)

  // Light poll so the badge stays roughly current without a push channel.
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
    <aside className="sidebar">
      <h1 className="app-title">Transportation Service</h1>
      <nav>
        <ul>
          {renderNavItems(visibleOperations)}
          <li>
            <NavLink to="/notifications" className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
              Meldingen
              {unreadCount > 0 && <span className="nav-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>}
            </NavLink>
          </li>
        </ul>

        {visibleAdministration.length > 0 && (
          <>
            <div className="nav-group-label">Beheer</div>
            <ul>{renderNavItems(visibleAdministration)}</ul>
          </>
        )}

        {visibleLookupsByGroup.length > 0 && <div className="nav-group-label">Stamgegevens</div>}
        {visibleLookupsByGroup.map(({ group, items }) => (
          <div key={group} className="nav-subgroup">
            <div className="nav-subgroup-label">{LOOKUP_GROUP_LABELS[group]}</div>
            <ul>
              {renderNavItems(
                items.map((resource) => ({
                  label: resource.title,
                  to: `/master-data/${resource.slug}`,
                })),
              )}
            </ul>
          </div>
        ))}

        {hasAnyPermission(['company_settings.view', 'company_settings.manage']) && (
          <ul className="nav-footer">
            <li>
              <NavLink to="/settings" className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
                Instellingen
              </NavLink>
            </li>
          </ul>
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
