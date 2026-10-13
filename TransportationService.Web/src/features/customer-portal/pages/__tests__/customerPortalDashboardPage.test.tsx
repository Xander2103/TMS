import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { CustomerPortalDashboardPage } from '../CustomerPortalDashboardPage'
import type { PortalDashboard, PortalFeedMessage } from '../../api/customerPortalApi'

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

const feed = vi.hoisted(() => ({ value: [] as PortalFeedMessage[] }))
const acknowledgeSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/customerPortalApi', () => ({
  getPortalDashboard: () => Promise.resolve(dashboard.value),
  listPortalFeedMessages: () => Promise.resolve(feed.value),
  acknowledgePortalFeedMessage: acknowledgeSpy,
}))

function feedMessage(overrides: Partial<PortalFeedMessage>): PortalFeedMessage {
  return {
    id: 'm1',
    title: 'Bericht',
    body: 'Inhoud',
    language: 'nl',
    priority: 'Normal',
    displayMode: 'Notification',
    requiresAcknowledgement: false,
    relatedEntityType: null,
    relatedEntityId: null,
    publishedAt: '2026-08-01T08:00:00Z',
    expiresAt: null,
    readAt: null,
    acknowledgedAt: null,
    ...overrides,
  }
}

describe('CustomerPortalDashboardPage', () => {
  beforeEach(() => {
    feed.value = []
    acknowledgeSpy.mockReset().mockResolvedValue(undefined)
  })

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

  it('shows DashboardBanner feed messages as banners at the top', async () => {
    feed.value = [feedMessage({ id: 'b1', title: 'Nieuwe openingsuren', body: 'Vanaf maandag.', displayMode: 'DashboardBanner' })]
    render(
      <MemoryRouter>
        <CustomerPortalDashboardPage />
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('Nieuwe openingsuren')).toBeInTheDocument())
    expect(screen.getByText('Vanaf maandag.')).toBeInTheDocument()
  })

  it('shows a blocking overlay for an unacknowledged BlockingAcknowledgement message and removes it after confirming', async () => {
    const user = userEvent.setup()
    feed.value = [
      feedMessage({
        id: 'blk1',
        title: 'Nieuwe voorwaarden',
        body: 'Gelieve te bevestigen.',
        displayMode: 'BlockingAcknowledgement',
        requiresAcknowledgement: true,
      }),
    ]
    render(
      <MemoryRouter>
        <CustomerPortalDashboardPage />
      </MemoryRouter>,
    )

    const overlay = await screen.findByRole('alertdialog', { name: 'Nieuwe voorwaarden' })
    expect(overlay).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Ik bevestig' }))

    await waitFor(() => expect(acknowledgeSpy).toHaveBeenCalledWith('blk1'))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
  })

  it('does not show a blocking overlay for an already acknowledged message', async () => {
    feed.value = [
      feedMessage({
        id: 'blk2',
        displayMode: 'BlockingAcknowledgement',
        requiresAcknowledgement: true,
        acknowledgedAt: '2026-08-01T09:00:00Z',
      }),
    ]
    render(
      <MemoryRouter>
        <CustomerPortalDashboardPage />
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('Welkom, Kaat')).toBeInTheDocument())
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })
})
