import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { MessageDetailModal } from '../MessageDetailModal'
import type { EdiMessageDetail } from '../../api/ediApi'

vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  getMessage: vi.fn(),
  replayMessage: vi.fn(),
}))
vi.mock('../../api/ediApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/ediApi')>()),
  getMessage: api.getMessage,
  replayMessage: api.replayMessage,
}))

function detail(overrides: Partial<EdiMessageDetail> = {}): EdiMessageDetail {
  return {
    id: 'msg-1', direction: 'Inbound', partnerCode: 'haven-edi', messageType: 'order',
    externalReference: 'EXT-1', status: 'Failed', attemptCount: 2, processedAt: null,
    errorDetail: 'Locatiemapping onvolledig.', mappingIssue: true, resultEntityType: null,
    resultEntityId: null, createdAt: '2026-07-30T10:00:00Z',
    validationErrors: ["Onbekende locatiecode 'X' voor deze partner."],
    payloadJson: '{"externalOrderId":"EXT-1"}',
    ...overrides,
  }
}

function renderModal(overrides: Partial<Parameters<typeof MessageDetailModal>[0]> = {}) {
  return render(
    <MemoryRouter>
      <MessageDetailModal id="msg-1" canRetry onClose={vi.fn()} onReplayed={vi.fn()} {...overrides} />
    </MemoryRouter>,
  )
}

describe('MessageDetailModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders a structured header, payload and validation errors — not a raw JSON dump', async () => {
    api.getMessage.mockResolvedValue(detail())
    renderModal()

    expect(await screen.findByText('haven-edi')).toBeInTheDocument()
    expect(screen.getByText('Inkomend')).toBeInTheDocument()
    expect(screen.getByText('2 / 3')).toBeInTheDocument()
    expect(screen.getByText('EXT-1')).toBeInTheDocument()
    expect(screen.getByText('Locatiemapping onvolledig.')).toBeInTheDocument()
    expect(screen.getByText("Onbekende locatiecode 'X' voor deze partner.")).toBeInTheDocument()
    expect(screen.getByText('{"externalOrderId":"EXT-1"}')).toBeInTheDocument()
  })

  it('shows a "Bekijk order" link when the message resolved to a TransportOrder', async () => {
    api.getMessage.mockResolvedValue(detail({ status: 'Processed', resultEntityType: 'TransportOrder', resultEntityId: 'order-9' }))
    renderModal()

    const link = await screen.findByRole('link', { name: 'Bekijk order' })
    expect(link).toHaveAttribute('href', '/transport-orders/order-9')
  })

  it('shows the replay button only for Failed/DeadLettered messages when canRetry is true', async () => {
    api.getMessage.mockResolvedValue(detail({ status: 'Processed', resultEntityType: null }))
    renderModal()
    await screen.findByText('haven-edi')
    expect(screen.queryByRole('button', { name: 'Opnieuw verwerken' })).not.toBeInTheDocument()
  })

  it('replays the message and calls onReplayed', async () => {
    const user = userEvent.setup()
    const onReplayed = vi.fn()
    api.getMessage.mockResolvedValue(detail())
    api.replayMessage.mockResolvedValue({ id: 'msg-1', status: 'Processed', errorDetail: null })
    renderModal({ onReplayed })

    await user.click(await screen.findByRole('button', { name: 'Opnieuw verwerken' }))

    await waitFor(() => expect(api.replayMessage).toHaveBeenCalledWith('msg-1'))
    await waitFor(() => expect(onReplayed).toHaveBeenCalled())
  })

  it('hides the replay button when canRetry is false, even for a Failed message', async () => {
    api.getMessage.mockResolvedValue(detail())
    renderModal({ canRetry: false })
    await screen.findByText('haven-edi')
    expect(screen.queryByRole('button', { name: 'Opnieuw verwerken' })).not.toBeInTheDocument()
  })
})
