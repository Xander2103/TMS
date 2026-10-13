import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { InternalOnly, RootRedirect } from '../portalRouting'

const auth = vi.hoisted(() => ({
  customerId: null as string | null,
  permissions: [] as string[],
}))

vi.mock('../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    status: 'authenticated' as const,
    user: {
      id: 'u1', tenantId: 't1', tenantName: 'Acme', email: 'x@acme.be', firstName: 'A', lastName: 'B',
      employeeId: null, roles: [], permissions: auth.permissions, mustChangePassword: false,
      customerId: auth.customerId,
    },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

describe('RootRedirect', () => {
  it('sends a portal user (customerId + customer_portal.view) to /klantportaal', () => {
    auth.customerId = 'cust-1'
    auth.permissions = ['customer_portal.view']

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/klantportaal" element={<div>Klantportaal shell</div>} />
          <Route path="/transport-orders" element={<div>Interne app</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Klantportaal shell')).toBeInTheDocument()
  })

  it('sends an internal user with dossiers.view to /dossiers (Wave 1 landing)', () => {
    auth.customerId = null
    auth.permissions = ['orders.view', 'dossiers.view']

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/klantportaal" element={<div>Klantportaal shell</div>} />
          <Route path="/dossiers" element={<div>Dossierlijst</div>} />
          <Route path="/transport-orders" element={<div>Interne app</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Dossierlijst')).toBeInTheDocument()
  })

  it('sends an internal user WITHOUT dossiers.view to /transport-orders (fallback)', () => {
    auth.customerId = null
    auth.permissions = ['orders.view']

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/klantportaal" element={<div>Klantportaal shell</div>} />
          <Route path="/dossiers" element={<div>Dossierlijst</div>} />
          <Route path="/transport-orders" element={<div>Interne app</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Interne app')).toBeInTheDocument()
  })

  it('a customer-linked user WITHOUT customer_portal.view is treated as internal', () => {
    auth.customerId = 'cust-1'
    auth.permissions = [] // no customer_portal.view

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/klantportaal" element={<div>Klantportaal shell</div>} />
          <Route path="/transport-orders" element={<div>Opdrachtenlijst</div>} />
          <Route path="/inbox" element={<div>Interne app</div>} />
        </Routes>
      </MemoryRouter>,
    )

    // No permissions at all: the first ungated sidebar entry, never a page that answers 403.
    expect(screen.getByText('Interne app')).toBeInTheDocument()
    expect(screen.queryByText('Opdrachtenlijst')).not.toBeInTheDocument()
  })

  it('sends a role without dossiers.view and orders.view (HR) to its first permitted sidebar entry', () => {
    // Regression guard: HR landed on /transport-orders after every login and got a 403 there.
    auth.customerId = null
    auth.permissions = ['dashboard.view', 'employees.view', 'issued_items.manage_templates']

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/dashboard" element={<div>Dashboard</div>} />
          <Route path="/transport-orders" element={<div>Opdrachtenlijst</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Dashboard')).toBeInTheDocument()
    expect(screen.queryByText('Opdrachtenlijst')).not.toBeInTheDocument()
  })

  it('skips sidebar entries the role may not open when picking the landing page', () => {
    auth.customerId = null
    auth.permissions = ['employees.view'] // no dashboard.view → /inbox is the first ungated entry

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/dashboard" element={<div>Dashboard</div>} />
          <Route path="/inbox" element={<div>Berichten</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Berichten')).toBeInTheDocument()
  })
})

describe('InternalOnly', () => {
  it('bounces a portal user away from the internal shell back to /klantportaal', () => {
    auth.customerId = 'cust-1'
    auth.permissions = ['customer_portal.view']

    render(
      <MemoryRouter initialEntries={['/transport-orders']}>
        <Routes>
          <Route element={<InternalOnly />}>
            <Route path="/transport-orders" element={<div>Interne app</div>} />
          </Route>
          <Route path="/klantportaal" element={<div>Klantportaal shell</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Klantportaal shell')).toBeInTheDocument()
    expect(screen.queryByText('Interne app')).not.toBeInTheDocument()
  })

  it('renders the internal shell normally for internal staff', () => {
    auth.customerId = null
    auth.permissions = ['orders.view']

    render(
      <MemoryRouter initialEntries={['/transport-orders']}>
        <Routes>
          <Route element={<InternalOnly />}>
            <Route path="/transport-orders" element={<div>Interne app</div>} />
          </Route>
          <Route path="/klantportaal" element={<div>Klantportaal shell</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText('Interne app')).toBeInTheDocument()
  })
})
