import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import appRoutesSource from '../AppRoutes.tsx?raw'
import { getNavModules, type NavItem } from '../../components/layout/nav/navConfig'
import { LOOKUP_RESOURCES } from '../../features/master-data/lookupRegistry'
import { RequirePermission } from '../RequirePermission'
import { appRoutePermissions } from '../appRoutePermissions'
import { fleetRoutePermissions } from '../fleetRoutePermissions'
import { navPermissions } from '../routePermissions'
import { transportRoutePermissions } from '../transportRoutePermissions'

// SEC-M5: direct URLs for pages a role may not open (e.g. /operations as Boekhouding, Chauffeur,
// HR or Wagenparkbeheerder) answer "Geen toegang" instead of endless "Laden…", repeated error
// toasts with the raw server text and an "Opnieuw proberen" that can never succeed.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    user: { employeeId: null },
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const GUARDS = appRoutePermissions()

/** Relevant slices of the seeded role templates (Data/DefaultRoleDefinitions.cs). */
const ROLES: Record<string, string[]> = {
  chauffeur: ['driver_workflow.view', 'driver_workflow.execute', 'scanning.view', 'scanning.execute', 'exceptions.create', 'pod.finalize', 'attendance.self'],
  boekhouding: ['dashboard.view', 'invoices.view', 'orders.view', 'pod.view', 'customers.view', 'locations.view', 'company_settings.view', 'trip_costs.view', 'profitability.view', 'kpi.view', 'package_reports.export', 'dossiers.view', 'incidents.view', 'problems.approve_charge', 'tariffs.view', 'tariffs.manage', 'reports.view', 'attendance.self', 'vehicles.view', 'trailers.view'],
  hr: ['dashboard.view', 'employees.view', 'employee_documents.view', 'hr_settings.manage', 'leave_types.manage', 'attendance.self', 'attendance.view', 'attendance.manage_settings', 'departments.view', 'departments.manage', 'reports.view'],
  wagenparkbeheerder: ['dashboard.view', 'vehicles.view', 'trailers.view', 'vehicle_categories.manage', 'trailer_categories.manage', 'maintenance_policies.view', 'tank_cards.view', 'kpi.view'],
  magazijn: ['warehouse.view', 'warehouse.manage', 'warehouse.schedule', 'packages.view', 'scanning.view', 'scanning.execute', 'exceptions.view', 'planning.view', 'operations.view', 'attendance.self'],
  dispatcher: ['dashboard.view', 'customers.view', 'orders.view', 'planning.view', 'exceptions.view', 'operations.view', 'operations.manage_alerts', 'dossiers.view'],
}

function allowed(role: string, route: string) {
  const permissions = GUARDS[route]
  if (!permissions) throw new Error(`no guard for ${route}`)
  return permissions.some((p) => ROLES[role].includes(p))
}

