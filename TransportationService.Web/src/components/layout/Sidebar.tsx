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
}

const operationsNavItems: NavItem[] = [
  { label: 'Dashboard', to: '/dashboard' },
  { label: 'Transportopdrachten', to: '/transport-orders' },
  { label: 'Planning', to: '/planning' },
  { label: 'Mijn ritten', to: '/my-trips' },
  { label: 'Klanten', to: '/customers' },
  { label: 'Chauffeurs', to: '/drivers' },
  { label: 'Afwezigheden', to: '/absences' },
  { label: 'Vloot', to: '/fleet' },
  { label: 'Voertuigen', to: '/vehicles' },
  { label: 'Opleggers', to: '/trailers' },
  { label: 'Tankkaarten', to: '/tank-cards' },
  { label: 'Locaties', to: '/locations' },
  { label: 'Facturen', to: '/invoices' },
]

const administrationNavItems: NavItem[] = [
  { label: 'Gebruikers', to: '/users' },
  { label: 'Rollen en rechten', to: '/roles' },
  { label: 'Personeel', to: '/employees' },
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
  const { user, logout } = useAuth()
  const [unreadCount, setUnreadCount] = useState(0)

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
          {renderNavItems(operationsNavItems)}
          <li>
            <NavLink to="/notifications" className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
              Meldingen
              {unreadCount > 0 && <span className="nav-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>}
            </NavLink>
          </li>
        </ul>

        <div className="nav-group-label">Beheer</div>
        <ul>{renderNavItems(administrationNavItems)}</ul>

        <div className="nav-group-label">Stamgegevens</div>
        {lookupGroupOrder.map((group) => (
          <div key={group} className="nav-subgroup">
            <div className="nav-subgroup-label">{LOOKUP_GROUP_LABELS[group]}</div>
            <ul>
              {renderNavItems(
                LOOKUP_RESOURCES.filter((resource) => resource.group === group).map((resource) => ({
                  label: resource.title,
                  to: `/master-data/${resource.slug}`,
                })),
              )}
            </ul>
          </div>
        ))}

        <ul className="nav-footer">
          <li>
            <NavLink to="/settings" className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
              Instellingen
            </NavLink>
          </li>
        </ul>
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
