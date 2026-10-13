import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { NewInvoicePage } from '../pages/NewInvoicePage'
import type { UninvoicedActivity } from '../types'

/**
 * Dossier activities (Opslag/Kraan) with an agreed price are invoiced next to transport
 * orders: the builder lists them per customer, ticks them like orders and sends their ids
 * as `dossierActivityIds`. A free activity still shows up (€ 0,00 + "gratis") so the
 * operator sees it was delivered at no charge.
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
}))
vi.mock('../api/invoicesApi', () => api)

function activity(overrides: Partial<UninvoicedActivity> = {}): UninvoicedActivity {
  return {
    id: 'act-1',
    dossierId: 'dos-1',
    dossierNumber: 'DOS-0042',
    dossierTitle: 'Verhuis Antwerpen',
    activityTypeName: 'Opslag',
    label: 'Week 34',
    plannedDate: '2026-08-20',
    agreedPrice: 150,
    isFree: false,
    pricingSource: 'OneOff',
    priceLineCount: 0,
    legalEntityId: null,
    ...overrides,
  }
}

/** Renders the builder and picks the one mocked customer. */
async function chooseCustomer() {
  const user = userEvent.setup()
  render(
    <MemoryRouter>
      <NewInvoicePage />
    </MemoryRouter>,
  )
  const select = await screen.findByLabelText(/Klant/)
  await screen.findByRole('option', { name: 'Haven BV (K-0001)' })
  await user.selectOptions(select, 'cust-1')
  return user
}

describe('NewInvoicePage — factureerbare activiteiten', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.createInvoice.mockResolvedValue({ id: 'inv-1', invoiceNumber: 'F2026-001' })
    api.getNextInvoiceNumber.mockRejectedValue(new Error('no entity'))
    api.listUninvoicedOrders.mockResolvedValue([])
  })

  it('lists the activities of the chosen customer with dossier, type, amount and the free marker', async () => {
    api.listUninvoicedActivities.mockResolvedValue([
      activity(),
      activity({ id: 'act-2', dossierNumber: 'DOS-0043', dossierTitle: null, activityTypeName: 'Kraan', label: null, agreedPrice: 0, isFree: true }),
    ])
    await chooseCustomer()

    const section = (await screen.findByText('Factureerbare activiteiten (2)')).closest('section')!
    await waitFor(() => expect(api.listUninvoicedActivities).toHaveBeenCalledWith('cust-1'))
    expect(within(section).getByText('DOS-0042')).toBeInTheDocument()
    expect(within(section).getByText('Verhuis Antwerpen')).toBeInTheDocument()
    expect(within(section).getByText('Opslag')).toBeInTheDocument()
    expect(within(section).getByText('Week 34')).toBeInTheDocument()
    expect(within(section).getByText('€ 150,00')).toBeInTheDocument()
    // The free one keeps its € 0,00 amount and is marked as such.
    expect(within(section).getByText('DOS-0043')).toBeInTheDocument()
    expect(within(section).getByText('Kraan')).toBeInTheDocument()
    expect(within(section).getByText('€ 0,00')).toBeInTheDocument()
    expect(within(section).getByText('gratis')).toBeInTheDocument()
  })

  it('sends the ticked activity ids as dossierActivityIds next to an empty orderIds', async () => {
    api.listUninvoicedActivities.mockResolvedValue([
      activity(),
      activity({ id: 'act-2', dossierNumber: 'DOS-0043', activityTypeName: 'Kraan', agreedPrice: 80 }),
    ])
    const user = await chooseCustomer()
    await screen.findByText('Factureerbare activiteiten (2)')

    // Everything is ticked by default (same as orders); untick one and keep the other.
    const first = screen.getByLabelText('Selecteer activiteit Opslag (DOS-0042)')
    const second = screen.getByLabelText('Selecteer activiteit Kraan (DOS-0043)')
    expect(first).toBeChecked()
    expect(second).toBeChecked()
    await user.click(first)
    expect(first).not.toBeChecked()
    // The running estimate only counts what stays selected.
    expect(screen.getByText('€ 80,00', { selector: 'strong' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Factuur aanmaken' }))

    await waitFor(() =>
      expect(api.createInvoice).toHaveBeenCalledWith(
        expect.objectContaining({ customerId: 'cust-1', orderIds: [], dossierActivityIds: ['act-2'] }),
      ),
    )
  })

  it('shows the empty state for activities when the customer has none', async () => {
    api.listUninvoicedActivities.mockResolvedValue([])
    await chooseCustomer()

    expect(await screen.findByText('Factureerbare activiteiten (0)')).toBeInTheDocument()
    expect(screen.getByText('Geen ongefactureerde activiteiten voor deze klant.')).toBeInTheDocument()
    expect(screen.getByText('Geen afgeronde, ongefactureerde opdrachten voor deze klant.')).toBeInTheDocument()
  })
})