describe('app route guard table', () => {
  it('mirrors the API permissions of each page', () => {
    expect(GUARDS['/operations']).toEqual(['operations.view'])
    expect(GUARDS['/dashboard']).toEqual(['dashboard.view'])
    expect(GUARDS['/warehouse/trace']).toEqual(['warehouse.view', 'warehouse.manage'])
    expect(GUARDS['/warehouses']).toEqual(['warehouse.view', 'warehouse.manage'])
    expect(GUARDS['/dock-planning']).toEqual(['warehouse.view', 'warehouse.schedule'])
    expect(GUARDS['/pods/:id']).toEqual(['pod.view'])
    expect(GUARDS['/packages/:id']).toEqual(['packages.view', 'packages.manage'])
    expect(GUARDS['/my-trips']).toEqual(['driver_workflow.view'])
    expect(GUARDS['/driver']).toEqual(['driver_workflow.view'])
    expect(GUARDS['/kpi/trips']).toEqual(['profitability.view'])
    expect(GUARDS['/locations/new']).toEqual(['locations.create'])
    // Read-only modes and second tabs the page's API also serves stay reachable.
    expect(GUARDS['/settings/pricing']).toEqual(['tariffs.manage', 'tariffs.view'])
    expect(GUARDS['/settings/system']).toEqual(['system_info.view', 'backups.view'])
    expect(GUARDS['/settings/hr-reminders']).toEqual(['hr_settings.manage', 'employees.view'])
    expect(GUARDS['/settings/leave']).toEqual(['leave_types.manage', 'employees.view'])
    expect(GUARDS['/settings/task-templates']).toEqual(['tasks.manage_templates', 'tasks.manage_recurring', 'tasks.assign'])
    expect(GUARDS['/settings/activity-types']).toEqual(['activity_types.view', 'activity_types.manage', 'dossiers.view'])
  })

  it('locks every role without operations.view out of /operations', () => {
    for (const role of ['boekhouding', 'chauffeur', 'hr', 'wagenparkbeheerder']) {
      expect(allowed(role, '/operations'), role).toBe(false)
    }
    expect(allowed('dispatcher', '/operations')).toBe(true)
    expect(allowed('magazijn', '/operations')).toBe(true)
  })

  it('keeps each role on the pages it works with', () => {
    expect(allowed('chauffeur', '/my-trips')).toBe(true)
    expect(allowed('chauffeur', '/my-trips/:id')).toBe(true)
    expect(allowed('chauffeur', '/driver')).toBe(true)
    expect(allowed('boekhouding', '/pods/:id')).toBe(true)
    expect(allowed('boekhouding', '/profitability')).toBe(true)
    expect(allowed('boekhouding', '/kpi/trips')).toBe(true)
    expect(allowed('boekhouding', '/settings/pricing')).toBe(true)
    expect(allowed('boekhouding', '/settings/charge-policies')).toBe(true)
    expect(allowed('hr', '/settings/hr-reminders')).toBe(true)
    expect(allowed('hr', '/settings/leave')).toBe(true)
    expect(allowed('hr', '/qualifications')).toBe(true)
    expect(allowed('magazijn', '/warehouse')).toBe(true)
    expect(allowed('magazijn', '/warehouse/trace')).toBe(true)
    expect(allowed('magazijn', '/packages/:id')).toBe(true)
    expect(allowed('wagenparkbeheerder', '/kpi')).toBe(true)
  })

  it('keeps a scanning-only driver off the warehouse pages (SEC-H1)', () => {
    for (const route of ['/warehouse', '/warehouse/trace', '/warehouses', '/dock-planning', '/packages/:id']) {
      expect(allowed('chauffeur', route), route).toBe(false)
    }
  })
})

/** Every guard table AppRoutes reads, by the identifier it uses. */
const TABLES: Record<string, Record<string, string[]>> = {
  APP_GUARDS: GUARDS,
  TRANSPORT_GUARDS: transportRoutePermissions(),
  FLEET_GUARDS: fleetRoutePermissions(),
}

/** Nav entries whose route deliberately has no route-level guard, and why. */
const SELF_CHECKED: Record<string, string> = {
  '/order-imports': 'checks orders.create|manage itself and shows the upload only then',
  '/edi': 'checks edi.view|manage per tab',
  '/issued-items': 'index redirect; IssuedItemsAdminPage checks per tab',
  '/portal/time': 'self-service portal (own clock, api/me/attendance)',
  '/driver': 'guarded on the driver-shell layout route (driver_workflow.view); the menu entry is narrower on purpose',
}

