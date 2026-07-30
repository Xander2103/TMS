import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { CustomerPortalMessagesPage } from '../CustomerPortalMessagesPage'
import type { CustomerMessage } from '../../api/customerPortalApi'

const messages = vi.hoisted(() => ({ value: [] as CustomerMessage[] }))
const sendSpy = vi.hoisted(() => vi.fn())
const markReadSpy = vi.hoisted(() => vi.fn())

vi.mock('../../api/customerPortalApi', () => ({
  listPortalMessages: () => Promise.resolve(messages.value),
  sendPortalMessage: sendSpy,
  markPortalMessagesRead: markReadSpy,
}))

describe('CustomerPortalMessagesPage', () => {
  beforeEach(() => {
    messages.value = []
    sendSpy.mockReset().mockResolvedValue({
      id: 'm2', transportOrderId: null, orderNumber: null, authorIsStaff: false, authorName: 'U', body: 'Hallo', createdAt: '2026-07-30T10:00:00Z',
    })
    markReadSpy.mockReset().mockResolvedValue(undefined)
  })

  it('renders the thread and marks it read on load', async () => {
    messages.value = [
      { id: 'm1', transportOrderId: null, orderNumber: null, authorIsStaff: true, authorName: 'Pia Planner', body: 'Welkom!', createdAt: '2026-07-29T10:00:00Z' },
    ]
    render(
      <MemoryRouter>
        <CustomerPortalMessagesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Welkom!')).toBeInTheDocument()
    await waitFor(() => expect(markReadSpy).toHaveBeenCalledWith(null))
  })

  it('sends a new message and refreshes the thread', async () => {
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <CustomerPortalMessagesPage />
      </MemoryRouter>,
    )

    await waitFor(() => expect(screen.getByText('Nog geen berichten in dit gesprek.')).toBeInTheDocument())
    await user.type(screen.getByLabelText('Nieuw bericht'), 'Wanneer wordt dit geleverd?')
    await user.click(screen.getByRole('button', { name: 'Versturen' }))

    await waitFor(() => expect(sendSpy).toHaveBeenCalledWith(null, 'Wanneer wordt dit geleverd?'))
  })
})
