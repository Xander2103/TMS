import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { InboxPage } from '../InboxPage'
import type { InboxMessage, MessageDeliveryStatus } from '../../api/inboxApi'

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const auth = vi.hoisted(() => ({ permissions: [] as string[] }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({
    hasPermission: (code: string) => auth.permissions.includes(code),
    hasAnyPermission: (codes: string[]) => codes.some((c) => auth.permissions.includes(c)),
  }),
}))

vi.mock('../../../roles/api/rolesApi', () => ({
  getRoles: () => Promise.resolve([{ id: 'r1', name: 'Planner', isActive: true }]),
}))

const inboxData = vi.hoisted(() => ({ value: [] as InboxMessage[] }))
const sentData = vi.hoisted(() => ({ value: [] as InboxMessage[] }))
const deliveryStatus = vi.hoisted(() => ({ value: null as MessageDeliveryStatus | null }))
const sendSpy = vi.hoisted(() => vi.fn())
const acknowledgeSpy = vi.hoisted(() => vi.fn())
const cancelSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/inboxApi', () => ({
  listInboxMessages: () => Promise.resolve(inboxData.value),
  listSentMessages: () => Promise.resolve(sentData.value),
  listMessageRecipients: () => Promise.resolve([{ userId: 'u1', name: 'Jan Peeters' }]),
  listDepartmentOptions: () => Promise.resolve([{ id: 'd1', code: 'PLN', name: 'Planning' }]),
  sendInternalMessage: sendSpy,
  markMessageRead: () => Promise.resolve(),
  acknowledgeMessage: acknowledgeSpy,
  getMessageDeliveryStatus: () => (deliveryStatus.value ? Promise.resolve(deliveryStatus.value) : Promise.reject(new Error('geen'))),
  cancelInternalMessage: cancelSpy,
}))

function message(overrides: Partial<InboxMessage>): InboxMessage {
  return {
    id: 'm1',
    subject: 'Onderwerp',
    body: 'Inhoud',
    senderName: 'HR',
    sentAt: '2026-08-01T09:00:00',
    readAt: '2026-08-01T09:05:00',
    recipientCount: 1,
    priority: 'Normal',
    requiresAcknowledgement: false,
    acknowledgedAt: null,
    visibleFrom: null,
    expiresAt: null,
    cancelledAt: null,
    ...overrides,
  }
}

describe('InboxPage', () => {
  beforeEach(() => {
    auth.permissions = ['messages.send']
    inboxData.value = []
    sentData.value = []
    deliveryStatus.value = null
    sendSpy.mockReset().mockResolvedValue({ recipients: 3 })
    acknowledgeSpy.mockReset().mockResolvedValue(undefined)
    cancelSpy.mockReset().mockResolvedValue(undefined)
    toast.showSuccess.mockReset()
    toast.showError.mockReset()
  })

  it('hides bulk targeting options without messages.send_bulk', async () => {
    const user = userEvent.setup()
    render(<InboxPage />)

    await user.click(await screen.findByRole('button', { name: 'Nieuw bericht' }))
    await screen.findByText('Jan Peeters')

    expect(screen.queryByLabelText('Doelgroep')).not.toBeInTheDocument()
    expect(screen.getByText('Aan (personen)')).toBeInTheDocument()
  })

  it('sends the correct payload for department targeting', async () => {
    auth.permissions = ['messages.send', 'messages.send_bulk']
    const user = userEvent.setup()
    render(<InboxPage />)

    await user.click(await screen.findByRole('button', { name: 'Nieuw bericht' }))
    await user.selectOptions(await screen.findByLabelText('Doelgroep'), 'department')
    await user.selectOptions(await screen.findByLabelText(/^Afdeling/), 'd1')
    await user.type(screen.getByLabelText(/^Onderwerp/), 'Planning update')
    await user.type(screen.getByLabelText(/^Bericht/), 'Nieuwe roosters staan klaar.')
    await user.click(screen.getByRole('button', { name: 'Verzenden' }))

    await waitFor(() =>
      expect(sendSpy).toHaveBeenCalledWith({
        subject: 'Planning update',
        body: 'Nieuwe roosters staan klaar.',
        userIds: [],
        roleId: null,
        departmentId: 'd1',
        allEmployees: false,
        priority: 'Normal',
        requiresAcknowledgement: false,
        visibleFrom: null,
        expiresAt: null,
        sendEmail: false,
      }),
    )
  })

  it('shows a Bevestigen button for unacknowledged messages and calls acknowledge', async () => {
    inboxData.value = [
      message({ id: 'ack1', subject: 'Veiligheidsinstructie', requiresAcknowledgement: true, acknowledgedAt: null }),
    ]
    const user = userEvent.setup()
    render(<InboxPage />)

    await user.click(await screen.findByText('Veiligheidsinstructie'))
    await user.click(await screen.findByRole('button', { name: 'Bevestigen' }))

    await waitFor(() => expect(acknowledgeSpy).toHaveBeenCalledWith('ack1'))
    expect(await screen.findByText(/Bevestigd op/)).toBeInTheDocument()
  })

  it('renders the delivery-status modal with recipient rows', async () => {
    sentData.value = [message({ id: 's1', subject: 'Verzonden bericht', recipientCount: 2 })]
    deliveryStatus.value = {
      messageId: 's1',
      subject: 'Verzonden bericht',
      sentAt: '2026-08-01T09:00:00',
      cancelledAt: null,
      requiresAcknowledgement: true,
      recipients: [
        {
          userId: 'u1',
          name: 'Jan Peeters',
          readAt: '2026-08-01T10:00:00',
          acknowledgedAt: null,
          emailStatus: 'Failed',
          emailFailureReason: 'Mailbox vol',
        },
      ],
    }
    const user = userEvent.setup()
    render(<InboxPage />)

    await user.click(await screen.findByRole('tab', { name: 'Verzonden' }))
    await user.click(await screen.findByRole('button', { name: 'Bezorgstatus' }))

    expect(await screen.findByText('Jan Peeters')).toBeInTheDocument()
    expect(screen.getByText(/Mislukt/)).toBeInTheDocument()
    expect(screen.getByText(/Mailbox vol/)).toBeInTheDocument()
  })

  it('cancels a sent message after confirmation', async () => {
    sentData.value = [message({ id: 's2', subject: 'Terug te trekken', recipientCount: 4 })]
    const user = userEvent.setup()
    render(<InboxPage />)

    await user.click(await screen.findByRole('tab', { name: 'Verzonden' }))
    await user.click(await screen.findByRole('button', { name: 'Intrekken' }))
    const dialog = await screen.findByRole('dialog', { name: 'Bericht intrekken' })
    await user.click(within(dialog).getByRole('button', { name: 'Intrekken' }))

    await waitFor(() => expect(cancelSpy).toHaveBeenCalledWith('s2'))
  })
})
