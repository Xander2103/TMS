import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PortalMessagesPage } from '../pages/PortalMessagesPage'
import type { PortalMessageAdminItem, PortalMessageDeliveryStatus } from '../api/portalMessagesApi'

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const auth = vi.hoisted(() => ({ permissions: ['portal_messages.view', 'portal_messages.send'] }))
vi.mock('../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({
      items: [
        { id: 'c1', customerNumber: 'K001', name: 'Haven BV', city: null, countryCode: null, categoryName: null, isActive: true, isBlocked: false },
        { id: 'c2', customerNumber: 'K002', name: 'Trans NV', city: null, countryCode: null, categoryName: null, isActive: true, isBlocked: false },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 500,
    }),
}))

const messages = vi.hoisted(() => ({ value: [] as PortalMessageAdminItem[] }))
const deliveryStatus = vi.hoisted(() => ({ value: null as PortalMessageDeliveryStatus | null }))
const sendSpy = vi.hoisted(() => vi.fn())
const cancelSpy = vi.hoisted(() => vi.fn())

vi.mock('../api/portalMessagesApi', () => ({
  listPortalMessagesAdmin: () => Promise.resolve(messages.value),
  sendPortalMessage: sendSpy,
  getPortalMessageDeliveryStatus: () =>
    deliveryStatus.value ? Promise.resolve(deliveryStatus.value) : Promise.reject(new Error('geen')),
  cancelPortalMessage: cancelSpy,
}))

function adminMessage(overrides: Partial<PortalMessageAdminItem>): PortalMessageAdminItem {
  return {
    id: 'pm1',
    titleNl: 'Titel NL',
    titleFr: null,
    titleEn: null,
    bodyNl: 'Body NL',
    bodyFr: null,
    bodyEn: null,
    priority: 'Normal',
    displayMode: 'Notification',
    requiresAcknowledgement: false,
    visibleFrom: null,
    expiresAt: null,
    relatedEntityType: null,
    relatedEntityId: null,
    emailRequested: false,
    cancelledAt: null,
    createdAt: '2026-08-01T08:00:00',
    customerNames: ['Haven BV'],
    ...overrides,
  }
}

describe('PortalMessagesPage', () => {
  beforeEach(() => {
    auth.permissions = ['portal_messages.view', 'portal_messages.send']
    messages.value = []
    deliveryStatus.value = null
    sendSpy.mockReset().mockResolvedValue(adminMessage({}))
    cancelSpy.mockReset().mockResolvedValue(undefined)
    toast.showSuccess.mockReset()
    toast.showError.mockReset()
  })

  it('lists messages with customers, display mode and status', async () => {
    messages.value = [
      adminMessage({ id: 'pm1', titleNl: 'Onderhoudsvenster', displayMode: 'DashboardBanner' }),
      adminMessage({ id: 'pm2', titleNl: 'Ingetrokken bericht', cancelledAt: '2026-08-01T10:00:00' }),
    ]
    render(<PortalMessagesPage />)

    expect(await screen.findByText('Onderhoudsvenster')).toBeInTheDocument()
    expect(screen.getAllByText('Haven BV').length).toBeGreaterThan(0)
    expect(screen.getByText('Dashboardbanner')).toBeInTheDocument()
    expect(screen.getByText('Ingetrokken')).toBeInTheDocument()
  })

  it('sends the correct payload with NL and FR content and a selected customer', async () => {
    const user = userEvent.setup()
    render(<PortalMessagesPage />)

    await user.click(await screen.findByRole('button', { name: 'Nieuw bericht' }))
    await screen.findByText('Haven BV')

    await user.type(screen.getByLabelText(/^Titel \(NL\)/), 'Nieuwe openingsuren')
    await user.type(screen.getByLabelText(/^Inhoud \(NL\)/), 'Vanaf maandag om 7u.')
    await user.type(screen.getByLabelText(/^Titel \(FR\)/), 'Nouveaux horaires')
    await user.type(screen.getByLabelText(/^Inhoud \(FR\)/), 'A partir de lundi.')
    await user.click(screen.getByLabelText('Haven BV'))
    await user.selectOptions(screen.getByLabelText(/^Weergavemodus/), 'DashboardBanner')
    await user.click(screen.getByRole('button', { name: 'Verzenden' }))

    await waitFor(() =>
      expect(sendSpy).toHaveBeenCalledWith({
        titleNl: 'Nieuwe openingsuren',
        bodyNl: 'Vanaf maandag om 7u.',
        titleFr: 'Nouveaux horaires',
        bodyFr: 'A partir de lundi.',
        titleEn: null,
        bodyEn: null,
        customerIds: ['c1'],
        priority: 'Normal',
        displayMode: 'DashboardBanner',
        requiresAcknowledgement: false,
        visibleFrom: null,
        expiresAt: null,
        sendEmail: false,
      }),
    )
    expect(toast.showSuccess).toHaveBeenCalledWith('Portaalbericht verzonden.')
  })

  it('warns when multiple customers are selected', async () => {
    const user = userEvent.setup()
    render(<PortalMessagesPage />)

    await user.click(await screen.findByRole('button', { name: 'Nieuw bericht' }))
    await user.click(await screen.findByLabelText('Haven BV'))
    await user.click(screen.getByLabelText('Trans NV'))

    expect(screen.getByText(/Let op: u verstuurt naar meerdere klanten/)).toBeInTheDocument()
  })

  it('renders the delivery-status modal with per-user rows', async () => {
    messages.value = [adminMessage({ id: 'pm1', titleNl: 'Onderhoudsvenster' })]
    deliveryStatus.value = {
      messageId: 'pm1',
      titleNl: 'Onderhoudsvenster',
      createdAt: '2026-08-01T08:00:00',
      cancelledAt: null,
      requiresAcknowledgement: true,
      recipients: [
        {
          userId: 'u1',
          name: 'Kaat Klant',
          customerName: 'Haven BV',
          readAt: '2026-08-01T09:00:00',
          acknowledgedAt: null,
          emailStatus: 'Sent',
          emailFailureReason: null,
        },
      ],
    }
    const user = userEvent.setup()
    render(<PortalMessagesPage />)

    await user.click(await screen.findByRole('button', { name: 'Bezorgstatus' }))

    expect(await screen.findByText('Kaat Klant')).toBeInTheDocument()
    expect(screen.getAllByText('Haven BV').length).toBeGreaterThan(0)
    expect(screen.getByText('Sent')).toBeInTheDocument()
  })
})
