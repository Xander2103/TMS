import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router-dom'
import { RequirePermission } from '../RequirePermission'
import { transportRoutePermissions } from '../transportRoutePermissions'

// ACC-M8: the finance/settings pages and the customer-portal shell follow the route-level guard
// instead of an in-page "Je hebt geen rechten…" string, a raw API "Missing permission" text or a
// portal shell full of load errors.

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../features/auth/authContextValue', () => ({
  useAuth: () => ({
    user: { employeeId: null },
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

const GUARDS = transportRoutePermissions()

describe('finance and portal route guard table', () => {
  it('mirrors the API permissions of each page', () => {
    expect(GUARDS['/settings/legal-entities']).toEqual(['legal_entities.view', 'legal_entities.manage'])
    expect(GUARDS['/settings/accounting']).toEqual(['accounting.view', 'accounting.manage'])
    expect(GUARDS['/peppol']).toEqual(['peppol.view'])
    expect(GUARDS['/klantportaal']).toEqual(['customer_portal.view'])
    expect(GUARDS['/klantportaal/facturen']).toEqual(['customer_portal.view_invoices'])
    expect(GUARDS['/klantportaal/documenten']).toEqual(['customer_portal.view_documents'])
    expect(GUARDS['/klantportaal/berichten']).toEqual(['customer_portal.messages'])
    expect(GUARDS['/klantportaal/gebruikers']).toEqual(['customer_portal.manage_users'])
  })
})

function renderAt(path: string) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/settings/legal-entities" element={<RequirePermission permissions={GUARDS['/settings/legal-entities']}><div>Eigen bedrijven</div></RequirePermission>} />
        <Route path="/peppol" element={<RequirePermission permissions={GUARDS['/peppol']}><div>Peppol-pagina</div></RequirePermission>} />
        {/* The same nesting as AppRoutes: guard → portal shell (layout) → pages with their own right. */}
        <Route element={<RequirePermission permissions={GUARDS['/klantportaal']} />}>
          <Route element={<div data-testid="portal-shell"><Outlet /></div>}>
            <Route path="/klantportaal" element={<div>Portaalopdrachten</div>} />
            <Route path="/klantportaal/facturen" element={<RequirePermission permissions={GUARDS['/klantportaal/facturen']}><div>Portaalfacturen</div></RequirePermission>} />
          </Route>
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

function expectAccessDenied() {
  expect(screen.getByRole('heading', { name: 'Geen toegang' })).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Naar het startscherm' })).toHaveAttribute('href', '/')
}

describe('guarded finance and portal routes', () => {
  it('shows "Geen toegang" to a chauffeur on Eigen bedrijven — never a raw "Missing permission" text', () => {
    auth.permissions = ['driver_workflow.view']
    renderAt('/settings/legal-entities')
    expectAccessDenied()
    expect(screen.queryByText('Eigen bedrijven')).not.toBeInTheDocument()
  })

  it('opens Eigen bedrijven for legal_entities.view and Peppol for peppol.view', () => {
    auth.permissions = ['legal_entities.view']
    renderAt('/settings/legal-entities')
    expect(screen.getByText('Eigen bedrijven')).toBeInTheDocument()
    auth.permissions = ['peppol.view']
    renderAt('/peppol')
    expect(screen.getByText('Peppol-pagina')).toBeInTheDocument()
  })

  it('refuses the Peppol page to a planner', () => {
    auth.permissions = ['planning.view', 'dossiers.view']
    renderAt('/peppol')
    expectAccessDenied()
  })

  it('shows an internal user (Boekhouding) "Geen toegang" on the customer portal instead of the portal shell', () => {
    auth.permissions = ['invoices.view', 'invoices.create']
    renderAt('/klantportaal/facturen')
    expectAccessDenied()
    expect(screen.queryByTestId('portal-shell')).not.toBeInTheDocument()
  })

  it('lets a portal user in, and guards the invoices page on its own right inside the shell', () => {
    auth.permissions = ['customer_portal.view']
    renderAt('/klantportaal/facturen')
    expect(screen.getByTestId('portal-shell')).toBeInTheDocument()
    expectAccessDenied()
    auth.permissions = ['customer_portal.view', 'customer_portal.view_invoices']
    renderAt('/klantportaal/facturen')
    expect(screen.getByText('Portaalfacturen')).toBeInTheDocument()
  })
})
