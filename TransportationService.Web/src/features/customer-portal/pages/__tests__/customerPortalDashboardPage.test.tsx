import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { CustomerPortalDashboardPage } from '../CustomerPortalDashboardPage'
import type { PortalDashboard } from '../../api/customerPortalApi'

vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    user: { firstName: 'Kaat' },
    hasPermission: (code: string) => code === 'customer_portal.messages' || code === 'customer_portal.view_invoices',
  }),
}))

const dashboard = vi.hoisted(() => ({
  value: {
    activeOrders: 2,
    upcomingDeliveries: [{ orderId: 'o1', orderNumber: 'ORD-1', plannedAt: '2026-08-01T10:00:00Z', city: 'Gent' }],
    problemOrders: 1,
    unreadMessages: 4,
    recentInvoices: [{ id: 'i1', invoiceNumber: '2026070001', invoiceDate: '2026-07-20', status: 'Sent', total: 121.0 }],
    announcements: [{ id: 'a1', title: 'Onderhoud gepland', body: 'Vannacht onderhoud.', activeFrom: null, activeUntil: null, isActive: true }],
  } satisfies PortalDashboard,
}))

vi.mock('../../api/customerPortalApi', () => ({
  getPortalDashboard: () => Promise.resolve(dashboard.value),
}))

describe('CustomerPortalDashboardPage', () => {
  it('renders the summary cards, upcoming delivery, recent invoice and announcement', async () => {
    render(
      <MemoryRouter>
        <CustomerPortalDashboardPage />
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('Welkom, Kaat')).toBeInTheDocument())
    expect(screen.getByText('Onderhoud gepland')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument() // activeOrders card
    expect(screen.getByText('4')).toBeInTheDocument() // unreadMessages card
    expect(screen.getByText(/ORD-1/)).toBeInTheDocument()
    expect(screen.getByText(/2026070001/)).toBeInTheDocument()
  })
})
