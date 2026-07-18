import { NavLink } from 'react-router-dom'
import './Sidebar.css'

interface NavItem {
  label: string
  to: string
}

const navItems: NavItem[] = [
  { label: 'Dashboard', to: '/dashboard' },
  { label: 'Transportopdrachten', to: '/transport-orders' },
  { label: 'Klanten', to: '/customers' },
  { label: 'Chauffeurs', to: '/drivers' },
  { label: 'Voertuigen', to: '/vehicles' },
]

const masterDataNavItems: NavItem[] = [
  { label: 'Gebruikers', to: '/users' },
  { label: 'Rollen en rechten', to: '/roles' },
  { label: 'Personeel', to: '/employees' },
]

function renderNavItems(items: NavItem[]) {
  return items.map((item) => (
    <li key={item.to}>
      <NavLink to={item.to} className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
        {item.label}
      </NavLink>
    </li>
  ))
}

export function Sidebar() {
  return (
    <aside className="sidebar">
      <h1 className="app-title">Transportation Service</h1>
      <nav>
        <ul>{renderNavItems(navItems)}</ul>
        <div className="nav-group-label">Master Data</div>
        <ul>{renderNavItems(masterDataNavItems)}</ul>
        <ul>
          <li>
            <NavLink to="/settings" className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}>
              Instellingen
            </NavLink>
          </li>
        </ul>
      </nav>
    </aside>
  )
}
