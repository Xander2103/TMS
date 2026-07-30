import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MessagesTab } from '../MessagesTab'
import type { EdiMessageRow, EdiPartner } from '../../api/ediApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  listMessages: vi.fn(),
  listPartners: vi.fn(),
  replayMessage: vi.fn(),
}))
vi.mock('../../api/ediApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/ediApi')>()),
  listMessages: api.listMessages,
  listPartners: api.listPartners,
  replayMessage: api.replayMessage,
}))

function row(overrides: Partial<EdiMessageRow> = {}): EdiMessageRow {
  return {
    id: 'msg-1', direction: 'Inbound', partnerCode: 'haven-edi', messageType: 'order',
    externalReference: 'EXT-1', status: 'Failed', attemptCount: 2, processedAt: null,
    errorDetail: 'Locatiemapping onvolledig.', mappingIssue: true, resultEntityType: null,
    resultEntityId: null, createdAt: '2026-07-30T10:00:00Z',
    ...overrides,
  }
}

function partner(overrides: Partial<EdiPartner> = {}): EdiPartner {
  return {
    id: 'partner-1', code: 'haven-edi', name: 'Haven EDI', customerId: 'cust-1', customerName: 'Haven BV',
    externalCustomerIdentifier: null, mappingProfile: 'generic-json-v1', isActive: true, notes: null, locations: [],
    ...overrides,
  }
}

describe('MessagesTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listPartners.mockResolvedValue([partner()])
    api.listMessages.mockResolvedValue({ items: [row()], totalCount: 1, page: 1, pageSize: 25 })
  })

  it('loads with no filters set', async () => {
    render(<MessagesTab canRetry />)
    await waitFor(() =>
      expect(api.listMessages).toHaveBeenCalledWith(
        expect.objectContaining({ direction: undefined, status: undefined, partnerId: undefined, search: undefined, page: 1 }),
      ),
    )
  })

  it('passes the richting filter through as a query param', async () => {
    const user = userEvent.setup()
    render(<MessagesTab canRetry />)
    await screen.findByText('EXT-1')

    await user.selectOptions(screen.getByLabelText('Richting'), 'Inbound')

    await waitFor(() => expect(api.listMessages).toHaveBeenLastCalledWith(expect.objectContaining({ direction: 'Inbound' })))
  })

  it('passes the status filter through as a query param', async () => {
    const user = userEvent.setup()
    render(<MessagesTab canRetry />)
    await screen.findByText('EXT-1')

    await user.selectOptions(screen.getByLabelText('Status'), 'Failed')

    await waitFor(() => expect(api.listMessages).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Failed' })))
  })

  it('passes the partner filter through as a query param', async () => {
    const user = userEvent.setup()
    render(<MessagesTab canRetry />)
    await screen.findByText('EXT-1')

    await user.selectOptions(screen.getByLabelText('Partner'), 'partner-1')

    await waitFor(() => expect(api.listMessages).toHaveBeenLastCalledWith(expect.objectContaining({ partnerId: 'partner-1' })))
  })

  it('passes the search term through as a query param', async () => {
    const user = userEvent.setup()
    render(<MessagesTab canRetry />)
    await screen.findByText('EXT-1')

    await user.type(screen.getByPlaceholderText('Zoeken op externe referentie...'), 'EXT-1')

    await waitFor(() => expect(api.listMessages).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'EXT-1' })))
  })

  it('shows the Opnieuw action only for Failed/DeadLettered rows when canRetry is true', async () => {
    render(<MessagesTab canRetry />)
    expect(await screen.findByRole('button', { name: 'Opnieuw' })).toBeInTheDocument()
  })

  it('hides the Opnieuw action when canRetry is false', async () => {
    render(<MessagesTab canRetry={false} />)
    await screen.findByText('EXT-1')
    expect(screen.queryByRole('button', { name: 'Opnieuw' })).not.toBeInTheDocument()
  })

  it('does not show Opnieuw for a Processed row even when canRetry is true', async () => {
    api.listMessages.mockResolvedValue({ items: [row({ status: 'Processed' })], totalCount: 1, page: 1, pageSize: 25 })
    render(<MessagesTab canRetry />)
    await screen.findByText('EXT-1')
    expect(screen.queryByRole('button', { name: 'Opnieuw' })).not.toBeInTheDocument()
  })
})
