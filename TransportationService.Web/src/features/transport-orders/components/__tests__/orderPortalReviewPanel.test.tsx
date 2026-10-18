import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderPortalReviewPanel } from '../OrderPortalReviewPanel'
import type { TransportOrderDetail } from '../../types'
import type { PortalReviewState } from '../../api/transportOrdersApi'

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const reviewSpy = vi.hoisted(() => vi.fn())
const stateSpy = vi.hoisted(() => vi.fn((): Promise<PortalReviewState> => Promise.resolve({ infoRequest: null })))
vi.mock('../../api/transportOrdersApi', () => ({
  reviewPortalOrder: reviewSpy,
  getPortalReviewState: stateSpy,
}))

function order(overrides: Partial<TransportOrderDetail> = {}): TransportOrderDetail {
  return {
    id: 'o1',
    status: 'Submitted',
    orderNumber: 'ORD-1',
    customerId: 'c1',
    customerName: 'Haven BV',
    ...overrides,
  } as TransportOrderDetail
}

describe('OrderPortalReviewPanel', () => {
  it('shows the open question, then the customer answer, where the planner reviews', async () => {
    stateSpy.mockResolvedValueOnce({
      infoRequest: {
        question: 'Wat is het gewicht per pallet?',
        askedAt: '2026-10-18T08:00:00Z',
        answers: [{ body: '650 kg per pallet.', at: '2026-10-18T09:00:00Z' }],
      },
    })
    render(<OrderPortalReviewPanel order={order()} onReviewed={vi.fn()} />)

    expect(await screen.findByText(/650 kg per pallet\./)).toBeInTheDocument()
    expect(screen.getByText(/Wat is het gewicht per pallet\?/)).toBeInTheDocument()
    expect(stateSpy).toHaveBeenCalledWith('o1')
  })

  it('says the request waits for the customer while the question is open', async () => {
    stateSpy.mockResolvedValueOnce({ infoRequest: { question: 'Laadadres?', askedAt: '2026-10-18T08:00:00Z', answers: [] } })
    render(<OrderPortalReviewPanel order={order()} onReviewed={vi.fn()} />)

    expect(await screen.findByText(/Wacht op antwoord van de klant/)).toBeInTheDocument()
  })

  it('renders nothing when the order is not Submitted', () => {
    const { container } = render(<OrderPortalReviewPanel order={order({ status: 'Confirmed' })} onReviewed={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('accepts a submitted order without a reason', async () => {
    reviewSpy.mockResolvedValue(order({ status: 'Confirmed' }))
    const onReviewed = vi.fn()
    const user = userEvent.setup()
    render(<OrderPortalReviewPanel order={order()} onReviewed={onReviewed} />)

    await user.click(screen.getByRole('button', { name: 'Accepteren' }))

    await waitFor(() => expect(reviewSpy).toHaveBeenCalledWith('o1', 'Accept', null))
    expect(onReviewed).toHaveBeenCalled()
  })

  it('requires a reason before rejecting', async () => {
    const user = userEvent.setup()
    render(<OrderPortalReviewPanel order={order()} onReviewed={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: 'Afwijzen' }))
    const confirmButton = screen.getByRole('button', { name: 'Bevestigen' })
    expect(confirmButton).toBeDisabled()

    await user.type(screen.getByLabelText(/Reden/), 'Onvoldoende capaciteit')
    expect(confirmButton).toBeEnabled()
    await user.click(confirmButton)

    await waitFor(() => expect(reviewSpy).toHaveBeenCalledWith('o1', 'Reject', 'Onvoldoende capaciteit'))
  })

  it('sends a request-info action with the note as reason', async () => {
    reviewSpy.mockResolvedValue(order())
    const user = userEvent.setup()
    render(<OrderPortalReviewPanel order={order()} onReviewed={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: 'Info opvragen' }))
    await user.type(screen.getByLabelText(/Welke informatie/), 'Wat is het exacte adres?')
    await user.click(screen.getByRole('button', { name: 'Bevestigen' }))

    await waitFor(() => expect(reviewSpy).toHaveBeenCalledWith('o1', 'RequestInfo', 'Wat is het exacte adres?'))
  })
})
