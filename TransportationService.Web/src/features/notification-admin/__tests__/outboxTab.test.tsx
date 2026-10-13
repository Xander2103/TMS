import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { OutboxTab } from '../components/OutboxTab'
import type { OutboxRow } from '../types'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listOutbox: vi.fn(),
  retryOutboxMessage: vi.fn(),
  releaseOutboxMessage: vi.fn(),
  rejectOutboxMessage: vi.fn(),
}))
vi.mock('../api/notificationAdminApi', () => api)

function failedRow(overrides: Partial<OutboxRow> = {}): OutboxRow {
  return {
    id: 'msg-1',
    channel: 'Email',
    kind: 'order_accepted',
    recipientAddress: 'haven@klant.test',
    recipientName: 'Haven BV',
    subject: 'Onderwerp',
    status: 'Failed',
    attemptCount: 3,
    nextAttemptAt: null,
    sentAt: null,
    failureReason: 'SMTP timeout',
    createdAt: '2026-07-30T10:00:00Z',
    isFallback: false,
    relatedEntityType: null,
    relatedEntityId: null,
    ...overrides,
  }
}

describe('OutboxTab (failed variant)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.retryOutboxMessage.mockResolvedValue(undefined)
  })

  it('retries a failed message and reloads the list', async () => {
    const user = userEvent.setup()
    api.listOutbox.mockResolvedValue({ items: [failedRow()], totalCount: 1, page: 1, pageSize: 25 })

    render(
      <MemoryRouter>
        <OutboxTab variant="failed" includeSuppressedToggle />
      </MemoryRouter>,
    )

    expect(await screen.findByText('SMTP timeout')).toBeInTheDocument()
    expect(api.listOutbox).toHaveBeenCalledWith(expect.objectContaining({ status: 'Failed' }))

    await user.click(screen.getByRole('button', { name: 'Opnieuw proberen' }))

    await waitFor(() => expect(api.retryOutboxMessage).toHaveBeenCalledWith('msg-1'))
    await waitFor(() => expect(api.listOutbox).toHaveBeenCalledTimes(2))
  })

  it('switches to Suppressed status when the toggle is checked', async () => {
    const user = userEvent.setup()
    api.listOutbox.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 25 })

    render(
      <MemoryRouter>
        <OutboxTab variant="failed" includeSuppressedToggle />
      </MemoryRouter>,
    )

    await waitFor(() => expect(api.listOutbox).toHaveBeenCalledWith(expect.objectContaining({ status: 'Failed' })))

    await user.click(screen.getByLabelText('Onderdrukte berichten tonen'))

    await waitFor(() => expect(api.listOutbox).toHaveBeenCalledWith(expect.objectContaining({ status: 'Suppressed' })))
  })
})

describe('OutboxTab (review variant — P9 "Wacht op controle")', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.releaseOutboxMessage.mockResolvedValue(undefined)
    api.rejectOutboxMessage.mockResolvedValue(undefined)
  })

  function reviewRow(overrides: Partial<OutboxRow> = {}): OutboxRow {
    return failedRow({ status: 'AwaitingReview', failureReason: null, attemptCount: 0, ...overrides })
  }

  it('lists AwaitingReview messages and releases one back into the send queue', async () => {
    const user = userEvent.setup()
    api.listOutbox.mockResolvedValue({ items: [reviewRow()], totalCount: 1, page: 1, pageSize: 25 })

    render(
      <MemoryRouter>
        <OutboxTab variant="review" />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Haven BV · haven@klant.test')).toBeInTheDocument()
    expect(api.listOutbox).toHaveBeenCalledWith(expect.objectContaining({ status: 'AwaitingReview' }))

    await user.click(screen.getByRole('button', { name: 'Vrijgeven' }))

    await waitFor(() => expect(api.releaseOutboxMessage).toHaveBeenCalledWith('msg-1'))
    await waitFor(() => expect(api.listOutbox).toHaveBeenCalledTimes(2))
  })

  it('rejects a message with an inline reason', async () => {
    const user = userEvent.setup()
    api.listOutbox.mockResolvedValue({ items: [reviewRow()], totalCount: 1, page: 1, pageSize: 25 })

    render(
      <MemoryRouter>
        <OutboxTab variant="review" />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole('button', { name: 'Afwijzen' }))
    await user.type(screen.getByLabelText('Reden van afwijzing voor haven@klant.test'), 'Verkeerde ontvanger')
    await user.click(screen.getByRole('button', { name: 'Bevestig afwijzen' }))

    await waitFor(() => expect(api.rejectOutboxMessage).toHaveBeenCalledWith('msg-1', 'Verkeerde ontvanger'))
    await waitFor(() => expect(api.listOutbox).toHaveBeenCalledTimes(2))
  })
})
