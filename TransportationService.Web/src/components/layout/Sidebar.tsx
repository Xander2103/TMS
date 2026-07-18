import { NavLink } from 'react-router-dom'
import { LOOKUP_GROUP_LABELS, LOOKUP_RESOURCES, type LookupGroup } from '../../features/master-data/lookupRegistry'
import { useAuth } from '../../features/auth/AuthContext'
import './Sidebar.css'

interface NavItem {
  label: string
  to: string
}

const operationsNavItems: NavItem[] = [
  { label: 'Dashboard', to: '/dashboard' },
  { label: 'Transportopdrachten', to: '/transport-orders' },
  { label: 'Klanten', to: '/customers' },
  { label: 'Chauffeurs', to: '/drivers' },
  { label: 'Voertuigen', to: '/vehicles' },
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

export function Sidebar() {
  const { user, logout } = useAuth()

  return (
    <aside className="sidebar">
      <h1 className="app-title">Transportation Service</h1>
      <nav>
        <ul>{renderNavItems(operationsNavItems)}</ul>

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
