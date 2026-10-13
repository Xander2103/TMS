import { beforeEach, describe, expect, it, vi } from 'vitest'
import { resetDisplayPreferences } from '../../../../components/layout/displayPreferences'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { CustomerPortalLayout } from '../CustomerPortalLayout'

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))

// The shared display-preferences bootstrap keys its cache on the signed-in session, so it reads
// AuthContext from this module — keep the real exports alongside the stubbed useAuth.
vi.mock('../../../auth/authContextValue', async (actual) => ({
  ...(await actual<typeof import('../../../auth/authContextValue')>()),
  useAuth: () => ({
    status: 'authenticated' as const,
    user: {
      id: 'u1', tenantId: 't1', tenantName: 'Acme', email: 'x@haven.be', firstName: 'Kaat', lastName: 'Klant',
      employeeId: null, roles: [], permissions: auth.permissions, mustChangePassword: false, customerId: 'cust-1',
    },
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

// C-03: the portal shell now gates its routed content on the shared regional bootstrap, so the
// display-preferences call has to answer before the Outlet renders.
vi.mock('../../../../api/apiClient', () => ({
  apiClient: {
    getJson: () => Promise.resolve({
      dateFormat: 'dd/MM/yyyy', decimalSeparator: ',', timezone: 'Europe/Amsterdam',
    }),
  },
}))

const unreadCount = vi.hoisted(() => ({ value: 0 }))

const unreadNotices = vi.hoisted(() => ({ value: 0 }))

vi.mock('../../api/customerPortalApi', () => ({
  getPortalContext: () => Promise.resolve({ customerId: 'cust-1', customerName: 'Haven BV' }),
  getPortalMessagesUnreadCount: () => Promise.resolve({ count: unreadCount.value }),
  getPortalFeedUnreadCount: () => Promise.resolve({ count: unreadNotices.value }),
}))

function renderLayout() {
  return render(
    <MemoryRouter initialEntries={['/klantportaal']}>
      <Routes>
        <Route element={<CustomerPortalLayout />}>
          <Route path="/klantportaal" element={<div>Opdrachten-inhoud</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('CustomerPortalLayout', () => {
  beforeEach(() => {
    resetDisplayPreferences()
    unreadCount.value = 0
    unreadNotices.value = 0
  })

  it('always shows Dashboard and Opdrachten, and hides every optional nav item without its permission', async () => {
    auth.permissions = ['customer_portal.view']
    renderLayout()

    await waitFor(() => expect(screen.getByText('Haven BV')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Opdrachten' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Documenten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Facturen' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Berichten' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Gebruikers' })).not.toBeInTheDocument()
    expect(await screen.findByText('Opdrachten-inhoud')).toBeInTheDocument()
  })

  it('shows Gebruikers only when customer_portal.manage_users is granted', async () => {
    auth.permissions = ['customer_portal.view', 'customer_portal.manage_users', 'customer_portal.messages']
    renderLayout()

    await waitFor(() => expect(screen.getByText('Haven BV')).toBeInTheDocument())
    expect(screen.getByRole('link', { name: 'Gebruikers' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Berichten' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Documenten' })).not.toBeInTheDocument()
  })

  it('shows an unread-count badge on Berichten when there are unread messages', async () => {
    auth.permissions = ['customer_portal.view', 'customer_portal.messages']
    unreadCount.value = 3
    renderLayout()

    await waitFor(() => expect(screen.getByText('Haven BV')).toBeInTheDocument())
    const berichtenLink = await screen.findByRole('link', { name: /Berichten/ })
    expect(berichtenLink).toHaveTextContent('3')
  })
})
