import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { ApiError } from '../../../api/apiClient'
import { NewInvoicePage } from '../pages/NewInvoicePage'

/**
 * ACC-H1 / ACC-M2 / ACC-H3 / ACC-M4 on the invoice builder: manual lines follow the customer's
 * VAT, inputs keep 3/4 decimals, a draft announces that its number comes at sending, and a
 * refused creation shows the server's reason.
 */

const toast = vi.hoisted(() => ({ showError: vi.fn(), showSuccess: vi.fn(), showToast: vi.fn() }))
vi.mock('../../../components/ui/toastContext', () => ({ useToast: () => toast }))

vi.mock('../../customers/api/customersApi', () => ({
  searchCustomers: () =>
    Promise.resolve({ items: [{ id: 'cust-1', name: 'TEST ACCOUNTING Klant', customerNumber: 'KL-0006' }], totalCount: 1 }),
  getCustomer: () => Promise.resolve({ id: 'cust-1', invoiceGrouping: null }),
}))
vi.mock('../../customers/api/customerBillingConfigApi', () => ({
  getPoPolicy: () => Promise.resolve({ policy: 'Optional', effectivePoNumber: null }),
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
  getDefaultVatRate: vi.fn(),
}))
vi.mock('../api/invoicesApi', () => api)

async function chooseCustomer() {
  const user = userEvent.setup()
  render(
    <MemoryRouter>
      <NewInvoicePage />
    </MemoryRouter>,
  )
  const picker = await screen.findByRole('combobox', { name: /Klant/ })
  await user.click(picker)
  await user.click(await screen.findByRole('option', { name: 'TEST ACCOUNTING Klant (KL-0006)' }))
  return user
}

describe('NewInvoicePage — VAT default, precision, numbering, server reasons', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getNextInvoiceNumber.mockResolvedValue({ invoiceNumber: 'FAC-2026100014', legalEntityId: 'le-1', year: 2026, month: 10 })
    api.listUninvoicedOrders.mockResolvedValue([])
    api.listUninvoicedActivities.mockResolvedValue([])
    api.getDefaultVatRate.mockResolvedValue({ vatRatePercent: 21 })
  })

  it('a manual line takes the customer VAT (21) and the inputs accept 3/4 decimals', async () => {
    const user = await chooseCustomer()
    await waitFor(() => expect(api.getDefaultVatRate).toHaveBeenCalledWith('cust-1'))
    await user.click(screen.getByRole('button', { name: '+ Lijn toevoegen' }))
    expect(screen.getByLabelText('Btw %')).toHaveValue(21)
    expect(screen.getByLabelText('Hoeveelheid')).toHaveAttribute('step', '0.001')
    expect(screen.getByLabelText('Eenheidsprijs')).toHaveAttribute('step', '0.0001')
    expect(await screen.findByText(/toegekend bij het verzenden; de reeks begint bij FAC-2026100014/)).toBeInTheDocument()
  })

  it('without a known default the VAT field says it follows the customer and sends null', async () => {
    api.getDefaultVatRate.mockRejectedValue(new ApiError('nope', 404))
    api.createInvoice.mockResolvedValue({ id: 'inv-9', invoiceNumber: null, displayNumber: 'CONCEPT-1234' })
    const user = await chooseCustomer()
    await user.click(screen.getByRole('button', { name: '+ Lijn toevoegen' }))
    const vat = screen.getByLabelText('Btw %')
    expect(vat).toHaveValue(null)
    expect(vat).toHaveAttribute('placeholder', 'volgt klant')
    await user.type(screen.getByPlaceholderText(/Omschrijving/), 'Wachturen')
    await user.click(screen.getByRole('button', { name: 'Factuur aanmaken' }))
    await waitFor(() => expect(api.createInvoice).toHaveBeenCalledWith(expect.objectContaining({
      manualLines: [expect.objectContaining({ description: 'Wachturen', vatRatePercent: null })],
    })))
    // ACC-H3: a draft has no legal number yet.
    expect(toast.showSuccess).toHaveBeenCalledWith('Conceptfactuur aangemaakt.')
  })

  it('shows the server reason when the creation is refused', async () => {
    api.createInvoice.mockRejectedValue(new ApiError('Opdracht ORD-0062 heeft nog geen prijs en kan niet gefactureerd worden.', 400))
    const user = await chooseCustomer()
    await user.click(screen.getByRole('button', { name: '+ Lijn toevoegen' }))
    await user.type(screen.getByPlaceholderText(/Omschrijving/), 'Lijn')
    await user.click(screen.getByRole('button', { name: 'Factuur aanmaken' }))
    await waitFor(() => expect(toast.showError).toHaveBeenCalledWith('Opdracht ORD-0062 heeft nog geen prijs en kan niet gefactureerd worden.'))
  })
})
