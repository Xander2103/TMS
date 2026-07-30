import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OrderPortalReviewPanel } from '../OrderPortalReviewPanel'
import type { TransportOrderDetail } from '../../types'

const toast = vi.hoisted(() => ({ showSuccess: vi.fn(), showError: vi.fn() }))
vi.mock('../../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: toast.showSuccess, showError: toast.showError }),
}))

const reviewSpy = vi.hoisted(() => vi.fn())
vi.mock('../../api/transportOrdersApi', () => ({
  reviewPortalOrder: reviewSpy,
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
