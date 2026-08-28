import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { DossierCustomerChangeDialog } from '../components/DossierCustomerChangeDialog'
import type { DossierCustomerChangeImpact } from '../api/dossiersApi'
import { dossierDetail } from './fixtures'

const api = vi.hoisted(() => ({
  searchCustomers: vi.fn(),
  getLegalEntityOptions: vi.fn(),
  getDossierCustomerChangeImpact: vi.fn(),
  changeDossierCustomer: vi.fn(),
}))
vi.mock('../../customers/api/customersApi', async (orig) => ({
  ...(await orig<typeof import('../../customers/api/customersApi')>()),
  searchCustomers: api.searchCustomers,
}))
vi.mock('../../legal-entities/api/legalEntitiesApi', () => ({ getLegalEntityOptions: api.getLegalEntityOptions }))
vi.mock('../api/dossiersApi', async (orig) => ({
  ...(await orig<typeof import('../api/dossiersApi')>()),
  getDossierCustomerChangeImpact: api.getDossierCustomerChangeImpact,
  changeDossierCustomer: api.changeDossierCustomer,
}))

const orderImpact = (orderNumber: string, blockedReason: string | null = null) => ({
  orderId: `o-${orderNumber}`, orderNumber, currentCustomerId: 'cust-tmp', currentCustomerName: 'VCB tijdelijk',
  newCustomerId: 'cust-real', newCustomerName: 'Client SA', blockedReason,
  automaticLinesInvalidated: 1, manualLinesKept: 0, adjustedLinesFlaggedForReview: 0, needsPricingReview: false,
  newLegalEntityId: 'ent-b', legalEntityChanges: true, newInvoiceLanguage: 'fr', newVatTreatment: 'ReverseCharge',
  stopsKept: 2, goodsKept: 1, documentsKept: 0, draftInvoiceLinesReleased: 0, owningDossierId: 'dos-1', owningDossierNumber: 'D-1',
})

function impact(overrides: Partial<DossierCustomerChangeImpact> = {}): DossierCustomerChangeImpact {
  return {
    dossierId: 'dos-1', dossierNumber: 'D-1', currentCustomerId: 'cust-tmp', currentCustomerName: 'VCB tijdelijk',
    newCustomerId: 'cust-real', newCustomerName: 'Client SA', blockedReason: null,
    newLegalEntityId: 'ent-b', newInvoiceLanguage: 'fr', newVatTreatment: 'ReverseCharge',
    orders: [orderImpact('ORD-1'), orderImpact('ORD-2')], ordersLeftOnOtherCustomer: ['ORD-3'],
    ...overrides,
  }
}

const dossier = dossierDetail({ id: 'dos-1', dossierNumber: 'D-1', customerId: 'cust-tmp', customerName: 'VCB tijdelijk', version: 'v7' })

beforeEach(() => {
  api.searchCustomers.mockReset().mockResolvedValue({
    items: [{ id: 'cust-real', customerNumber: 'KL-9', name: 'Client SA', city: null, countryCode: 'BE', categoryName: null, isActive: true, isBlocked: false }],
    totalCount: 1, page: 1, pageSize: 10,
  })
  api.getLegalEntityOptions.mockReset().mockResolvedValue([{ id: 'ent-b', displayName: 'Entiteit B', vatNumber: null, isDefault: false, isActive: true }])
  api.getDossierCustomerChangeImpact.mockReset().mockResolvedValue(impact())
  api.changeDossierCustomer.mockReset().mockResolvedValue({ ...dossier, customerId: 'cust-real', customerName: 'Client SA' })
})

async function pickClientSa() {
  await userEvent.type(screen.getByPlaceholderText('Zoek op naam of klantnummer…'), 'cli')
  await userEvent.click(await screen.findByRole('button', { name: /Client SA/ }))
}

describe('DossierCustomerChangeDialog', () => {
  it('lists every linked order that moves, the ones left alone, and applies with reason + version', async () => {
    const onChanged = vi.fn()
    render(
      <MemoryRouter>
        <DossierCustomerChangeDialog dossier={dossier} onClose={vi.fn()} onChanged={onChanged} />
      </MemoryRouter>,
    )
    await pickClientSa()

    await screen.findByText('Gekoppelde orders die mee verhuizen (2)')
    expect(screen.getByText('ORD-1')).toBeInTheDocument()
    expect(screen.getByText('ORD-2')).toBeInTheDocument()
    expect(screen.getByText('Blijven op hun eigen klant: ORD-3')).toBeInTheDocument()
    expect(screen.getByText('Facturerende entiteit van het dossier wordt: Entiteit B')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/Reden/), 'Echte klant')
    await userEvent.click(screen.getByRole('button', { name: 'Klant wijzigen' }))

    await waitFor(() => expect(api.changeDossierCustomer).toHaveBeenCalledWith('dos-1', 'cust-real', 'Echte klant', 'v7'))
    expect(onChanged).toHaveBeenCalledWith(expect.objectContaining({ customerId: 'cust-real' }))
  })

  it('one blocked order blocks the whole dossier', async () => {
    api.getDossierCustomerChangeImpact.mockResolvedValue(impact({
      blockedReason: 'Order ORD-2: Deze order staat op een verzonden factuur.',
      orders: [orderImpact('ORD-1'), orderImpact('ORD-2', 'Deze order staat op een verzonden factuur.')],
    }))
    render(
      <MemoryRouter>
        <DossierCustomerChangeDialog dossier={dossier} onClose={vi.fn()} onChanged={vi.fn()} />
      </MemoryRouter>,
    )
    await pickClientSa()

    await screen.findByText('Order ORD-2: Deze order staat op een verzonden factuur.')
    await userEvent.type(screen.getByLabelText(/Reden/), 'x')
    expect(screen.getByRole('button', { name: 'Klant wijzigen' })).toBeDisabled()
  })
})
