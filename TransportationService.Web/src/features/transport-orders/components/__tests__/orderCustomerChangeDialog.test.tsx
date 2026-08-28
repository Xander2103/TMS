import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { OrderCustomerChangeDialog } from '../OrderCustomerChangeDialog'
import type { OrderCustomerChangeImpact } from '../../api/transportOrdersApi'
import type { CustomerListItem } from '../../../customers/types'

const api = vi.hoisted(() => ({
  searchCustomers: vi.fn(),
  getOrderCustomerChangeImpact: vi.fn(),
  changeOrderCustomer: vi.fn(),
}))

vi.mock('../../../customers/api/customersApi', async (orig) => ({
  ...(await orig<typeof import('../../../customers/api/customersApi')>()),
  searchCustomers: api.searchCustomers,
}))
vi.mock('../../api/transportOrdersApi', async (orig) => ({
  ...(await orig<typeof import('../../api/transportOrdersApi')>()),
  getOrderCustomerChangeImpact: api.getOrderCustomerChangeImpact,
  changeOrderCustomer: api.changeOrderCustomer,
}))

const clientSa: CustomerListItem = {
  id: 'cust-real', customerNumber: 'KL-9', name: 'Client SA', city: 'Liège', countryCode: 'BE',
  categoryName: null, isActive: true, isBlocked: false,
}

function impact(overrides: Partial<OrderCustomerChangeImpact> = {}): OrderCustomerChangeImpact {
  return {
    orderId: 'order-1', orderNumber: 'ORD-1',
    currentCustomerId: 'cust-tmp', currentCustomerName: 'VCB tijdelijk',
    newCustomerId: 'cust-real', newCustomerName: 'Client SA',
    blockedReason: null,
    automaticLinesInvalidated: 2, manualLinesKept: 1, adjustedLinesFlaggedForReview: 1, needsPricingReview: true,
    newLegalEntityId: 'ent-b', legalEntityChanges: true, newInvoiceLanguage: 'fr', newVatTreatment: 'ReverseCharge',
    stopsKept: 2, goodsKept: 3, documentsKept: 0, draftInvoiceLinesReleased: 1,
    owningDossierId: null, owningDossierNumber: null,
    ...overrides,
  }
}

function renderDialog(onChanged = vi.fn()) {
  render(
    <MemoryRouter>
      <OrderCustomerChangeDialog
        orderId="order-1"
        orderNumber="ORD-1"
        currentCustomerId="cust-tmp"
        currentCustomerName="VCB tijdelijk"
        onClose={vi.fn()}
        onChanged={onChanged}
      />
    </MemoryRouter>,
  )
  return onChanged
}

async function pickClientSa() {
  await userEvent.type(screen.getByPlaceholderText('Zoek op naam of klantnummer…'), 'cli')
  await userEvent.click(await screen.findByRole('button', { name: /Client SA/ }))
}

beforeEach(() => {
  api.searchCustomers.mockReset().mockResolvedValue({ items: [clientSa], totalCount: 1, page: 1, pageSize: 10 })
  api.getOrderCustomerChangeImpact.mockReset().mockResolvedValue(impact())
  api.changeOrderCustomer.mockReset().mockResolvedValue(impact())
})

describe('OrderCustomerChangeDialog', () => {
  it('searches server-side, shows the backend impact and only applies with a reason', async () => {
    const onChanged = renderDialog()
    await pickClientSa()

    await waitFor(() => expect(api.getOrderCustomerChangeImpact).toHaveBeenCalledWith('order-1', 'cust-real'))
    await screen.findByText('2 automatische prijslijn(en) van de oude klant vervallen.')
    expect(screen.getByText("1 aangepaste lijn(en) blijven staan, gemarkeerd als 'te controleren'.")).toBeInTheDocument()
    expect(screen.getByText('1 lijn(en) op een conceptfactuur worden losgekoppeld.')).toBeInTheDocument()
    expect(screen.getByText('Blijft ongewijzigd: 2 stop(s), 3 goederenlijn(en), 0 document(en).')).toBeInTheDocument()

    const confirm = screen.getByRole('button', { name: 'Klant wijzigen' })
    expect(confirm).toBeDisabled()

    await userEvent.type(screen.getByLabelText(/Reden/), 'Echte klant bekend')
    expect(confirm).toBeEnabled()
    await userEvent.click(confirm)

    await waitFor(() => expect(api.changeOrderCustomer).toHaveBeenCalledWith('order-1', 'cust-real', 'Echte klant bekend'))
    expect(onChanged).toHaveBeenCalled()
  })

  it('shows the blocking reason with a dossier link and never enables the action', async () => {
    api.getOrderCustomerChangeImpact.mockResolvedValue(impact({
      blockedReason: 'Deze order volgt de klant van dossier D-1.',
      owningDossierId: 'dos-1', owningDossierNumber: 'D-1',
    }))
    renderDialog()
    await pickClientSa()

    await screen.findByText('Deze wijziging is niet mogelijk')
    expect(screen.getByRole('link', { name: 'Open dossier D-1' })).toHaveAttribute('href', '/dossiers/dos-1')
    await userEvent.type(screen.getByLabelText(/Reden/), 'x')
    expect(screen.getByRole('button', { name: 'Klant wijzigen' })).toBeDisabled()
    expect(api.changeOrderCustomer).not.toHaveBeenCalled()
  })

  it('marks the current customer as not selectable in the results', async () => {
    api.searchCustomers.mockResolvedValue({
      items: [{ ...clientSa, id: 'cust-tmp', name: 'VCB tijdelijk', customerNumber: 'TMP' }],
      totalCount: 1, page: 1, pageSize: 10,
    })
    renderDialog()
    await userEvent.type(screen.getByPlaceholderText('Zoek op naam of klantnummer…'), 'vcb')
    const row = await screen.findByRole('button', { name: /VCB tijdelijk/ })
    expect(row).toBeDisabled()
    expect(row).toHaveTextContent('huidig')
  })
})
