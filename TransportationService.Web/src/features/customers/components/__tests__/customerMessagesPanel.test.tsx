import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerMessagesPanel } from '../CustomerMessagesPanel'
import type { CustomerMessage } from '../../api/customerMessagesApi'

const auth = vi.hoisted(() => ({ canSend: true }))
vi.mock('../../../auth/authContextValue', () => ({
  useAuth: () => ({ hasPermission: (code: string) => code === 'customer_messages.send' && auth.canSend }),
}))

const messages = vi.hoisted(() => ({ value: [] as CustomerMessage[] }))
const sendSpy = vi.hoisted(() => vi.fn())
const markReadSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/customerMessagesApi', () => ({
  listCustomerMessages: () => Promise.resolve(messages.value),
  sendCustomerMessage: sendSpy,
  markCustomerMessagesRead: markReadSpy,
}))

describe('CustomerMessagesPanel', () => {
  beforeEach(() => {
    auth.canSend = true
    messages.value = [
      { id: 'm1', transportOrderId: null, orderNumber: null, authorIsStaff: false, authorName: 'Kaat Klant', body: 'Vraag over levering', createdAt: '2026-07-29T09:00:00Z' },
    ]
    sendSpy.mockReset().mockResolvedValue({
      id: 'm2', transportOrderId: null, orderNumber: null, authorIsStaff: true, authorName: 'Pia Planner', body: 'Antwoord', createdAt: '2026-07-30T09:00:00Z',
    })
    markReadSpy.mockReset().mockResolvedValue(undefined)
  })

  it('renders the thread and marks it read for this customer', async () => {
    render(<CustomerMessagesPanel customerId="c1" />)

    expect(await screen.findByText('Vraag over levering')).toBeInTheDocument()
    await waitFor(() => expect(markReadSpy).toHaveBeenCalledWith('c1', null))
  })

  it('lets a staff member with customer_messages.send reply', async () => {
    const user = userEvent.setup()
    render(<CustomerMessagesPanel customerId="c1" />)

    await screen.findByText('Vraag over levering')
    await user.type(screen.getByLabelText('Nieuw bericht'), 'We leveren morgen.')
    await user.click(screen.getByRole('button', { name: 'Versturen' }))

    await waitFor(() => expect(sendSpy).toHaveBeenCalledWith('c1', null, 'We leveren morgen.'))
  })

  it('hides the compose form without customer_messages.send', async () => {
    auth.canSend = false
    render(<CustomerMessagesPanel customerId="c1" />)

    await screen.findByText('Vraag over levering')
    expect(screen.queryByLabelText('Nieuw bericht')).not.toBeInTheDocument()
  })

  it('scopes the thread to one order when orderId is set', async () => {
    render(<CustomerMessagesPanel customerId="c1" orderId="o1" />)

    await waitFor(() => expect(markReadSpy).toHaveBeenCalledWith('c1', 'o1'))
  })
})
