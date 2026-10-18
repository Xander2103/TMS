import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewInvoicePage } from '../pages/NewInvoicePage'
import type { UninvoicedOrder } from '../types'

/**
 * MD-H10: an order whose ONLY readiness reason is "po.missing" is invoiceable (the server sends
 * isInvoiceable: true; the PO is then required on the invoice at send). The builder must not
 * block it client-side and must never show the raw code.
 */

vi.mock('../../../components/ui/toastContext', () => ({
  useToast: () => ({ showToast: vi.fn(), showSuccess: vi.fn(), showError: vi.fn() }),
}))
vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({ items: [{ id: 'cust-1', name: 'Haven BV', customerNumber: 'K-0001' }], totalCount: 1 }),
  getCustomer: () => Promise.resolve({ id: 'cust-1', invoiceGrouping: null }),
}))
vi.mock('../../customers/api/customerBillingConfigApi', () => ({
  getPoPolicy: () => Promise.resolve({ policy: 'Required', effectivePoNumber: null }),
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

function order(overrides: Partial<UninvoicedOrder> = {}): UninvoicedOrder {
  return {
    id: 'ord-1',
    orderNumber: 'ORD-0104',
    orderDate: '2026-10-10',
    goodsDescription: 'Pallets',
    firstLoadingCity: 'Gent',
    lastUnloadingCity: 'Antwerpen',
    agreedPrice: 250,
    invoiceReadiness: 'ReviewRequired',
    invoiceReadinessReasons: 'po.missing',
    isInvoiceable: true,
    ...overrides,
  }
}

beforeEach(() => {
  vi.clearAllMocks()
  api.createInvoice.mockResolvedValue({ id: 'inv-1', invoiceNumber: null })
  api.getNextInvoiceNumber.mockRejectedValue(new Error('no entity'))
  api.listUninvoicedActivities.mockResolvedValue([])
})

describe('NewInvoicePage — order waiting only for its PO', () => {
  it('is pre-selected, selectable and sent; the reason reads as Dutch text', async () => {
    api.listUninvoicedOrders.mockResolvedValue([order()])
    const user = userEvent.setup()
    render(
      <MemoryRouter>
        <NewInvoicePage />
      </MemoryRouter>,
    )
    await user.click(await screen.findByRole('combobox', { name: /Klant/ }))
    await user.click(await screen.findByRole('option', { name: 'Haven BV (K-0001)' }))

    const checkbox = await screen.findByLabelText('Selecteer ORD-0104')
    expect(checkbox).toBeEnabled()
    expect(checkbox).toBeChecked()
    expect(screen.getByText('Nakijken')).toHaveAttribute(
      'title',
      'PO-nummer ontbreekt — registreer het PO van de klant of vul het in op de factuur',
    )
    expect(screen.queryByText(/po\.missing/)).not.toBeInTheDocument()

    await user.type(screen.getByLabelText(/PO-nummer/), 'PO-778')
    await user.click(screen.getByRole('button', { name: 'Factuur aanmaken' }))
    await waitFor(() =>
      expect(api.createInvoice).toHaveBeenCalledWith(
        expect.objectContaining({ orderIds: ['ord-1'], purchaseOrderNumber: 'PO-778' }),
      ),
    )
  })
})
