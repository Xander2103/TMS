import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { RequirePermission } from '../RequirePermission'
import { transportRoutePermissions } from '../transportRoutePermissions'

// M14: the transport routes render "Geen toegang" for roles without the rights instead of page
// shells full of "kon niet worden geladen · Opnieuw proberen", using the real guard table.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    user: { employeeId: null },
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const GUARDS = transportRoutePermissions()

/** Transport-relevant slices of the seeded role templates (Data/DefaultRoleDefinitions.cs). */
const ROLES: Record<string, string[]> = {
  chauffeur: ['driver_workflow.view', 'driver_workflow.execute', 'scanning.view', 'scanning.execute', 'exceptions.create', 'pod.finalize', 'attendance.self'],
  boekhouding: ['dashboard.view', 'invoices.view', 'invoices.create', 'invoices.edit', 'orders.view', 'orders.export', 'customers.view', 'customers.edit', 'dossiers.view', 'incidents.view'],
  dispatcher: ['dashboard.view', 'customers.view', 'orders.view', 'orders.change_status', 'planning.view', 'exceptions.view', 'dossiers.view', 'incidents.view', 'incidents.manage'],
  planner: ['customers.view', 'orders.view', 'orders.create', 'planning.view', 'exceptions.view', 'dossiers.view', 'dossiers.manage', 'incidents.view', 'incidents.manage'],
}

function allowed(role: string, route: string) {
  const permissions = GUARDS[route]
  if (!permissions) throw new Error(`no guard for ${route}`)
  return permissions.some((p) => ROLES[role].includes(p))
}

describe('transport route guard table', () => {
  it('mirrors the API permissions of each page', () => {
    expect(GUARDS['/dossiers']).toEqual(['dossiers.view', 'dossiers.manage'])
    expect(GUARDS['/dossiers/:id']).toEqual(['dossiers.view', 'dossiers.manage'])
    expect(GUARDS['/dossiers/new']).toEqual(['dossiers.manage'])
    expect(GUARDS['/transport-orders/:id']).toEqual(['orders.view', 'orders.manage'])
    expect(GUARDS['/transport-orders/new']).toEqual(['orders.create', 'orders.manage'])
    expect(GUARDS['/planning']).toEqual(['planning.view'])
    expect(GUARDS['/planning/:id']).toEqual(['planning.view'])
    expect(GUARDS['/incidents']).toEqual(['incidents.view', 'incidents.manage'])
    expect(GUARDS['/incidents/new']).toEqual(['incidents.manage'])
    expect(GUARDS['/exceptions']).toEqual(['exceptions.view'])
    expect(GUARDS['/invoices']).toEqual(['invoices.view'])
    expect(GUARDS['/invoices/:id']).toEqual(['invoices.view'])
    expect(GUARDS['/invoices/new']).toEqual(['invoices.create'])
    expect(GUARDS['/invoice-control']).toEqual(['invoices.view'])
    expect(GUARDS['/customers']).toEqual(['customers.view'])
    expect(GUARDS['/customers/new']).toEqual(['customers.create'])
  })

  it('locks a chauffeur out of every transport route', () => {
    for (const route of ['/dossiers', '/dossiers/:id', '/transport-orders/:id', '/planning', '/planning/:id', '/invoices', '/incidents', '/customers']) {
      expect(allowed('chauffeur', route), route).toBe(false)
    }
  })

  it('keeps Boekhouding on dossiers, orders and invoices read-only, but not on planning or the dossier wizard', () => {
    expect(allowed('boekhouding', '/dossiers')).toBe(true)
    expect(allowed('boekhouding', '/dossiers/:id')).toBe(true)
    expect(allowed('boekhouding', '/transport-orders/:id')).toBe(true)
    expect(allowed('boekhouding', '/invoices/new')).toBe(true)
    expect(allowed('boekhouding', '/planning')).toBe(false)
    expect(allowed('boekhouding', '/planning/:id')).toBe(false)
    expect(allowed('boekhouding', '/dossiers/new')).toBe(false)
    expect(allowed('boekhouding', '/transport-orders/new')).toBe(false)
  })

  it('lets a dispatcher open dossiers read-only and plan, without the create wizards', () => {
    expect(allowed('dispatcher', '/dossiers/:id')).toBe(true)
    expect(allowed('dispatcher', '/planning/:id')).toBe(true)
    expect(allowed('dispatcher', '/incidents/new')).toBe(true)
    expect(allowed('dispatcher', '/dossiers/new')).toBe(false)
    expect(allowed('dispatcher', '/invoices')).toBe(false)
  })

  it('lets a planner create dossiers and orders', () => {
    expect(allowed('planner', '/dossiers/new')).toBe(true)
    expect(allowed('planner', '/transport-orders/new')).toBe(true)
  })
})

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/dossiers/new" element={<RequirePermission permissions={GUARDS['/dossiers/new']}><form aria-label="Nieuw dossier" /></RequirePermission>} />
        <Route path="/dossiers/:id/:section?" element={<RequirePermission permissions={GUARDS['/dossiers/:id']}><div>Dossierwerkruimte</div></RequirePermission>} />
        <Route path="/planning/:id" element={<RequirePermission permissions={GUARDS['/planning/:id']}><div>Ritdetail</div></RequirePermission>} />
      </Routes>
    </MemoryRouter>,
  )
}

function expectAccessDenied() {
  expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Naar het startscherm' })).toHaveAttribute('href', '/')
}

describe('guarded transport routes', () => {
  it('shows "Geen toegang" to a chauffeur on a dossier', () => {
    auth.permissions = ROLES.chauffeur
    renderAt('/dossiers/d-1')
    expectAccessDenied()
    expect(screen.queryByText('Dossierwerkruimte')).not.toBeInTheDocument()
  })

  it('shows Boekhouding no dossier wizard and no trip detail, but the dossier itself', () => {
    auth.permissions = ROLES.boekhouding
    renderAt('/dossiers/new')
    expectAccessDenied()
    expect(screen.queryByRole('form', { name: 'Nieuw dossier' })).not.toBeInTheDocument()
  })

  it('shows Boekhouding "Geen toegang" on a trip', () => {
    auth.permissions = ROLES.boekhouding
    renderAt('/planning/t-1')
    expectAccessDenied()
  })

  it('renders the dossier for a read-only dispatcher', () => {
    auth.permissions = ROLES.dispatcher
    renderAt('/dossiers/d-1/route')
    expect(screen.getByText('Dossierwerkruimte')).toBeInTheDocument()
  })
})
