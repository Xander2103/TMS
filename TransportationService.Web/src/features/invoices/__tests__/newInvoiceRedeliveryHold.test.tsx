import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewInvoicePage } from '../pages/NewInvoicePage'
import type { UninvoicedOrder } from '../types'

/** R-N2: the server holds a redelivery next to its charging original; the builder shows why and never selects it. */

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({ items: [{ id: 'cust-1', name: 'Haven BV', customerNumber: 'K-0001' }], totalCount: 1 }),
  getCustomer: () => Promise.resolve({ id: 'cust-1', invoiceGrouping: null }),
}))
vi.mock('../../customers/api/customerBillingConfigApi', () => ({
  getPoPolicy: () => Promise.resolve({ policy: 'None', effectivePoNumber: null }),
}))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({
  getLegalEntityOptions: () => Promise.resolve([]),
  getActiveLegalEntity: () => Promise.resolve({ legalEntityId: null }),
}))

const api = vi.hoisted(() => ({
  createInvoice: vi.fn(),
  getNextInvoiceNumber: vi.fn(),
  listUninvoicedOrders: vi.fn(),
  listUninvoicedActivities: vi.fn(),
  getDefaultVatRate: vi.fn(() => Promise.resolve({ vatRatePercent: 21 })),
}))
vi.mock('../api/invoicesApi', () => api)

const base: UninvoicedOrder = {
  id: 'ord-2',
  orderNumber: 'ORD-0002',
  orderDate: '2026-10-30',
  goodsDescription: 'Pallets',
  firstLoadingCity: 'Gent',
  lastUnloadingCity: 'Antwerpen',
  agreedPrice: 300,
  invoiceReadiness: 'ReadyForInvoice',
  invoiceReadinessReasons: null,
  isInvoiceable: true,
}

beforeEach(() => {
  vi.clearAllMocks()
  api.createInvoice.mockResolvedValue({ id: 'inv-1', invoiceNumber: null })
  api.getNextInvoiceNumber.mockRejectedValue(new Error('no entity'))
  api.listUninvoicedActivities.mockResolvedValue([])
})

describe('NewInvoicePage: failed delivery and its redelivery', () => {
  it('selects only the original, explains the held redelivery in words and sends one order', async () => {
    api.listUninvoicedOrders.mockResolvedValue([
      base,
      {
        ...base,
        id: 'ord-3',
        orderNumber: 'ORD-0003',
        invoiceReadiness: 'ReviewRequired',
        invoiceReadinessReasons: 'redelivery.original_charged',
        isInvoiceable: false,
        billingHoldOrderNumber: 'ORD-0002',
      },
    ])
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <NewInvoicePage />
      </MemoryRouter>,
    )
    await user.click(await screen.findByRole('combobox', { name: /Klant/ }))
    await user.click(await screen.findByRole('option', { name: /Haven BV/ }))

    expect(await screen.findByLabelText('Selecteer ORD-0002')).toBeChecked()
    const held = screen.getByLabelText('Selecteer ORD-0003')
    expect(held).not.toBeChecked()
    expect(held).toBeDisabled()
    expect(screen.getByText(/de originele opdracht ORD-0002 draagt de transportprijs al/)).toBeInTheDocument()
    expect(screen.queryByText(/redelivery\.original_charged/)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Factuur aanmaken' }))
    await waitFor(() =>
      expect(api.createInvoice).toHaveBeenCalledWith(expect.objectContaining({ orderIds: ['ord-2'] })),
    )
  })
})