/** The any-of permissions guarding the AppRoutes line for `path`, or null when unguarded. */
function guardFor(path: string): string[] | null {
  const line = appRoutesSource.split('\n').find((l) => l.includes(`<Route path="${path}" `))
  if (!line) throw new Error(`AppRoutes has no <Route path="${path}">`)
  if (!line.includes('<RequirePermission')) return null
  const table = /permissions=\{(\w+)\['([^']+)'\]\}/.exec(line)
  if (table) return TABLES[table[1]][table[2]]
  const nav = /permissions=\{navPermissions\('([^']+)'\)\}/.exec(line)
  if (nav) return navPermissions(nav[1])
  const literal = /permissions=\{\[([^\]]+)\]\}/.exec(line)
  if (literal) return literal[1].split(',').map((p) => p.trim().replace(/'/g, ''))
  throw new Error(`unrecognised guard on ${path}: ${line.trim()}`)
}

function navEntriesWithPermissions(): NavItem[] {
  const entries: NavItem[] = []
  const visit = (items: NavItem[]) => {
    for (const item of items) {
      if (item.permissions) entries.push(item)
      if (item.children) visit(item.children)
    }
  }
  for (const module of getNavModules()) {
    visit(module.items ?? [])
    for (const subgroup of module.subgroups ?? []) visit(subgroup.items)
  }
  return entries
}

describe('every sidebar entry with permissions has a guarded route', () => {
  const lookupPaths = new Set(LOOKUP_RESOURCES.map((r) => `/master-data/${r.slug}`))

  it.each(navEntriesWithPermissions().map((item) => [item.to, item.permissions!] as const))(
    '%s',
    (path, menuPermissions) => {
      if (SELF_CHECKED[path]) return
      // Lookups share one route; LookupPage guards with the registry's view|manage pair.
      const guard = lookupPaths.has(path)
        ? (() => {
            const r = LOOKUP_RESOURCES.find((x) => `/master-data/${x.slug}` === path)!
            return [r.viewPermission, r.managePermission]
          })()
        : guardFor(path)
      expect(guard, `${path} has no route guard`).not.toBeNull()
      // Whoever sees the menu entry may open the page: the guard covers all its rights.
      for (const permission of menuPermissions) {
        expect(guard, `${path} guard misses ${permission}`).toContain(permission)
      }
    },
  )

  it('uses every entry of the app guard table on a route', () => {
    for (const key of Object.keys(GUARDS)) {
      expect(appRoutesSource, key).toContain(`APP_GUARDS['${key}']`)
    }
  })
})

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/operations" element={<RequirePermission permissions={GUARDS['/operations']}><div>Live operaties</div></RequirePermission>} />
        <Route path="/dashboard" element={<RequirePermission permissions={GUARDS['/dashboard']}><div>Dashboard</div></RequirePermission>} />
        <Route path="/settings/system" element={<RequirePermission permissions={GUARDS['/settings/system']}><div>Systeeminfo</div></RequirePermission>} />
        <Route path="/my-trips" element={<RequirePermission permissions={GUARDS['/my-trips']}><div>Mijn ritten</div></RequirePermission>} />
        <Route path="/warehouse/trace" element={<RequirePermission permissions={GUARDS['/warehouse/trace']}><div>Tracering</div></RequirePermission>} />
      </Routes>
    </MemoryRouter>,
  )
}

function expectAccessDenied() {
  expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Naar het startscherm' })).toHaveAttribute('href', '/')
  expect(screen.queryByRole('button', { name: 'Opnieuw proberen' })).not.toBeInTheDocument()
  expect(screen.queryByText(/Laden/)).not.toBeInTheDocument()
}

describe('guarded app routes', () => {
  it.each(['boekhouding', 'chauffeur', 'hr', 'wagenparkbeheerder'])('shows "Geen toegang" on /operations to %s', (role) => {
    auth.permissions = ROLES[role]
    renderAt('/operations')
    expectAccessDenied()
    expect(screen.queryByText('Live operaties')).not.toBeInTheDocument()
  })

  it('renders /operations for a dispatcher', () => {
    auth.permissions = ROLES.dispatcher
    renderAt('/operations')
    expect(screen.getByText('Live operaties')).toBeInTheDocument()
  })

  it('shows a chauffeur "Geen toegang" on the dashboard', () => {
    auth.permissions = ROLES.chauffeur
    renderAt('/dashboard')
    expectAccessDenied()
  })

  it('shows a scanning-only chauffeur "Geen toegang" on warehouse tracing', () => {
    auth.permissions = ROLES.chauffeur
    renderAt('/warehouse/trace')
    expectAccessDenied()
  })

  it('shows HR "Geen toegang" on system info', () => {
    auth.permissions = ROLES.hr
    renderAt('/settings/system')
    expectAccessDenied()
  })

  it('shows Boekhouding "Geen toegang" on /my-trips', () => {
    auth.permissions = ROLES.boekhouding
    renderAt('/my-trips')
    expectAccessDenied()
  })

  it('renders /my-trips for a chauffeur', () => {
    auth.permissions = ROLES.chauffeur
    renderAt('/my-trips')
    expect(screen.getByText('Mijn ritten')).toBeInTheDocument()
  })
})
