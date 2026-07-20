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
  { label: "KPI's", to: '/kpi', permissions: ['kpi.view'] },
  { label: 'Transportopdrachten', to: '/transport-orders', permissions: ['orders.view', 'orders.manage'] },
  { label: 'Planning', to: '/planning', permissions: ['planning.view'] },
  { label: 'Afwijkingen', to: '/exceptions', permissions: ['exceptions.view'] },
  { label: 'Magazijn', to: '/warehouse', permissions: ['warehouse.view'] },
  { label: 'Mijn ritten', to: '/my-trips', permissions: ['driver_workflow.view'] },
  { label: 'Klanten', to: '/customers', permissions: ['customers.view'] },
  { label: 'Klantportaal', to: '/customer-portal', permissions: ['customer_portal.view'] },
  { label: 'Chauffeurs', to: '/drivers', permissions: ['drivers.view'] },
  { label: 'Personeelsplanning', to: '/employee-planning', permissions: ['employee_planning.view', 'employee_planning.manage'] },
  { label: 'Afwezigheden', to: '/absences', permissions: ['absences.view'] },
  { label: 'Vloot', to: '/fleet', permissions: ['vehicles.view'] },
  { label: 'Voertuigen', to: '/vehicles', permissions: ['vehicles.view'] },
  { label: 'Opleggers', to: '/trailers', permissions: ['trailers.view'] },
  { label: 'Tankkaarten', to: '/tank-cards', permissions: ['tank_cards.view'] },
  { label: 'Onderhoudsbeleid', to: '/maintenance-policies', permissions: ['maintenance_policies.view', 'maintenance_policies.manage'] },
  { label: 'Locaties', to: '/locations', permissions: ['locations.view'] },
  { label: 'Facturen', to: '/invoices', permissions: ['invoices.view'] },
  { label: 'Kostentarieven', to: '/cost-rates', permissions: ['trip_costs.view', 'trip_costs.manage'] },
  { label: 'Berichten (e-mail/SMS)', to: '/messaging', permissions: ['messaging.manage'] },
  { label: 'EDI', to: '/edi', permissions: ['edi.manage'] },
  { label: 'Integraties', to: '/integrations', permissions: ['integrations.manage'] },
]

const administrationNavItems: NavItem[] = [
  { label: 'Gebruikers', to: '/users', permissions: ['users.view'] },
  { label: 'Rollen en rechten', to: '/roles', permissions: ['roles.view'] },
  { label: 'Functie→rol-koppelingen', to: '/job-function-mappings', permissions: ['roles.view', 'roles.manage_permissions'] },
  { label: 'Personeel', to: '/employees', permissions: ['employees.view'] },
  { label: 'Kwalificaties', to: '/qualifications', permissions: ['employee_documents.view'] },
]

const lookupGroupOrder: LookupGroup[] = ['organisatie', 'categorieen', 'referentie']

function renderNavItems(items: NavItem[], onNavigate?: () => void) {
  return items.map((item) => (
    <li key={item.to}>
      <NavLink
        to={item.to}
        className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
        onClick={onNavigate}
      >
        {item.label}
      </NavLink>
    </li>
  ))
}

function initials(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase() || '?'
}

const UNREAD_POLL_MS = 60_000

const portalNavItems: NavItem[] = [
  { label: 'Mijn dashboard', to: '/portal' },
  { label: 'Mijn planning', to: '/portal/planning' },
  { label: 'Mijn afwezigheden', to: '/portal/absences' },
  { label: 'Mijn kwalificaties', to: '/portal/qualifications' },
  { label: 'Mijn profiel', to: '/portal/profile' },
]

export function Sidebar({ open = false, onNavigate }: { open?: boolean; onNavigate?: () => void }) {
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
    <aside className={open ? 'sidebar sidebar-open' : 'sidebar'}>
      <h1 className="app-title">Transportation Service</h1>
      <nav>
        {/* The self-service portal is available to every user with an employee link. */}
        {user?.employeeId && (
          <>
            <div className="nav-group-label">Mijn portaal</div>
            <ul>{renderNavItems(portalNavItems, onNavigate)}</ul>
          </>
        )}

        {(visibleOperations.length > 0 || user?.employeeId) && <div className="nav-group-label">Operaties</div>}
        <ul>
          {renderNavItems(visibleOperations, onNavigate)}
          <li>
            <NavLink
              to="/notifications"
              className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
              onClick={onNavigate}
            >
              Meldingen
              {unreadCount > 0 && <span className="nav-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>}
            </NavLink>
          </li>
          <li>
            <NavLink
              to="/inbox"
              className={({ isActive }) => (isActive ? 'nav-item active' : 'nav-item')}
              onClick={onNavigate}
            >
              Berichten
            </NavLink>
          </li>
        </ul>

        {visibleAdministration.length > 0 && (
          <>
            <div className="nav-group-label">Beheer</div>
            <ul>{renderNavItems(visibleAdministration, onNavigate)}</ul>
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
                onNavigate,
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
