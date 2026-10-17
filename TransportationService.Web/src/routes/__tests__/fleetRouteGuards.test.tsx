import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { RequirePermission } from '../RequirePermission'
import { fleetRoutePermissions } from '../fleetRoutePermissions'

// FL-M10: fleet pages answer "Geen toegang" for roles without fleet rights instead of rendering a
// page shell that then fails with "kon niet worden geladen".

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    user: { employeeId: null },
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const GUARDS = fleetRoutePermissions()

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        {Object.keys(GUARDS).map((route) => (
          <Route
            key={route}
            path={route}
            element={<RequirePermission permissions={GUARDS[route]}><div>Pagina {route}</div></RequirePermission>}
          />
        ))}
      </Routes>
    </MemoryRouter>,
  )
}

describe('fleet route guards', () => {
  it('mirror the API permissions of each fleet page', () => {
    expect(GUARDS['/vehicles']).toEqual(['vehicles.view'])
    expect(GUARDS['/vehicles/new']).toEqual(['vehicles.create'])
    expect(GUARDS['/trailers/new']).toEqual(['trailers.create'])
    expect(GUARDS['/tank-cards']).toEqual(['tank_cards.view'])
    expect(GUARDS['/maintenance-policies']).toEqual(['maintenance_policies.view', 'maintenance_policies.manage'])
  })

  it.each(['/fleet', '/vehicles', '/vehicles/abc', '/vehicles/new', '/trailers', '/trailers/new', '/tank-cards', '/maintenance-policies'])(
    'shows "Geen toegang" to a chauffeur on %s',
    (path) => {
      auth.permissions = ['driver_workflow.view']
      renderAt(path)
      expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
      expect(screen.queryByText(/^Pagina/)).not.toBeInTheDocument()
    },
  )

  it('a planner reads the vehicle list', () => {
    auth.permissions = ['vehicles.view', 'trailers.view']
    renderAt('/vehicles')
    expect(screen.getByText('Pagina /vehicles')).toBeInTheDocument()
  })

  it('a planner does not get the new-vehicle form', () => {
    auth.permissions = ['vehicles.view']
    renderAt('/vehicles/new')
    expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
  })
})
