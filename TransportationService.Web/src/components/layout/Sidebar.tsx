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
  { label: 'Instellingen', to: '/settings' },
]

export function Sidebar() {
  return (
    <aside className="sidebar">
      <h1 className="app-title">Transportation Service</h1>
      <nav>
        <ul>
          {navItems.map((item) => (
            <li key={item.to}>
              <NavLink
                to={item.to}
                className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
              >
                {item.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
    </aside>
  )
}
