import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { RequirePermission } from '../RequirePermission'
import { EmployeeRecordGuard } from '../EmployeeRecordGuard'
import { navPermissions } from '../routePermissions'

// Item 14/15/21: one route-level guard renders "Geen toegang" instead of a page shell full of
// 403 load errors; the permissions come from the sidebar config (single source of truth).

const auth = vi.hoisted(() => ({ permissions: [] as string[], employeeId: null as string | null }))

vi.mock('../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    user: { employeeId: auth.employeeId },
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/users" element={<RequirePermission permissions={navPermissions('/users')}><div>Gebruikerslijst</div></RequirePermission>} />
        <Route element={<RequirePermission permissions={['roles.view']} />}>
          <Route path="/roles" element={<div>Rollenlijst</div>} />
        </Route>
        <Route path="/employees/new" element={<RequirePermission permissions={['employees.create']}><form aria-label="Nieuwe medewerker" /></RequirePermission>} />
        <Route path="/employees/:id" element={<EmployeeRecordGuard><div>Medewerkerdossier</div></EmployeeRecordGuard>} />
      </Routes>
    </MemoryRouter>,
  )
}

function expectAccessDenied() {
  expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
  expect(screen.getByText(/Je hebt geen rechten voor deze pagina/)).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Naar het startscherm' })).toHaveAttribute('href', '/')
}

describe('navPermissions', () => {
  it('returns the permissions of the matching sidebar entry', () => {
    expect(navPermissions('/users')).toEqual(['users.view'])
    expect(navPermissions('/inventory')).toEqual(['inventory.view', 'inventory.manage'])
    expect(navPermissions('/employee-planning')).toEqual(['employee_planning.view', 'employee_planning.manage'])
  })

  it('throws for a path without a guarded nav entry, so a renamed entry cannot unguard a route', () => {
    expect(() => navPermissions('/does-not-exist')).toThrow()
  })
})

describe('RequirePermission', () => {
  it('renders the access-denied state without the permission', () => {
    auth.permissions = ['planning.view']
    renderAt('/users')
    expectAccessDenied()
    expect(screen.queryByText('Gebruikerslijst')).not.toBeInTheDocument()
  })

  it('renders the page with the permission', () => {
    auth.permissions = ['users.view']
    renderAt('/users')
    expect(screen.getByText('Gebruikerslijst')).toBeInTheDocument()
    expect(screen.queryByText('Geen toegang')).not.toBeInTheDocument()
  })

  it('works as a layout route (Outlet) too', () => {
    auth.permissions = []
    renderAt('/roles')
    expectAccessDenied()

    auth.permissions = ['roles.view']
    renderAt('/roles')
    expect(screen.getByText('Rollenlijst')).toBeInTheDocument()
  })
})

describe('/employees/new gating', () => {
  it('shows no create form to a planner with employees.view but without employees.create', () => {
    auth.permissions = ['employees.view', 'planning.view']
    renderAt('/employees/new')
    expectAccessDenied()
    expect(screen.queryByRole('form', { name: 'Nieuwe medewerker' })).not.toBeInTheDocument()
  })

  it('shows the create form with employees.create', () => {
    auth.permissions = ['employees.view', 'employees.create']
    renderAt('/employees/new')
    expect(screen.getByRole('form', { name: 'Nieuwe medewerker' })).toBeInTheDocument()
  })
})

describe('EmployeeRecordGuard', () => {
  it('offers a chauffeur opening their own record the way to their portal profile', () => {
    auth.permissions = ['attendance.self']
    auth.employeeId = 'emp-self'
    renderAt('/employees/emp-self')
    expectAccessDenied()
    expect(screen.getByRole('link', { name: 'Naar mijn profiel' })).toHaveAttribute('href', '/portal/profile')
    expect(screen.queryByText('Medewerkerdossier')).not.toBeInTheDocument()
  })

  it('does not offer the profile link on somebody else\'s record', () => {
    auth.permissions = []
    auth.employeeId = 'emp-self'
    renderAt('/employees/emp-other')
    expectAccessDenied()
    expect(screen.queryByRole('link', { name: 'Naar mijn profiel' })).not.toBeInTheDocument()
  })

  it('renders the record with employees.view', () => {
    auth.permissions = ['employees.view']
    renderAt('/employees/emp-other')
    expect(screen.getByText('Medewerkerdossier')).toBeInTheDocument()
  })
})
