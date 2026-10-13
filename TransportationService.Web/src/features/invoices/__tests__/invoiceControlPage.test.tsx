import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { InvoiceControlPage } from '../pages/InvoiceControlPage'
import type { ControlOrder, InvoiceControl } from '../api/invoicesApi'

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))

const api = vi.hoisted(() => ({
  getInvoiceControl: vi.fn(),
  createInvoice: vi.fn(),
  snoozeInvoiceControlOrder: vi.fn(),
}))
vi.mock('../api/invoicesApi', () => api)

function controlOrder(overrides: Partial<ControlOrder> = {}): ControlOrder {
  return {
    transportOrderId: 'order-1',
    orderNumber: 'ORD-0001',
    orderDate: '2026-08-10',
    agreedPrice: 100,
    dossierNumber: 'DOS-0001',
    customerReference: null,
    invoiceReadiness: 'ReadyForInvoice',
    reasons: [],
    snoozedUntil: null,
    snoozeReason: null,
    ...overrides,
  }
}

function control(overrides: Partial<InvoiceControl> = {}): InvoiceControl {
  return {
    proposals: [
      {
        customerId: 'cust-1',
        customerName: 'Haven BV',
        grouping: 'PerDossier',
        groupLabel: 'Dossier DOS-0001',
        orders: [
          controlOrder(),
          controlOrder({ transportOrderId: 'order-2', orderNumber: 'ORD-0002', agreedPrice: 250 }),
        ],
        totalAmount: 350,
      },
    ],
    needsReview: [],
    pendingCharges: [],
    snoozed: [],
    ...overrides,
  }
}

function renderPage() {
  return render(
    <MemoryRouter>
      <InvoiceControlPage />
    </MemoryRouter>,
  )
}

describe('InvoiceControlPage (P12: selectie + uitstel)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.snoozeInvoiceControlOrder.mockResolvedValue(undefined)
    api.createInvoice.mockResolvedValue({ id: 'inv-1', invoiceNumber: 'F2026-001' })
  })

  it('checks every proposal order by default and invoices only the selected ids', async () => {
    const user = userEvent.setup()
    api.getInvoiceControl.mockResolvedValue(control())
    renderPage()

    const firstCheckbox = await screen.findByLabelText('Order ORD-0001 meenemen op de factuur')
    const secondCheckbox = screen.getByLabelText('Order ORD-0002 meenemen op de factuur')
    expect(firstCheckbox).toBeChecked()
    expect(secondCheckbox).toBeChecked()
    expect(screen.getByText(/2 van 2 orders aangevinkt/)).toBeInTheDocument()

    await user.click(firstCheckbox)
    expect(screen.getByText(/1 van 2 orders aangevinkt/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Maak factuur' }))

    await waitFor(() =>
      expect(api.createInvoice).toHaveBeenCalledWith(
        expect.objectContaining({ customerId: 'cust-1', orderIds: ['order-2'] }),
      ),
    )
  })

  it('snoozes an order with date + reason via the inline editor and reloads', async () => {
    const user = userEvent.setup()
    api.getInvoiceControl.mockResolvedValue(control())
    renderPage()

    await screen.findByLabelText('Order ORD-0001 meenemen op de factuur')
    await user.click(screen.getAllByRole('button', { name: 'Uitstellen' })[0])

    await user.type(screen.getByLabelText('Uitstellen tot voor ORD-0001'), '2026-09-01')
    await user.type(screen.getByLabelText('Reden van uitstel voor ORD-0001'), 'Wacht op creditnota')
    await user.click(screen.getByRole('button', { name: 'Bevestig uitstel' }))

    await waitFor(() =>
      expect(api.snoozeInvoiceControlOrder).toHaveBeenCalledWith('order-1', {
        until: '2026-09-01',
        reason: 'Wacht op creditnota',
      }),
    )
    await waitFor(() => expect(api.getInvoiceControl).toHaveBeenCalledTimes(2))
  })

  it('lists snoozed orders with until/reason and clears the snooze via until = null', async () => {
    const user = userEvent.setup()
    api.getInvoiceControl.mockResolvedValue(
      control({
        proposals: [],
        snoozed: [
          controlOrder({
            transportOrderId: 'order-9',
            orderNumber: 'ORD-0009',
            snoozedUntil: '2026-09-15',
            snoozeReason: 'Klant betwist',
          }),
        ],
      }),
    )
    renderPage()

    const section = (await screen.findByText('Uitgesteld (1)')).closest('section')!
    expect(within(section).getByText('ORD-0009')).toBeInTheDocument()
    expect(within(section).getByText('2026-09-15')).toBeInTheDocument()
    expect(within(section).getByText('Klant betwist')).toBeInTheDocument()
    expect(within(section).getByRole('link', { name: 'Naar opdracht' })).toHaveAttribute(
      'href',
      '/transport-orders/order-9',
    )

    await user.click(within(section).getByRole('button', { name: 'Uitstel opheffen' }))

    await waitFor(() =>
      expect(api.snoozeInvoiceControlOrder).toHaveBeenCalledWith('order-9', { until: null, reason: null }),
    )
    await waitFor(() => expect(api.getInvoiceControl).toHaveBeenCalledTimes(2))
  })
})
